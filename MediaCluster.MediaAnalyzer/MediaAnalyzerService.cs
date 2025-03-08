using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MediaCluster.Common;
using MediaCluster.Common.Models.Configuration;
using MediaCluster.MediaAnalyzer.Models;
using MediaCluster.MediaAnalyzer.Models.External;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaCluster.MediaAnalyzer
{
    
    /// <summary>
    /// Implementation of the media analyzer service using FFprobe
    /// </summary>
    public class MediaAnalyzerService : IMediaAnalyzerService
    {
        private readonly ILogger<MediaAnalyzerService> _logger;
        private readonly MediaAnalyzerConfig _config;
        
        /// <summary>
        /// Create a new media analyzer service
        /// </summary>
        public MediaAnalyzerService(IOptions<MediaAnalyzerConfig> config, ILogger<MediaAnalyzerService> logger)
        {
            _logger = logger;
            _config = config.Value;
            
            // Validate that FFprobe exists and is executable
            ValidateFFprobe();
            
            _logger.LogInformation("MediaAnalyzerService initialized with FFprobe path: {FFprobePath}", _config.FFprobePath);
        }
        
        /// <summary>
        /// Validate that FFprobe exists and is executable
        /// </summary>
        private void ValidateFFprobe()
        {
            try
            {
                // Check if FFprobe is available
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _config.FFprobePath,
                        Arguments = "-version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                if (process.ExitCode != 0)
                {
                    _logger.LogWarning("FFprobe validation failed with exit code {ExitCode}", process.ExitCode);
                }
                else
                {
                    _logger.LogDebug("FFprobe validation successful: {Version}", output.Split('\n')[0]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FFprobe validation failed: {Message}. Media analysis will not be available.", ex.Message);
            }
        }

        /// <inheritdoc/>
        public async Task<MediaInfo> AnalyzeMediaAsync(byte[] fileContent, string fileName,
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Analyzing media from byte array: {FileName}, length: {Length} bytes", fileName,
                fileContent.Length);

            using var ffprobeProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = "-v quiet -print_format json -show_format -show_streams -",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            // Create cancellation token to prevent potential deadlocks
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(30)); // Set timeout to 30 seconds

            try
            {
                // Start the process
                ffprobeProcess.Start();

                // Create tasks for reading stdout and stderr
                var outputTask = ffprobeProcess.StandardOutput.ReadToEndAsync();
                var errorTask = ffprobeProcess.StandardError.ReadToEndAsync();

                // Write input data in a separate task
                var inputTask = Task.Run(async () =>
                {
                    try
                    {
                        await ffprobeProcess.StandardInput.BaseStream.WriteAsync(fileContent, 0, fileContent.Length, cts.Token);
                        await ffprobeProcess.StandardInput.BaseStream.FlushAsync(cts.Token);
                    }
                    catch (IOException ex) when (ex.Message.Contains("pipe") || ex.Message.Contains("broken"))
                    {
                        // Pipe errors are expected if ffprobe closes the connection early
                        // This is normal behavior if ffprobe has read enough data
                    }
                    finally
                    {
                        // Always close the input stream to signal EOF
                        ffprobeProcess.StandardInput.Close();
                    }
                });

                // Wait for all tasks to complete or timeout
                await Task.WhenAll(
                    inputTask,
                    Task.Run(() => ffprobeProcess.WaitForExit())
                ).WaitAsync(cts.Token);

                // Get the output and error results
                string output = await outputTask;
                string errorOutput = await errorTask;

                if (!string.IsNullOrWhiteSpace(errorOutput))
                {
                    Console.WriteLine($"ffprobe Error: {errorOutput}");
                }

                if (string.IsNullOrWhiteSpace(output))
                {
                    Console.WriteLine("Error: ffprobe returned empty output.");
                    return null;
                }
                
                return await Task.Run(() => ParseFFprobeOutput(output), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Error: ffprobe operation timed out.");

                // Make sure to kill the process if it's still running
                if (!ffprobeProcess.HasExited)
                {
                    try
                    {
                        ffprobeProcess.Kill(true);
                    }
                    catch
                    {
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running ffprobe: {ex.Message}");
                return null;
            }

            return null;
        }

        /// <inheritdoc/>
        public async Task<MediaInfo> AnalyzeMediaAsync(string filePath, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Analyzing media file: {FilePath}", filePath);
            
            try
            {
                // Build FFprobe arguments
                var args = new StringBuilder();
                args.Append("-v error ");
                args.Append("-show_format -show_streams ");
                args.Append("-print_format json ");
                args.AppendFormat("\"{0}\"", filePath);
                
                // Run FFprobe as a process
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _config.FFprobePath,
                        Arguments = args.ToString(),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                var tcs = new TaskCompletionSource<string>();
                
                // Use the cancellation token
                using var ct = cancellationToken.Register(() => 
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                    catch
                    {
                        // Ignore errors when killing the process
                    }
                    
                    tcs.TrySetCanceled();
                });
                
                // Collect output asynchronously
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();
                
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                };
                
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                };
                
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                // Wait for process completion or cancellation
                await process.WaitForExitAsync(cancellationToken);
                
                if (process.ExitCode != 0)
                {
                    var error = errorBuilder.ToString();
                    _logger.LogError("FFprobe exited with code {ExitCode}, error: {Error}", process.ExitCode, error);
                    throw new InvalidOperationException($"FFprobe analysis failed with exit code {process.ExitCode}: {error}");
                }
                
                // Parse the JSON output
                string output = outputBuilder.ToString();
                
                _logger.LogDebug("FFprobe analysis complete for {FilePath}, output length: {Length} characters", filePath, output.Length);
                
                return await Task.Run(() => ParseFFprobeOutput(output), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("FFprobe analysis cancelled for {FilePath}", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing media file {FilePath}: {Message}", filePath, ex.Message);
                throw;
            }
        }
        
        /// <summary>
        /// Parse FFprobe JSON output into a MediaInfo object
        /// </summary>
        private MediaInfo ParseFFprobeOutput(string json)
        {
            try
            {
                // Deserialize the JSON output
                var ResultDto = JsonSerializer.Deserialize<ResultDto>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (ResultDto == null)
                {
                    throw new InvalidOperationException("Failed to parse FFprobe output");
                }
                
                // Create media info object
                var mediaInfo = new MediaInfo
                {
                    FormatName = ResultDto.Format?.FormatName,
                    FormatLongName = ResultDto.Format?.FormatLongName,
                    Duration = ResultDto.Format?.Duration,
                    BitRate = ResultDto.Format?.BitRate,
                    Size = ResultDto.Format?.Size,
                    StartTime = ResultDto.Format?.StartTime
                };
                
                // Process video streams
                var videoStreams = ResultDto.Streams?
                    .Where(s => s.CodecType?.ToLower() == "video" && s.DispositionIsNotAttachment())
                    .ToList();
                
                if (videoStreams != null && videoStreams.Count > 0)
                {
                    mediaInfo.VideoStreams = new List<VideoStream>();
                    
                    foreach (var stream in videoStreams)
                    {
                        var videoStream = new VideoStream
                        {
                            Index = stream.Index,
                            CodecName = stream.CodecName,
                            Profile = stream.Profile,
                            Width = stream.Width,
                            Height = stream.Height,
                            DisplayAspectRatio = stream.DisplayAspectRatio,
                            Duration = stream.Duration,
                            FrameRate = FormatFrameRate(stream.FrameRate),
                            BitDepth = GetBitDepth(stream.PixelFormat),
                            IsHDR = IsHdrStream(stream)
                        };
                        
                        // Get language tag if available
                        if (stream.Tags != null && stream.Tags.TryGetValue("language", out var language))
                        {
                            videoStream.Language = language;
                        }
                        
                        mediaInfo.VideoStreams.Add(videoStream);
                    }
                }
                
                // Process audio streams
                var audioStreams = ResultDto.Streams?
                    .Where(s => s.CodecType?.ToLower() == "audio")
                    .ToList();
                
                if (audioStreams != null && audioStreams.Count > 0)
                {
                    mediaInfo.AudioStreams = new List<AudioStream>();
                    
                    foreach (var stream in audioStreams)
                    {
                        var audioStream = new AudioStream
                        {
                            Index = stream.Index,
                            CodecName = stream.CodecName,
                            SampleRate = stream.SampleRate,
                            Channels = stream.Channels,
                            ChannelLayout = (stream.Tags?.TryGetValue("channel_layout", out var layout) ?? false) ? layout : null,
                            BitsPerRawSample = stream.BitsPerRawSample,
                            HearingImpaired = stream.Disposition?.HearingImpaired == 1,
                            VisualImpaired = stream.Disposition?.VisualImpaired == 1
                        };
                        
                        // Get language tag if available
                        if (stream.Tags != null && stream.Tags.TryGetValue("language", out var language))
                        {
                            audioStream.Language = language;
                        }
                        
                        mediaInfo.AudioStreams.Add(audioStream);
                    }
                }
                
                // Process subtitle streams
                var subtitleStreams = ResultDto.Streams?
                    .Where(s => s.CodecType?.ToLower() == "subtitle")
                    .ToList();
                
                if (subtitleStreams != null && subtitleStreams.Count > 0)
                {
                    mediaInfo.SubtitleStreams = new List<SubtitleStream>();
                    
                    foreach (var stream in subtitleStreams)
                    {
                        var subtitleStream = new SubtitleStream
                        {
                            Index = stream.Index,
                            Format = stream.CodecName
                        };
                        
                        // Get language tag if available
                        if (stream.Tags != null && stream.Tags.TryGetValue("language", out var language))
                        {
                            subtitleStream.Language = language;
                        }
                        
                        mediaInfo.SubtitleStreams.Add(subtitleStream);
                    }
                }
                
                _logger.LogDebug("Parsed media info: {VideoStreams} video, {AudioStreams} audio, {SubtitleStreams} subtitle streams",
                    mediaInfo.VideoStreams?.Count ?? 0,
                    mediaInfo.AudioStreams?.Count ?? 0,
                    mediaInfo.SubtitleStreams?.Count ?? 0);
                
                return mediaInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing FFprobe output: {Message}", ex.Message);
                throw;
            }
        }
        
        /// <summary>
        /// Check if a video stream is HDR
        /// </summary>
        private bool IsHdrStream(StreamDto stream)
        {
            // Check for HDR10, HDR10+, HLG, or Dolby Vision
            if (stream.PixelFormat?.Contains("p10") == true)
            {
                // Check for color_transfer metadata
                if (stream.Tags != null)
                {
                    if (stream.Tags.TryGetValue("color_transfer", out var transfer))
                    {
                        if (transfer == "smpte2084" || transfer == "arib-std-b67")
                        {
                            return true;
                        }
                    }
                    
                    // Check for HDR metadata in stream side data
                    if (stream.Tags.TryGetValue("side_data_list", out var sideData) && 
                        sideData.Contains("HDR"))
                    {
                        return true;
                    }
                    
                    // Check for Dolby Vision
                    if (stream.Tags.Any(t => t.Key.Contains("dovi") || 
                                              t.Value.Contains("dovi") || 
                                              t.Key.Contains("dolby") || 
                                              t.Value.Contains("dolby")))
                    {
                        return true;
                    }
                }
            }
            
            return false;
        }
        
        /// <inheritdoc/>
        public bool IsMediaFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;
                
            // Get file extension
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            
            // Check if it's in the list of media extensions
            return _config.MediaFileExtensions.Contains(extension);
        }
        
        /// <summary>
        /// Get bit depth from pixel format
        /// </summary>
        private int? GetBitDepth(string? pixelFormat)
        {
            if (string.IsNullOrEmpty(pixelFormat))
                return null;
                
            if (pixelFormat.Contains("p10"))
                return 10;
                
            if (pixelFormat.Contains("p12"))
                return 12;
                
            return 8; // Default for most formats
        }
        
        /// <summary>
        /// Format frame rate from a fraction string (e.g. "24000/1001")
        /// </summary>
        private string? FormatFrameRate(string? frameRate)
        {
            if (string.IsNullOrEmpty(frameRate))
                return null;
                
            if (frameRate.Contains('/'))
            {
                var parts = frameRate.Split('/');
                if (parts.Length == 2 && double.TryParse(parts[0], out double numerator) && double.TryParse(parts[1], out double denominator))
                {
                    if (denominator > 0)
                    {
                        double fps = numerator / denominator;
                        
                        // Handle common framerates
                        if (Math.Abs(fps - 23.976) < 0.01)
                            return "23.976 fps";
                            
                        if (Math.Abs(fps - 24) < 0.01)
                            return "24 fps";
                            
                        if (Math.Abs(fps - 25) < 0.01)
                            return "25 fps";
                            
                        if (Math.Abs(fps - 29.97) < 0.01)
                            return "29.97 fps";
                            
                        if (Math.Abs(fps - 30) < 0.01)
                            return "30 fps";
                            
                        if (Math.Abs(fps - 50) < 0.01)
                            return "50 fps";
                            
                        if (Math.Abs(fps - 59.94) < 0.01)
                            return "59.94 fps";
                            
                        if (Math.Abs(fps - 60) < 0.01)
                            return "60 fps";
                            
                        return $"{fps:F2} fps";
                    }
                }
            }
            
            // If we couldn't parse it as a fraction, return as is
            return $"{frameRate} fps";
        }
    }
    
    /// <summary>
    /// Extension methods for FFProbe result processing
    /// </summary>
    internal static class FFProbeExtensions
    {
        /// <summary>
        /// Check if a stream is not an attachment (covers image thumbnails etc.)
        /// </summary>
        public static bool DispositionIsNotAttachment(this StreamDto stream)
        {
            if (stream.Disposition == null)
                return true;
                
            return stream.Disposition.AttachedPic != 1;
        }
    }
}
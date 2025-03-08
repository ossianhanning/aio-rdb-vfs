using System.Net;
using MediaCluster.Common;
using MediaCluster.RealDebrid.SharedModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaCluster.CacheSystem.Http;

/// <summary>
/// Simplified HTTP handler for serving file content with support for range requests
/// </summary>
public class RangeRequestHandler
{
    private readonly ILogger<RangeRequestHandler> _logger;
    private readonly ICacheProvider _cacheProvider;
    private readonly IVirtualFileSystem _fileSystem;
    private readonly CacheConfig _config;

    public RangeRequestHandler(
        ILogger<RangeRequestHandler> logger,
        ICacheProvider cacheProvider,
        IVirtualFileSystem fileSystem,
        IOptions<CacheConfig> options)
    {
        _logger = logger;
        _cacheProvider = cacheProvider;
        _fileSystem = fileSystem;
        _config = options.Value;
    }

    /// <summary>
    /// Handle an HTTP request with range support
    /// </summary>
    public async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        
        try
        {
            // Get the file path from the URL
            var urlPath = WebUtility.UrlDecode(request.Url?.AbsolutePath ?? "");
            
            // Check if file exists
            if (!_fileSystem.FileExists(urlPath))
            {
                _logger.LogWarning("File not found: {Path}", urlPath);
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }
            
            // Get the file node
            var node = _fileSystem.FindNode(urlPath);
            if (node is not IVirtualFile file)
            {
                _logger.LogWarning("Path is not a file: {Path}", urlPath);
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }
            
            var remoteFile = file.RemoteFile;
            var fileSize = (long)remoteFile.Size;
            
            // Set content type based on file extension
            response.ContentType = GetContentType(urlPath);
            
            // Get range header
            var rangeHeader = request.Headers["Range"];
            
            if (string.IsNullOrEmpty(rangeHeader))
            {
                // No range header, serve the whole file
                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentLength64 = fileSize;
                
                // For large files, return a 'too large' error instead of serving the whole file
                if (fileSize > 100 * 1024 * 1024) // 100 MB
                {
                    _logger.LogWarning("File too large for full download: {Path} ({Size} bytes)", urlPath, fileSize);
                    response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
                    response.AddHeader("Content-Range", $"bytes */{fileSize}");
                    return;
                }
                
                // Serve the whole file
                await ServeRangeAsync(remoteFile, 0, (int)fileSize, response.OutputStream);
            }
            else
            {
                // Parse range header
                if (!TryParseRangeHeader(rangeHeader, fileSize, out var ranges))
                {
                    _logger.LogWarning("Invalid range header: {Header}", rangeHeader);
                    response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
                    response.AddHeader("Content-Range", $"bytes */{fileSize}");
                    return;
                }
                
                // We only support a single range for now
                var range = ranges[0];
                var rangeLength = (int)(range.End - range.Start + 1);
                
                _logger.LogDebug("Serving range {Start}-{End} ({Length} bytes) for {Path}",
                    range.Start, range.End, rangeLength, urlPath);
                
                // Set response headers
                response.StatusCode = (int)HttpStatusCode.PartialContent;
                response.ContentLength64 = rangeLength;
                response.AddHeader("Content-Range", $"bytes {range.Start}-{range.End}/{fileSize}");
                response.AddHeader("Accept-Ranges", "bytes");
                
                // Serve the requested range
                await ServeRangeAsync(remoteFile, range.Start, rangeLength, response.OutputStream);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request: {Path}", request.Url?.AbsolutePath);
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
        }
        finally
        {
            response.Close();
        }
    }
    
    /// <summary>
    /// Serve a range of a file to the output stream
    /// </summary>
    private async Task ServeRangeAsync(RemoteFile remoteFile, long start, int length, Stream outputStream)
    {
        // Use a 1MB buffer for output stream writes
        const int bufferSize = 1024 * 1024;
        
        var remaining = length;
        var position = start;
        
        while (remaining > 0)
        {
            // Calculate how much to read in this chunk
            var toRead = Math.Min(remaining, bufferSize);
            
            // Read from cache
            var data = await _cacheProvider.ReadAsync(remoteFile, position, toRead);
            
            // Write to output stream
            await outputStream.WriteAsync(data);
            
            // Update position and remaining count
            position += toRead;
            remaining -= toRead;
        }
    }
    
    /// <summary>
    /// Try to parse a range header
    /// </summary>
    private bool TryParseRangeHeader(string rangeHeader, long fileSize, out List<(long Start, long End)> ranges)
    {
        ranges = new List<(long Start, long End)>();
        
        // Range header format: "bytes=0-499" or "bytes=500-999" or "bytes=500-"
        if (!rangeHeader.StartsWith("bytes="))
            return false;
            
        // Parse each range, we only support one range for now
        var rangeValue = rangeHeader.Substring("bytes=".Length);
        var rangeParts = rangeValue.Split(',');
        
        foreach (var rangePart in rangeParts)
        {
            var range = rangePart.Trim().Split('-');
            
            if (range.Length != 2)
                return false;
                
            if (string.IsNullOrEmpty(range[0]))
            {
                // Suffix range: "-500" means last 500 bytes
                if (!long.TryParse(range[1], out var suffixLength) || suffixLength <= 0)
                    return false;
                    
                var start = Math.Max(0, fileSize - suffixLength);
                ranges.Add((start, fileSize - 1));
            }
            else if (string.IsNullOrEmpty(range[1]))
            {
                // Prefix range: "500-" means from byte 500 to end
                if (!long.TryParse(range[0], out var start) || start < 0)
                    return false;
                    
                if (start >= fileSize)
                    return false;
                    
                ranges.Add((start, fileSize - 1));
            }
            else
            {
                // Range with start and end: "500-999"
                if (!long.TryParse(range[0], out var start) || !long.TryParse(range[1], out var end))
                    return false;
                    
                if (start < 0 || end < start || start >= fileSize)
                    return false;
                    
                // Clamp end to file size
                end = Math.Min(end, fileSize - 1);
                
                ranges.Add((start, end));
            }
        }
        
        return ranges.Count > 0;
    }
    
    /// <summary>
    /// Get the content type for a file
    /// </summary>
    private string GetContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        
        return extension switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            _ => "application/octet-stream"
        };
    }
}
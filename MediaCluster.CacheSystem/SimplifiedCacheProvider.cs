using System.Collections.Concurrent;
using System.Diagnostics;
using MediaCluster.RealDebrid.SharedModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaCluster.CacheSystem;

public class SimplifiedCacheProvider : ICacheProvider, IDisposable
{
    private readonly ILogger<SimplifiedCacheProvider> _logger;
    private readonly CacheConfig _config;
    private readonly HttpClient _httpClient;
    private readonly CacheLogger _cacheLogger;
    private readonly CacheStatistics _statistics = new();
    private readonly SemaphoreSlim _globalDownloadSemaphore;
    
    // Track active file operations
    private readonly ConcurrentDictionary<string, FileOperationContext> _fileOperations = new();
    
    // Cache eviction related variables
    private readonly SemaphoreSlim _evictionLock = new(1, 1);
    private DateTime _lastEvictionCheck = DateTime.UtcNow.AddMinutes(-10); // Force check on first access
    
    /// <summary>
    /// Context for a file's operations
    /// </summary>
    private class FileOperationContext
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public CancellationTokenSource? DownloadCts { get; set; }
        public Task? CurrentDownloadTask { get; set; }
        public int CurrentChunkIndex { get; set; } = -1; // -1 means no active download
        public DateTime LastAccess { get; set; } = DateTime.UtcNow;
    }
    
    public SimplifiedCacheProvider(
        ILogger<SimplifiedCacheProvider> logger,
        IOptions<CacheConfig> options,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _config = options.Value;
        _httpClient = httpClientFactory.CreateClient("CacheClient");
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.RequestTimeoutSeconds);
        _globalDownloadSemaphore = new SemaphoreSlim(_config.MaxTotalConcurrentDownloads, _config.MaxTotalConcurrentDownloads);
        _statistics.MaxCacheSize = _config.MaxCacheSize;
        
        _cacheLogger = new CacheLogger(
            logger,
            _config.LogsPath,
            _config.EnableLogging);
        
        // Ensure cache directory exists
        Directory.CreateDirectory(_config.CachePath);
        
        // Load initial cache status
        InitializeCacheStatus();
        
        _cacheLogger.LogInfo($"SimplifiedCacheProvider initialized with cache path: {_config.CachePath}, " + 
                             $"max size: {FormatBytes(_config.MaxCacheSize)}, chunk size: {FormatBytes(_config.ChunkSize)}");
    }
    
    /// <summary>
    /// Initialize cache status from disk
    /// </summary>
    private void InitializeCacheStatus()
    {
        try
        {
            if (!Directory.Exists(_config.CachePath))
            {
                return;
            }
            
            var fileCount = 0;
            var chunkCount = 0;
            long totalSize = 0;
            
            foreach (var fileDir in Directory.GetDirectories(_config.CachePath))
            {
                fileCount++;
                var chunks = Directory.GetFiles(fileDir, "*.bin");
                chunkCount += chunks.Length;
                
                foreach (var chunk in chunks)
                {
                    var fileInfo = new FileInfo(chunk);
                    totalSize += fileInfo.Length;
                }
            }
            
            for (int i = 0; i < fileCount; i++)
            {
                _statistics.IncrementFileCount();
            }

            for (int i = 0; i < chunkCount; i++)
            {
                _statistics.IncrementChunkCount();
            }

            _statistics.AddToCacheSize(totalSize);
            
            _cacheLogger.LogInfo($"Cache initialized with {fileCount} files, {chunkCount} chunks, " +
                                $"{FormatBytes(totalSize)} / {FormatBytes(_config.MaxCacheSize)} " +
                                $"({_statistics.CacheUtilizationPercent:F1}%)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing cache status");
        }
    }
    
    /// <summary>
    /// Generate a file ID from the remote file
    /// </summary>
    private string GetFileId(RemoteFile remoteFile)
    {
        var torrentHash = remoteFile.Parent?.TorrentHash ?? "unknown";
        return $"{torrentHash}_{remoteFile.FileId}";
    }
    
    /// <summary>
    /// Get the file cache directory
    /// </summary>
    private string GetFileCacheDirectory(string fileId)
    {
        return Path.Combine(_config.CachePath, fileId);
    }
    
    /// <summary>
    /// Calculate chunk index from file offset
    /// </summary>
    private int GetChunkIndex(long offset)
    {
        return (int)(offset / _config.ChunkSize);
    }
    
    /// <summary>
    /// Calculate chunk's start offset from index
    /// </summary>
    private long GetChunkOffset(int chunkIndex)
    {
        return (long)chunkIndex * _config.ChunkSize;
    }
    
    /// <summary>
    /// Get the chunk file path
    /// </summary>
    private string GetChunkPath(string fileId, int chunkIndex)
    {
        return Path.Combine(GetFileCacheDirectory(fileId), $"{chunkIndex:D5}.bin");
    }
    
    /// <summary>
    /// Get or create file operation context
    /// </summary>
    private FileOperationContext GetFileContext(string fileId)
    {
        return _fileOperations.GetOrAdd(fileId, _ => new FileOperationContext());
    }
    
    /// <summary>
    /// Check if a chunk exists in cache
    /// </summary>
    private bool ChunkExists(string fileId, int chunkIndex)
    {
        return File.Exists(GetChunkPath(fileId, chunkIndex));
    }
    
    /// <summary>
    /// Format bytes to human readable form
    /// </summary>
    private string FormatBytes(long bytes)
    {
        const long kb = 1024;
        const long mb = kb * 1024;
        const long gb = mb * 1024;
        
        return bytes switch
        {
            >= gb => $"{bytes / (double)gb:F2} GB",
            >= mb => $"{bytes / (double)mb:F2} MB",
            >= kb => $"{bytes / (double)kb:F2} KB",
            _ => $"{bytes} bytes"
        };
    }
    
    /// <summary>
    /// Download a chunk from remote server
    /// </summary>
    private async Task DownloadChunkAsync(
        RemoteFile remoteFile,
        string fileId,
        int chunkIndex,
        CancellationToken cancellationToken)
    {
        var context = GetFileContext(fileId);
        
        // Set up the download context
        context.CurrentChunkIndex = chunkIndex;
        context.DownloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var downloadToken = context.DownloadCts.Token;
        
        // Calculate chunk start and length
        var chunkOffset = GetChunkOffset(chunkIndex);
        var chunkLength = (int)Math.Min(_config.ChunkSize, remoteFile.Size - chunkOffset);
        
        if (chunkLength <= 0)
        {
            // Invalid chunk - past end of file
            context.CurrentChunkIndex = -1;
            context.DownloadCts = null;
            return;
        }
        
        var chunkPath = GetChunkPath(fileId, chunkIndex);
        var chunkDir = Path.GetDirectoryName(chunkPath);
        
        if (!Directory.Exists(chunkDir))
        {
            Directory.CreateDirectory(chunkDir!);
        }
        
        // Use a temporary file during download
        var tempPath = $"{chunkPath}.tmp";
        
        // Ensure we never exceed global download limit
        await _globalDownloadSemaphore.WaitAsync(downloadToken);
        
        try
        {
            _cacheLogger.LogInfo($"Downloading chunk {chunkIndex} for {fileId} " +
                                $"(offset {chunkOffset}, length {chunkLength})");
            
            // Set range header
            using var request = new HttpRequestMessage(HttpMethod.Get, remoteFile.DownloadUrl);
            request.Headers.Add("Range", $"bytes={chunkOffset}-{chunkOffset + chunkLength - 1}");
            
            // Try up to max retries
            for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
            {
                try
                {
                    // Make the request
                    using var response = await _httpClient.SendAsync(request, downloadToken);
                    
                    // Ensure success
                    response.EnsureSuccessStatusCode();
                    
                    // Create the file directory if it doesn't exist
                    Directory.CreateDirectory(Path.GetDirectoryName(chunkPath)!);
                    
                    // Write the response to the temporary file
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fileStream, downloadToken);
                    }
                    
                    // Move the temp file to the final location
                    if (File.Exists(chunkPath))
                    {
                        File.Delete(chunkPath);
                    }
                    
                    File.Move(tempPath, chunkPath);
                    
                    // Update statistics
                    _statistics.IncrementChunkCount();
                    _statistics.AddToCacheSize(chunkLength);
                    
                    _cacheLogger.LogInfo($"Successfully downloaded chunk {chunkIndex} for {fileId}");
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (attempt >= _config.MaxRetries)
                    {
                        _cacheLogger.LogError($"Failed to download chunk {chunkIndex} for {fileId} after {attempt} attempts", ex);
                        throw;
                    }
                    
                    _cacheLogger.LogWarning($"Error downloading chunk {chunkIndex} for {fileId} (attempt {attempt}/{_config.MaxRetries}): {ex.Message}");
                    
                    // Wait before retrying
                    try
                    {
                        await Task.Delay(_config.RetryDelayMs * attempt, downloadToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _cacheLogger.LogInfo($"Download cancelled for chunk {chunkIndex} of {fileId}");
                        throw;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            _cacheLogger.LogInfo($"Download cancelled for chunk {chunkIndex} of {fileId}");
            
            // Clean up temp file if it exists
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch { /* Ignore cleanup errors */ }
            
            throw;
        }
        finally
        {
            _globalDownloadSemaphore.Release();
            
            // Reset download state if this is still the active download
            if (context.CurrentChunkIndex == chunkIndex)
            {
                context.CurrentChunkIndex = -1;
                context.DownloadCts = null;
            }
        }
    }
    
    /// <summary>
    /// Ensure a chunk is available in cache, downloading if necessary
    /// </summary>
    private async Task<bool> EnsureChunkAvailableAsync(
        RemoteFile remoteFile,
        string fileId,
        int chunkIndex,
        CancellationToken cancellationToken)
    {
        // Check if already cached
        if (ChunkExists(fileId, chunkIndex))
        {
            return true;
        }
        
        var context = GetFileContext(fileId);
        
        // If another download is in progress for this file
        if (context.CurrentDownloadTask != null)
        {
            // If it's the chunk we need, just wait for it
            if (context.CurrentChunkIndex == chunkIndex)
            {
                try
                {
                    await context.CurrentDownloadTask;
                    return true;
                }
                catch (OperationCanceledException)
                {
                    // Download was cancelled, we'll start a new one
                }
                catch (Exception ex)
                {
                    _cacheLogger.LogError($"Error waiting for chunk {chunkIndex} download of {fileId}", ex);
                    return false;
                }
            }
            // Otherwise, cancel the current download and start a new one
            else
            {
                try
                {
                    context.DownloadCts?.Cancel();
                    
                    // Wait for the task to complete (should throw OperationCanceledException)
                    try
                    {
                        await context.CurrentDownloadTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected
                    }
                    catch (Exception ex)
                    {
                        _cacheLogger.LogError($"Error cancelling previous download for {fileId}", ex);
                    }
                }
                catch (Exception ex)
                {
                    _cacheLogger.LogError($"Error during download cancellation for {fileId}", ex);
                }
            }
        }
        
        // Start a new download
        try
        {
            var downloadTask = DownloadChunkAsync(remoteFile, fileId, chunkIndex, cancellationToken);
            context.CurrentDownloadTask = downloadTask;
            
            await downloadTask;
            return true;
        }
        catch (OperationCanceledException)
        {
            // Download was cancelled
            return false;
        }
        catch (Exception ex)
        {
            _cacheLogger.LogError($"Error downloading chunk {chunkIndex} for {fileId}", ex);
            return false;
        }
        finally
        {
            context.CurrentDownloadTask = null;
        }
    }
    
    /// <summary>
    /// Start readahead for the next chunk
    /// </summary>
    private void StartReadaheadAsync(
        RemoteFile remoteFile, 
        string fileId,
        int currentChunkIndex,
        CancellationToken cancellationToken)
    {
        var nextChunkIndex = currentChunkIndex + 1;
        var nextChunkOffset = GetChunkOffset(nextChunkIndex);
        
        // Don't readahead past end of file
        if (nextChunkOffset >= remoteFile.Size)
        {
            return;
        }
        
        // Don't readahead if chunk already exists
        if (ChunkExists(fileId, nextChunkIndex))
        {
            return;
        }
        
        var context = GetFileContext(fileId);
        
        // Don't start readahead if we're already downloading or readaheading
        if (context.CurrentDownloadTask != null)
        {
            return;
        }
        
        // Start readahead as a background task (don't await)
        var downloadTask = DownloadChunkAsync(remoteFile, fileId, nextChunkIndex, cancellationToken);
        context.CurrentDownloadTask = downloadTask;
        
        // Use a continuation to handle errors and clean up
        downloadTask.ContinueWith(t => {
            context.CurrentDownloadTask = null;
            
            if (t.IsFaulted)
            {
                if (!(t.Exception?.InnerException is OperationCanceledException))
                {
                    _cacheLogger.LogError($"Error during readahead of chunk {nextChunkIndex} for {fileId}", 
                        t.Exception?.InnerException);
                }
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }
    
    /// <summary>
    /// Read a chunk from cache
    /// </summary>
    private async Task<byte[]> ReadChunkFromCacheAsync(
        string fileId,
        int chunkIndex,
        CancellationToken cancellationToken)
    {
        var chunkPath = GetChunkPath(fileId, chunkIndex);
        return await File.ReadAllBytesAsync(chunkPath, cancellationToken);
    }
    
    /// <summary>
    /// Check if cache eviction is needed and perform it if necessary
    /// </summary>
    private async Task CheckAndPerformCacheEvictionAsync(CancellationToken cancellationToken)
    {
        // Only check periodically to avoid excessive checks
        if ((DateTime.UtcNow - _lastEvictionCheck).TotalMinutes < 5 &&
            _statistics.CurrentCacheSize < _config.MaxCacheSize * 0.9)
        {
            return;
        }
        
        // Try to get the eviction lock, but don't block if another thread is already evicting
        if (!await _evictionLock.WaitAsync(0))
        {
            return;
        }
        
        try
        {
            _lastEvictionCheck = DateTime.UtcNow;
            
            // If we're over 90% of max cache size, evict some chunks
            if (_statistics.CurrentCacheSize > _config.MaxCacheSize * 0.9)
            {
                _cacheLogger.LogInfo($"Cache size {FormatBytes(_statistics.CurrentCacheSize)} exceeds 90% of limit " +
                                    $"({FormatBytes((long) (_config.MaxCacheSize * 0.9))}), starting eviction");
                
                // How much we need to free up - aim to get below 70% usage
                var targetSize = (long)(_config.MaxCacheSize * 0.7);
                var bytesToFree = Math.Max(0, _statistics.CurrentCacheSize - targetSize);
                
                if (bytesToFree > 0)
                {
                    await EvictChunksAsync(bytesToFree, cancellationToken);
                }
            }
        }
        finally
        {
            _evictionLock.Release();
        }
    }
    
    /// <summary>
    /// Evict chunks to free up the specified amount of space
    /// </summary>
    private async Task EvictChunksAsync(long bytesToFree, CancellationToken cancellationToken)
    {
        // Find all chunk files and sort by last access time
        var chunkFiles = new List<(string Path, DateTime LastAccess, long Size)>();
        
        try
        {
            // Scan all file directories in the cache
            foreach (var fileDir in Directory.GetDirectories(_config.CachePath))
            {
                // Skip if the directory is empty
                if (!Directory.Exists(fileDir))
                {
                    continue;
                }
                
                // Get all .bin files (chunks)
                foreach (var chunkPath in Directory.GetFiles(fileDir, "*.bin"))
                {
                    try
                    {
                        var fileInfo = new FileInfo(chunkPath);
                        chunkFiles.Add((chunkPath, fileInfo.LastAccessTime, fileInfo.Length));
                    }
                    catch (Exception ex)
                    {
                        _cacheLogger.LogError($"Error accessing chunk file info: {chunkPath}", ex);
                    }
                }
            }
            
            // Sort by last access time (oldest first)
            chunkFiles = chunkFiles
                .OrderBy(c => c.LastAccess)
                .ToList();
            
            // Start deleting chunks until we've freed enough space
            long freedBytes = 0;
            int deletedCount = 0;
            
            foreach (var (path, _, size) in chunkFiles)
            {
                // Stop if we've freed enough space
                if (freedBytes >= bytesToFree)
                {
                    break;
                }
                
                try
                {
                    // Don't delete chunks that are currently being accessed
                    var fileId = Path.GetFileName(Path.GetDirectoryName(path)!);
                    
                    // Skip if this file is currently being accessed
                    if (_fileOperations.TryGetValue(fileId, out var context) && 
                        context.Lock.CurrentCount == 0)
                    {
                        continue;
                    }
                    
                    // Delete the chunk
                    File.Delete(path);
                    
                    // Update tracking
                    freedBytes += size;
                    deletedCount++;
                    
                    // Update statistics
                    _statistics.AddToCacheSize(-size);
                    _statistics.DecrementChunkCount();
                }
                catch (Exception ex)
                {
                    _cacheLogger.LogError($"Error deleting chunk: {path}", ex);
                }
            }
            
            _cacheLogger.LogInfo($"Cache eviction completed: deleted {deletedCount} chunks, " +
                                $"freed {FormatBytes(freedBytes)}");
            
            // Clean up empty directories
            await CleanupEmptyDirectoriesAsync();
        }
        catch (Exception ex)
        {
            _cacheLogger.LogError("Error during cache eviction", ex);
        }
    }
    
    /// <summary>
    /// Clean up empty directories in the cache
    /// </summary>
    private async Task CleanupEmptyDirectoriesAsync()
    {
        try
        {
            foreach (var fileDir in Directory.GetDirectories(_config.CachePath))
            {
                try
                {
                    // Skip if the directory is empty
                    if (!Directory.Exists(fileDir))
                    {
                        continue;
                    }
                    
                    // Check if directory is empty
                    if (!Directory.EnumerateFileSystemEntries(fileDir).Any())
                    {
                        Directory.Delete(fileDir);
                        _statistics.DecrementFileCount();
                        _cacheLogger.LogInfo($"Removed empty directory: {fileDir}");
                    }
                }
                catch (Exception ex)
                {
                    _cacheLogger.LogError($"Error cleaning up directory: {fileDir}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            _cacheLogger.LogError("Error cleaning up empty directories", ex);
        }
    }
    
    /// <summary>
    /// Read data from a file, using the cache when possible
    /// </summary>
    public async Task<byte[]> ReadAsync(
        RemoteFile remoteFile, 
        long offset, 
        int length, 
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative");
        }
        
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive");
        }
        
        // Clamp length to file size
        if (offset >= (long)remoteFile.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset is beyond end of file");
        }
        
        // Adjust length if it would go past end of file
        if (offset + length > (long)remoteFile.Size)
        {
            length = (int)((long)remoteFile.Size - offset);
            _cacheLogger.LogWarning($"Adjusted read length to {length} to fit within file bounds " +
                                  $"(offset: {offset}, file size: {remoteFile.Size})");
        }
        
        // Edge case: zero-length file
        if (remoteFile.Size == 0 || length == 0)
        {
            return Array.Empty<byte>();
        }
        
        // Get file ID and context
        var fileId = GetFileId(remoteFile);
        var context = GetFileContext(fileId);
        var stopwatch = Stopwatch.StartNew();
        
        // Update last access time
        context.LastAccess = DateTime.UtcNow;
        
        // Check if cache eviction is needed
        await CheckAndPerformCacheEvictionAsync(cancellationToken);
        
        // Calculate which chunks we need
        var startChunkIndex = GetChunkIndex(offset);
        var endChunkIndex = GetChunkIndex(offset + length - 1);
        
        // Acquire the file lock
        await context.Lock.WaitAsync(cancellationToken);
        
        try
        {
            // Fast path for single chunk reads
            if (startChunkIndex == endChunkIndex)
            {
                var chunkOffset = GetChunkOffset(startChunkIndex);
                var offsetInChunk = (int)(offset - chunkOffset);
                
                bool isHit = ChunkExists(fileId, startChunkIndex);
                
                // If the chunk exists, read from cache
                if (isHit)
                {
                    var chunkData = await ReadChunkFromCacheAsync(fileId, startChunkIndex, cancellationToken);
                    
                    // Extract the requested portion
                    var result = new byte[length];
                    Array.Copy(chunkData, offsetInChunk, result, 0, length);
                    
                    // Update statistics
                    _statistics.RecordHit(length);
                    
                    // Check if we should start readahead
                    if (offsetInChunk > _config.ChunkSize - _config.ReadaheadTriggerPosition)
                    {
                        StartReadaheadAsync(remoteFile, fileId, startChunkIndex, cancellationToken);
                    }
                    
                    // Log access
                    _cacheLogger.LogCacheAccess(fileId, offset, length, true, stopwatch.ElapsedMilliseconds);
                    
                    return result;
                }
                
                // Cache miss - download the chunk
                await EnsureChunkAvailableAsync(remoteFile, fileId, startChunkIndex, cancellationToken);
                
                // Read from cache
                var data = await ReadChunkFromCacheAsync(fileId, startChunkIndex, cancellationToken);
                
                // Extract the requested portion
                var resultData = new byte[length];
                Array.Copy(data, offsetInChunk, resultData, 0, length);
                
                // Update statistics
                _statistics.RecordMiss(length);
                
                // Check if we should start readahead
                if (offsetInChunk > _config.ChunkSize - _config.ReadaheadTriggerPosition)
                {
                    StartReadaheadAsync(remoteFile, fileId, startChunkIndex, cancellationToken);
                }
                
                // Log access
                _cacheLogger.LogCacheAccess(fileId, offset, length, false, stopwatch.ElapsedMilliseconds);
                
                return resultData;
            }
            // Multi-chunk read
            else
            {
                // Calculate total result size
                var result = new byte[length];
                var bytesRead = 0;
                
                // Process each chunk
                for (int chunkIndex = startChunkIndex; chunkIndex <= endChunkIndex; chunkIndex++)
                {
                    var chunkOffset = GetChunkOffset(chunkIndex);
                    var readOffset = Math.Max(offset, chunkOffset);
                    var readEnd = Math.Min(offset + length, chunkOffset + _config.ChunkSize);
                    var readLength = (int)(readEnd - readOffset);
                    var offsetInChunk = (int)(readOffset - chunkOffset);
                    
                    bool isHit = ChunkExists(fileId, chunkIndex);
                    
                    // If the chunk exists, read from cache
                    if (isHit)
                    {
                        var chunkData = await ReadChunkFromCacheAsync(fileId, chunkIndex, cancellationToken);
                        
                        // Copy to result
                        Array.Copy(chunkData, offsetInChunk, result, bytesRead, readLength);
                        
                        // Update statistics
                        _statistics.RecordHit(readLength);
                        
                        // Log access
                        _cacheLogger.LogCacheAccess(fileId, readOffset, readLength, true, stopwatch.ElapsedMilliseconds);
                    }
                    else
                    {
                        // Download the chunk
                        await EnsureChunkAvailableAsync(remoteFile, fileId, chunkIndex, cancellationToken);
                        
                        // Read from cache
                        var chunkData = await ReadChunkFromCacheAsync(fileId, chunkIndex, cancellationToken);
                        
                        // Copy to result
                        Array.Copy(chunkData, offsetInChunk, result, bytesRead, readLength);
                        
                        // Update statistics
                        _statistics.RecordMiss(readLength);
                        
                        // Log access
                        _cacheLogger.LogCacheAccess(fileId, readOffset, readLength, false, stopwatch.ElapsedMilliseconds);
                    }
                    
                    // Start readahead for the last chunk if we're near the end
                    if (chunkIndex == endChunkIndex && 
                        offsetInChunk + readLength > _config.ChunkSize - _config.ReadaheadTriggerPosition)
                    {
                        StartReadaheadAsync(remoteFile, fileId, chunkIndex, cancellationToken);
                    }
                    
                    bytesRead += readLength;
                }
                
                return result;
            }
        }
        finally
        {
            context.Lock.Release();
        }
    }
    
    /// <summary>
    /// Invalidate cached data for a file
    /// </summary>
    public async Task InvalidateCacheAsync(RemoteFile remoteFile, CancellationToken cancellationToken = default)
    {
        var fileId = GetFileId(remoteFile);
        var fileCacheDir = GetFileCacheDirectory(fileId);
        
        // Get context and lock
        var context = GetFileContext(fileId);
        
        // Acquire the file lock
        await context.Lock.WaitAsync(cancellationToken);
        
        try
        {
            // Cancel any ongoing downloads
            if (context.DownloadCts != null)
            {
                context.DownloadCts.Cancel();
                context.DownloadCts = null;
            }
            
            if (Directory.Exists(fileCacheDir))
            {
                // Count chunks and size for statistics update
                int chunkCount = 0;
                long totalSize = 0;
                
                // Get all chunk files
                var chunkFiles = Directory.GetFiles(fileCacheDir, "*.bin");
                
                foreach (var chunkFile in chunkFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(chunkFile);
                        totalSize += fileInfo.Length;
                        chunkCount++;
                        
                        // Delete the file
                        File.Delete(chunkFile);
                    }
                    catch (Exception ex)
                    {
                        _cacheLogger.LogError($"Error deleting chunk file during invalidation: {chunkFile}", ex);
                    }
                }
                
                // Delete the directory
                try
                {
                    Directory.Delete(fileCacheDir, true);
                }
                catch (Exception ex)
                {
                    _cacheLogger.LogError($"Error deleting cache directory: {fileCacheDir}", ex);
                }
                
                // Update statistics
                _statistics.AddToCacheSize(-totalSize);
                for (int i = 0; i < chunkCount; i++)
                {
                    _statistics.DecrementChunkCount();
                }
                _statistics.DecrementFileCount();
                
                _cacheLogger.LogInfo($"Invalidated cache for {fileId}: removed {chunkCount} chunks, " +
                                    $"freed {FormatBytes(totalSize)}");
            }
            
            // Remove from file operations dictionary
            _fileOperations.TryRemove(fileId, out _);
        }
        finally
        {
            context.Lock.Release();
        }
    }
    
    /// <summary>
    /// Get current cache statistics
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        return _statistics;
    }
    
    /// <summary>
    /// Dispose of resources
    /// </summary>
    public void Dispose()
    {
        // Cancel all ongoing downloads
        foreach (var context in _fileOperations.Values)
        {
            context.DownloadCts?.Cancel();
            context.Lock.Dispose();
        }
        
        _globalDownloadSemaphore.Dispose();
        _evictionLock.Dispose();
        _cacheLogger.Dispose();
        
        GC.SuppressFinalize(this);
    }
}
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MediaCluster.CacheSystem;

/// <summary>
/// Simplified logging for the cache system
/// </summary>
public class CacheLogger : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _logDirectory;
    private readonly bool _enableLogging;
    private readonly ConcurrentQueue<string> _logQueue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processTask;
    private readonly SemaphoreSlim _logFileLock = new(1, 1);
    
    public CacheLogger(ILogger logger, string logDirectory, bool enableLogging)
    {
        _logger = logger;
        _logDirectory = logDirectory;
        _enableLogging = enableLogging;
        
        if (enableLogging)
        {
            Directory.CreateDirectory(logDirectory);
            _processTask = Task.Run(ProcessLogQueueAsync);
        }
    }
    
    public void LogInfo(string message)
    {
        _logger.LogInformation(message);
        if (_enableLogging)
        {
            _logQueue.Enqueue($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [INFO] {message}");
        }
    }
    
    public void LogWarning(string message)
    {
        _logger.LogWarning(message);
        if (_enableLogging)
        {
            _logQueue.Enqueue($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [WARN] {message}");
        }
    }
    
    public void LogError(string message, Exception? ex = null)
    {
        _logger.LogError(ex, message);
        if (_enableLogging)
        {
            var log = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [ERROR] {message}";
            if (ex != null)
            {
                log += $"\n{ex}";
            }
            _logQueue.Enqueue(log);
        }
    }
    
    public void LogCacheAccess(string fileId, long offset, int length, bool isHit, long elapsedMs)
    {
        if (_enableLogging)
        {
            var hitStr = isHit ? "HIT" : "MISS";
            _logQueue.Enqueue($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [CACHE] {fileId} {offset}-{offset+length-1} {length} {hitStr} {elapsedMs}ms");
        }
    }
    
    private async Task ProcessLogQueueAsync()
    {
        string currentLogFile = Path.Combine(_logDirectory, $"cache_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
        
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                // Rotate log file daily
                string expectedLogFile = Path.Combine(_logDirectory, $"cache_{DateTime.UtcNow:yyyyMMdd}.log");
                if (currentLogFile != expectedLogFile)
                {
                    currentLogFile = expectedLogFile;
                }
                
                // Process up to 100 log entries at once
                int count = 0;
                var lines = new string[Math.Min(100, _logQueue.Count)];
                
                while (count < lines.Length && _logQueue.TryDequeue(out var line))
                {
                    lines[count++] = line;
                }
                
                if (count > 0)
                {
                    await _logFileLock.WaitAsync();
                    try
                    {
                        await File.AppendAllLinesAsync(currentLogFile, lines);
                    }
                    finally
                    {
                        _logFileLock.Release();
                    }
                }
                
                // If queue is empty, wait a bit before checking again
                if (_logQueue.IsEmpty)
                {
                    await Task.Delay(500, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing log queue");
                // Wait before retrying
                try
                {
                    await Task.Delay(5000, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        
        // Flush remaining logs on shutdown
        try
        {
            var remainingLines = _logQueue.ToArray();
            if (remainingLines.Length > 0)
            {
                await _logFileLock.WaitAsync();
                try
                {
                    await File.AppendAllLinesAsync(currentLogFile, remainingLines);
                }
                finally
                {
                    _logFileLock.Release();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing logs on shutdown");
        }
    }
    
    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignore
        }

        try
        {
            _processTask?.Wait(2000);
        }
        catch (AggregateException)
        {
            // Ignore
        }
        
        _cts.Dispose();
        _logFileLock.Dispose();
    }
}
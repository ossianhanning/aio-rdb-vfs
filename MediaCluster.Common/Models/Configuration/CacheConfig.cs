namespace MediaCluster.CacheSystem;

/// <summary>
/// Simplified configuration for the cache system
/// </summary>
public class CacheConfig
{
    /// <summary>
    /// Path where cached files are stored
    /// </summary>
    public string CachePath { get; set; } = "D:\\dev\\rdvfs\\filecache";
    
    /// <summary>
    /// Maximum size of the cache in bytes
    /// </summary>
    public long MaxCacheSize { get; set; } = 10L * 1024 * 1024 * 1024; // 10 GB default
    
    /// <summary>
    /// Size of each chunk in bytes
    /// </summary>
    public int ChunkSize { get; set; } = 8 * 1024 * 1024; // 8 MB default
    
    /// <summary>
    /// Position within chunk to trigger readahead (from end)
    /// </summary>
    public int ReadaheadTriggerPosition { get; set; } = 4 * 1024 * 1024; // 4 MB from end
    
    /// <summary>
    /// Maximum number of concurrent downloads per file
    /// </summary>
    public int MaxConcurrentDownloadsPerFile { get; set; } = 1;
    
    /// <summary>
    /// Maximum total concurrent downloads across all files
    /// </summary>
    public int MaxTotalConcurrentDownloads { get; set; } = 16;
    
    /// <summary>
    /// Number of retries for failed downloads
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>
    /// Base delay between retries (in milliseconds) - will be multiplied by attempt number
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000; // 1 second default
    
    /// <summary>
    /// Whether to enable logging
    /// </summary>
    public bool EnableLogging { get; set; } = true;
    
    /// <summary>
    /// Path for logs
    /// </summary>
    public string LogsPath { get; set; } = "D:\\dev\\rdvfs\\cachelogs";
    
    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 60;
}
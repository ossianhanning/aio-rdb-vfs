namespace MediaCluster.CacheSystem;

/// <summary>
/// Simplified cache statistics
/// </summary>
public class CacheStatistics
{
    private long _totalHits;
    private long _totalMisses;
    private long _bytesReadFromCache;
    private long _bytesReadFromRemote;
    private long _currentCacheSize;
    private long _maxCacheSize;
    private int _fileCount;
    private int _chunkCount;
    
    /// <summary>
    /// Total number of cache hits
    /// </summary>
    public long TotalHits => Interlocked.Read(ref _totalHits);
    
    /// <summary>
    /// Total number of cache misses
    /// </summary>
    public long TotalMisses => Interlocked.Read(ref _totalMisses);
    
    /// <summary>
    /// Total bytes read from cache
    /// </summary>
    public long BytesReadFromCache => Interlocked.Read(ref _bytesReadFromCache);
    
    /// <summary>
    /// Total bytes read from remote source
    /// </summary>
    public long BytesReadFromRemote => Interlocked.Read(ref _bytesReadFromRemote);
    
    /// <summary>
    /// Current cache size in bytes
    /// </summary>
    public long CurrentCacheSize => Interlocked.Read(ref _currentCacheSize);
    
    /// <summary>
    /// Maximum cache size in bytes
    /// </summary>
    public long MaxCacheSize 
    {
        get => Interlocked.Read(ref _maxCacheSize);
        set => Interlocked.Exchange(ref _maxCacheSize, value);
    }
    
    /// <summary>
    /// Number of files in cache
    /// </summary>
    public int FileCount => Interlocked.CompareExchange(ref _fileCount, 0, 0);
    
    /// <summary>
    /// Number of chunks in cache
    /// </summary>
    public int ChunkCount => Interlocked.CompareExchange(ref _chunkCount, 0, 0);
    
    /// <summary>
    /// Cache hit ratio (0.0 to 1.0)
    /// </summary>
    public double HitRatio => TotalRequests == 0 ? 0 : (double)TotalHits / TotalRequests;
    
    /// <summary>
    /// Total number of requests (hits + misses)
    /// </summary>
    public long TotalRequests => TotalHits + TotalMisses;
    
    /// <summary>
    /// Total bytes read (from cache + from remote)
    /// </summary>
    public long TotalBytesRead => BytesReadFromCache + BytesReadFromRemote;
    
    /// <summary>
    /// Percentage of cache used
    /// </summary>
    public double CacheUtilizationPercent => MaxCacheSize == 0 ? 0 : (double)CurrentCacheSize / MaxCacheSize * 100;
    
    /// <summary>
    /// Record a cache hit
    /// </summary>
    public void RecordHit(int bytes)
    {
        Interlocked.Increment(ref _totalHits);
        Interlocked.Add(ref _bytesReadFromCache, bytes);
    }
    
    /// <summary>
    /// Record a cache miss
    /// </summary>
    public void RecordMiss(int bytes)
    {
        Interlocked.Increment(ref _totalMisses);
        Interlocked.Add(ref _bytesReadFromRemote, bytes);
    }
    
    /// <summary>
    /// Increment file count
    /// </summary>
    public void IncrementFileCount()
    {
        Interlocked.Increment(ref _fileCount);
    }
    
    /// <summary>
    /// Decrement file count
    /// </summary>
    public void DecrementFileCount()
    {
        Interlocked.Decrement(ref _fileCount);
    }
    
    /// <summary>
    /// Increment chunk count
    /// </summary>
    public void IncrementChunkCount()
    {
        Interlocked.Increment(ref _chunkCount);
    }
    
    /// <summary>
    /// Decrement chunk count
    /// </summary>
    public void DecrementChunkCount()
    {
        Interlocked.Decrement(ref _chunkCount);
    }
    
    /// <summary>
    /// Add to cache size
    /// </summary>
    public void AddToCacheSize(long bytes)
    {
        Interlocked.Add(ref _currentCacheSize, bytes);
    }
}
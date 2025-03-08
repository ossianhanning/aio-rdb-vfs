using System.Text.Json.Serialization;

namespace MediaCluster.CacheSystem.Models;

/// <summary>
/// Represents a cached file's metadata
/// </summary>
public class CachedFileMetadata
{
    /// <summary>
    /// Identifier for the file in the cache system
    /// </summary>
    public string CacheId { get; set; } = string.Empty;
    
    /// <summary>
    /// The host ID (RealDebrid or other provider ID)
    /// </summary>
    public string HostId { get; set; } = string.Empty;
    
    /// <summary>
    /// The file ID from the host
    /// </summary>
    public int? FileId { get; set; } = null;
    
    /// <summary>
    /// The hash of the parent torrent
    /// </summary>
    public string TorrentHash { get; set; } = string.Empty;
    
    /// <summary>
    /// Total size of the file in bytes
    /// </summary>
    public ulong TotalSize { get; set; }
    
    /// <summary>
    /// Last access time
    /// </summary>
    public DateTime LastAccessed { get; set; }
    
    /// <summary>
    /// Creation time in cache
    /// </summary>
    public DateTime Created { get; set; }
    
    /// <summary>
    /// Whether the first chunk is fully cached
    /// </summary>
    public bool HasCachedHeader { get; set; }
    
    /// <summary>
    /// Whether the last chunk is fully cached
    /// </summary>
    public bool HasCachedFooter { get; set; }
    
    /// <summary>
    /// OSDB hash for video files
    /// </summary>
    public string? OsdbHash { get; set; }
    
    /// <summary>
    /// Whether media info analysis has been performed
    /// </summary>
    public bool IsAnalyzed { get; set; }
    
    /// <summary>
    /// List of cached chunks
    /// </summary>
    public List<CachedChunk> Chunks { get; set; } = new();
    
    /// <summary>
    /// Statistics about this specific file's cache performance
    /// </summary>
    public FileCacheStatistics Statistics { get; set; } = new();
}

/// <summary>
/// Represents a chunk of cached data
/// </summary>
public class CachedChunk
{
    /// <summary>
    /// Offset in the file where this chunk starts
    /// </summary>
    public long Offset { get; set; }
    
    /// <summary>
    /// Length of the chunk in bytes
    /// </summary>
    public int Length { get; set; }
    
    /// <summary>
    /// Last access time
    /// </summary>
    public DateTime LastAccessed { get; set; }
    
    /// <summary>
    /// Whether this chunk is part of the permanent cache (header or footer)
    /// </summary>
    [JsonIgnore]
    public bool IsPermanent => Type != ChunkType.Standard;
    
    /// <summary>
    /// Type of chunk
    /// </summary>
    public ChunkType Type { get; set; } = ChunkType.Standard;
    
    /// <summary>
    /// Filename of the chunk on disk (not including path)
    /// </summary>
    [JsonIgnore]
    public string Filename => Type switch
    {
        ChunkType.Header => "first_128k.bin",
        ChunkType.Footer => "last_64k.bin",
        _ => $"chunks/{Offset}_{Length}.bin"
    };
}

/// <summary>
/// Type of cached chunk
/// </summary>
public enum ChunkType
{
    /// <summary>
    /// Standard cacheable chunk
    /// </summary>
    Standard = 0,
    
    /// <summary>
    /// Header chunk (always cached)
    /// </summary>
    Header = 1,
    
    /// <summary>
    /// Footer chunk (always cached)
    /// </summary>
    Footer = 2
}

/// <summary>
/// Cache statistics for an individual file
/// </summary>
public class FileCacheStatistics
{
    /// <summary>
    /// Number of cache hits
    /// </summary>
    public long Hits { get; set; }
    
    /// <summary>
    /// Number of cache misses
    /// </summary>
    public long Misses { get; set; }
    
    /// <summary>
    /// Bytes read from cache
    /// </summary>
    public long BytesReadFromCache { get; set; }
    
    /// <summary>
    /// Bytes read from remote
    /// </summary>
    public long BytesReadFromRemote { get; set; }
    
    /// <summary>
    /// Smallest read size
    /// </summary>
    public int SmallestReadSize { get; set; } = int.MaxValue;
    
    /// <summary>
    /// Largest read size
    /// </summary>
    public int LargestReadSize { get; set; }
    
    /// <summary>
    /// Distribution of reads by file position (in deciles)
    /// Counts of reads that fall within each 10% segment of the file
    /// </summary>
    public int[] ReadDistributionByDecile { get; set; } = new int[10];
    
    /// <summary>
    /// Distribution of misses by file position (in deciles)
    /// </summary>
    public int[] MissDistributionByDecile { get; set; } = new int[10];
    
    /// <summary>
    /// Distribution of hits by file position (in deciles)
    /// </summary>
    public int[] HitDistributionByDecile { get; set; } = new int[10];
    
    /// <summary>
    /// Last updated timestamp
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
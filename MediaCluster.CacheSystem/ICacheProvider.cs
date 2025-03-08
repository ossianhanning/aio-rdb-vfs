using MediaCluster.RealDebrid.SharedModels;

namespace MediaCluster.CacheSystem;

/// <summary>
/// Interface for the simplified cache provider
/// </summary>
public interface ICacheProvider
{
    /// <summary>
    /// Read data from a file, using the cache when possible
    /// </summary>
    /// <param name="remoteFile">The remote file to read from</param>
    /// <param name="offset">Offset in the file to start reading from</param>
    /// <param name="length">Number of bytes to read</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The requested data as a byte array</returns>
    Task<byte[]> ReadAsync(RemoteFile remoteFile, long offset, int length, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Invalidate cached data for a file
    /// </summary>
    /// <param name="remoteFile">The remote file to invalidate cache for</param>
    Task InvalidateCacheAsync(RemoteFile remoteFile, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get current cache statistics
    /// </summary>
    /// <returns>Cache statistics object</returns>
    CacheStatistics GetStatistics();
}
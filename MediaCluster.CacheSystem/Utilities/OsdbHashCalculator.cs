namespace MediaCluster.CacheSystem.Utilities;

/// <summary>
/// Utility class for calculating OSDB hash for video files
/// </summary>
public static class OsdbHashCalculator
{
    /// <summary>
    /// Chunk size for OSDB hash calculation (64KB)
    /// </summary>
    private const int ChunkSize = 64 * 1024; // 64 KB
    
    /// <summary>
    /// Calculate OSDB hash from file header and footer
    /// </summary>
    /// <param name="fileHeader">First 64KB of the file</param>
    /// <param name="fileFooter">Last 64KB of the file</param>
    /// <param name="fileSize">Total file size in bytes</param>
    /// <returns>OSDB hash as a hexadecimal string</returns>
    public static string ComputeHash(byte[] fileHeader, byte[] fileFooter, ulong fileSize)
    {
        // Ensure we have at least 64KB for both header and footer
        if (fileHeader.Length < ChunkSize || fileFooter.Length < ChunkSize)
        {
            throw new ArgumentException($"Both fileHeader and fileFooter must be at least {ChunkSize} bytes");
        }
        
        // The algorithm used is the same as the one used by OpenSubtitles.org
        // It's a 64-bit hash calculated from file size and the first and last 64KB of the file
        
        ulong hash = fileSize; // Initialize hash with file size
        
        // Process the first 64KB
        for (int i = 0; i < ChunkSize / 8; i++)
        {
            hash += BitConverter.ToUInt64(fileHeader, i * 8);
        }
        
        // Process the last 64KB
        for (int i = 0; i < ChunkSize / 8; i++)
        {
            hash += BitConverter.ToUInt64(fileFooter, i * 8);
        }
        
        // Return the hash as a 16-character hex string
        return hash.ToString("x16");
    }
    
    /// <summary>
    /// Calculate OSDB hash from a stream
    /// </summary>
    /// <param name="stream">Stream to calculate hash from</param>
    /// <returns>OSDB hash as a hexadecimal string</returns>
    public static async Task<string> ComputeHashAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        // Save the current position
        var initialPosition = stream.Position;
        
        try
        {
            // Get file size
            var fileSize = (ulong)stream.Length;
            
            // If the file is smaller than 128KB, we'll use a simpler hash
            if (fileSize < ChunkSize * 2)
            {
                // For very small files, just use the file size as the hash
                return fileSize.ToString("x16");
            }
            
            // Read the first 64KB
            stream.Position = 0;
            var header = new byte[ChunkSize];
            await stream.ReadAsync(header, 0, ChunkSize, cancellationToken);
            
            // Read the last 64KB
            stream.Position = stream.Length - ChunkSize;
            var footer = new byte[ChunkSize];
            await stream.ReadAsync(footer, 0, ChunkSize, cancellationToken);
            
            // Compute hash
            return ComputeHash(header, footer, fileSize);
        }
        finally
        {
            // Restore the original position
            stream.Position = initialPosition;
        }
    }
}
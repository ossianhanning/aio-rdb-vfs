using System.Text.Json.Serialization;

namespace MediaCluster.QBittorrentApi.Models;

/// <summary>
/// QBittorrent file information
/// </summary>
public class FileInfoDto
{
    /// <summary>
    /// File index
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>
    /// File name
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Download progress (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    /// <summary>
    /// Download priority (0 = do not download, 1 = normal, 6 = high, 7 = maximal)
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    /// <summary>
    /// Whether the file is a seed
    /// </summary>
    [JsonPropertyName("is_seed")]
    public bool IsSeed { get; set; }

    /// <summary>
    /// Piece range
    /// </summary>
    [JsonPropertyName("piece_range")]
    public int[] PieceRange { get; set; } = System.Array.Empty<int>();

    /// <summary>
    /// Availability of the file (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("availability")]
    public double Availability { get; set; }

    /// <summary>
    /// Relative path of the file within the torrent
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}
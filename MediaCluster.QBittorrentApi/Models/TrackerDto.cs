using System.Text.Json.Serialization;

namespace MediaCluster.QBittorrentApi.Models;

/// <summary>
/// QBittorrent tracker information
/// </summary>
public class TrackerDto
{
    /// <summary>
    /// URL of the tracker
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Status of the tracker
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    /// <summary>
    /// Number of peers from this tracker
    /// </summary>
    [JsonPropertyName("num_peers")]
    public int NumPeers { get; set; }

    /// <summary>
    /// Number of seeds from this tracker
    /// </summary>
    [JsonPropertyName("num_seeds")]
    public int NumSeeds { get; set; }

    /// <summary>
    /// Number of leeches from this tracker
    /// </summary>
    [JsonPropertyName("num_leeches")]
    public int NumLeeches { get; set; }

    /// <summary>
    /// Number of completed downloads from this tracker
    /// </summary>
    [JsonPropertyName("num_downloaded")]
    public int NumDownloaded { get; set; }

    /// <summary>
    /// Message from the tracker
    /// </summary>
    [JsonPropertyName("msg")]
    public string Message { get; set; } = string.Empty;
}
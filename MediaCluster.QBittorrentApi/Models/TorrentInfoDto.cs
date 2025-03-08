using System.Text.Json.Serialization;

namespace MediaCluster.QBittorrentApi.Models;

/// <summary>
/// QBittorrent torrent information
/// </summary>
public class TorrentInfoDto
{
    /// <summary>
    /// Torrent hash
    /// </summary>
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Torrent name
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Total size in bytes
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Download progress (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    /// <summary>
    /// Download speed in bytes per second
    /// </summary>
    [JsonPropertyName("dlspeed")]
    public long DownloadSpeed { get; set; }

    /// <summary>
    /// Estimated time of arrival (completion) in seconds
    /// </summary>
    [JsonPropertyName("eta")]
    public long Eta { get; set; }

    /// <summary>
    /// Current state (e.g., "downloading", "completed")
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Whether sequential download is enabled
    /// </summary>
    [JsonPropertyName("seq_dl")]
    public bool SequentialDownload { get; set; }

    /// <summary>
    /// Whether first/last piece priority is enabled
    /// </summary>
    [JsonPropertyName("f_l_piece_prio")]
    public bool FirstLastPiecePriority { get; set; }

    /// <summary>
    /// Category
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; } = string.Empty;

    /// <summary>
    /// Label
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Content path
    /// </summary>
    [JsonPropertyName("content_path")]
    public string ContentPath { get; set; } = string.Empty;

    /// <summary>
    /// Save path
    /// </summary>
    [JsonPropertyName("save_path")]
    public string SavePath { get; set; } = string.Empty;

    /// <summary>
    /// When the torrent was added (Unix timestamp)
    /// </summary>
    [JsonPropertyName("added_on")]
    public long AddedOn { get; set; }

    /// <summary>
    /// When the torrent was completed (Unix timestamp)
    /// </summary>
    [JsonPropertyName("completion_on")]
    public long CompletionOn { get; set; }

    /// <summary>
    /// Tracker URL
    /// </summary>
    [JsonPropertyName("tracker")]
    public string Tracker { get; set; } = string.Empty;

    /// <summary>
    /// Download limit in bytes per second
    /// </summary>
    [JsonPropertyName("dl_limit")]
    public long DownloadLimit { get; set; }

    /// <summary>
    /// Upload limit in bytes per second
    /// </summary>
    [JsonPropertyName("up_limit")]
    public long UploadLimit { get; set; }

    /// <summary>
    /// RealDebrid ID (not exposed in API)
    /// </summary>
    [JsonIgnore]
    public string RealDebridId { get; set; } = string.Empty;

    public float Ratio { get; set; } = 0;
    public float RatioLimit { get; set; } = 0;
}
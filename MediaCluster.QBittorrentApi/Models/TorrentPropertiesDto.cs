using System.Text.Json.Serialization;

namespace MediaCluster.QBittorrentApi.Models;

/// <summary>
/// QBittorrent torrent properties
/// </summary>
public class TorrentPropertiesDto
{
    /// <summary>
    /// Save path
    /// </summary>
    [JsonPropertyName("save_path")]
    public string SavePath { get; set; } = string.Empty;

    /// <summary>
    /// Creation date (Unix timestamp)
    /// </summary>
    [JsonPropertyName("creation_date")]
    public long CreationDate { get; set; }

    /// <summary>
    /// Piece size in bytes
    /// </summary>
    [JsonPropertyName("piece_size")]
    public long PieceSize { get; set; }

    /// <summary>
    /// Comment
    /// </summary>
    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;

    /// <summary>
    /// Total wasted bytes
    /// </summary>
    [JsonPropertyName("total_wasted")]
    public long TotalWasted { get; set; }

    /// <summary>
    /// Total uploaded bytes
    /// </summary>
    [JsonPropertyName("total_uploaded")]
    public long TotalUploaded { get; set; }

    /// <summary>
    /// Total downloaded bytes
    /// </summary>
    [JsonPropertyName("total_downloaded")]
    public long TotalDownloaded { get; set; }

    /// <summary>
    /// Upload limit in bytes per second
    /// </summary>
    [JsonPropertyName("up_limit")]
    public long UploadLimit { get; set; }

    /// <summary>
    /// Download limit in bytes per second
    /// </summary>
    [JsonPropertyName("dl_limit")]
    public long DownloadLimit { get; set; }

    /// <summary>
    /// Time elapsed in seconds
    /// </summary>
    [JsonPropertyName("time_elapsed")]
    public long TimeElapsed { get; set; }

    /// <summary>
    /// Seeding time in seconds
    /// </summary>
    [JsonPropertyName("seeding_time")]
    public long SeedingTime { get; set; }

    /// <summary>
    /// Number of connections
    /// </summary>
    [JsonPropertyName("nb_connections")]
    public long Connections { get; set; }

    /// <summary>
    /// Share ratio
    /// </summary>
    [JsonPropertyName("share_ratio")]
    public double ShareRatio { get; set; }

    /// <summary>
    /// Addition date (Unix timestamp)
    /// </summary>
    [JsonPropertyName("addition_date")]
    public long AdditionDate { get; set; }

    /// <summary>
    /// Completion date (Unix timestamp)
    /// </summary>
    [JsonPropertyName("completion_date")]
    public long CompletionDate { get; set; }

    /// <summary>
    /// Created by
    /// </summary>
    [JsonPropertyName("created_by")]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Average download speed in bytes per second
    /// </summary>
    [JsonPropertyName("dl_speed_avg")]
    public long DownloadSpeedAvg { get; set; }

    /// <summary>
    /// Current download speed in bytes per second
    /// </summary>
    [JsonPropertyName("dl_speed")]
    public long DownloadSpeed { get; set; }

    /// <summary>
    /// Estimated time of arrival (completion) in seconds
    /// </summary>
    [JsonPropertyName("eta")]
    public long Eta { get; set; }

    /// <summary>
    /// Last seen complete timestamp
    /// </summary>
    [JsonPropertyName("last_seen")]
    public long LastSeen { get; set; }

    /// <summary>
    /// Number of peers
    /// </summary>
    [JsonPropertyName("peers")]
    public long Peers { get; set; }

    /// <summary>
    /// Total number of peers
    /// </summary>
    [JsonPropertyName("peers_total")]
    public long PeersTotal { get; set; }

    /// <summary>
    /// Number of seeds
    /// </summary>
    [JsonPropertyName("seeds")]
    public long Seeds { get; set; }

    /// <summary>
    /// Total number of seeds
    /// </summary>
    [JsonPropertyName("seeds_total")]
    public long SeedsTotal { get; set; }

    /// <summary>
    /// Average upload speed in bytes per second
    /// </summary>
    [JsonPropertyName("up_speed_avg")]
    public long UploadSpeedAvg { get; set; }

    /// <summary>
    /// Current upload speed in bytes per second
    /// </summary>
    [JsonPropertyName("up_speed")]
    public long UploadSpeed { get; set; }

    public long ETA { get; set; }
    public long TotalSize { get; set; }
}
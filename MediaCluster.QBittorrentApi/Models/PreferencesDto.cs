using System.Text.Json.Serialization;

namespace MediaCluster.QBittorrentApi.Models;

/// <summary>
/// QBittorrent application preferences
/// </summary>
public class PreferencesDto
{
    /// <summary>
    /// Default save path
    /// </summary>
    [JsonPropertyName("save_path")]
    public string SavePath { get; set; } = string.Empty;

    /// <summary>
    /// Whether max ratio limit is enabled
    /// </summary>
    [JsonPropertyName("max_ratio_enabled")]
    public bool MaxRatioEnabled { get; set; }

    /// <summary>
    /// Maximum ratio
    /// </summary>
    [JsonPropertyName("max_ratio")]
    public float MaxRatio { get; set; } = -1;

    /// <summary>
    /// Whether max seeding time is enabled
    /// </summary>
    [JsonPropertyName("max_seeding_time_enabled")]
    public bool MaxSeedingTimeEnabled { get; set; }

    /// <summary>
    /// Maximum seeding time in minutes
    /// </summary>
    [JsonPropertyName("max_seeding_time")]
    public long MaxSeedingTime { get; set; } = -1;

    /// <summary>
    /// Whether max inactive seeding time is enabled
    /// </summary>
    [JsonPropertyName("max_inactive_seeding_time_enabled")]
    public bool MaxInactiveSeedingTimeEnabled { get; set; }

    /// <summary>
    /// Maximum inactive seeding time in minutes
    /// </summary>
    [JsonPropertyName("max_inactive_seeding_time")]
    public long MaxInactiveSeedingTime { get; set; } = -1;

    /// <summary>
    /// Max ratio action (0 = Pause, 1 = Remove, 2 = Enable super seeding, 3 = Remove and delete files)
    /// </summary>
    [JsonPropertyName("max_ratio_act")]
    public int MaxRatioAction { get; set; }

    /// <summary>
    /// Whether queueing is enabled
    /// </summary>
    [JsonPropertyName("queueing_enabled")]
    public bool QueueingEnabled { get; set; } = true;

    /// <summary>
    /// Whether DHT is enabled
    /// </summary>
    [JsonPropertyName("dht")]
    public bool DhtEnabled { get; set; } = true;
}
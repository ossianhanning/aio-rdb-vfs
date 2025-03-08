namespace MediaCluster.Common.Models.Configuration;

/// <summary>
/// QBittorrent API configuration
/// </summary>
public class QBittorrentConfig
{
    /// <summary>
    /// Port to listen on for HTTP requests
    /// </summary>
    public int Port { get; set; } = 8080;
        
    /// <summary>
    /// Default save path for torrents
    /// </summary>
    public string DefaultSavePath { get; set; } = string.Empty;
        
    /// <summary>
    /// Available categories
    /// </summary>
    public List<string> Categories { get; set; } = new();
        
    /// <summary>
    /// Whether to automatically select all files when adding a torrent
    /// </summary>
    public bool AutoSelectFiles { get; set; } = true;
}
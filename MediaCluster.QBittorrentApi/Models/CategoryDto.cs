using System.Text.Json.Serialization;

namespace MediaCluster.QBittorrentApi.Models;

/// <summary>
/// QBittorrent category information
/// </summary>
public class CategoryDto
{
    /// <summary>
    /// Name of the category
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Save path for the category
    /// </summary>
    [JsonPropertyName("savePath")]
    public string SavePath { get; set; } = string.Empty;
}
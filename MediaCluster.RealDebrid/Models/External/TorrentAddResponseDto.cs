using System.Text.Json.Serialization;

namespace MediaCluster.RealDebrid.Models.External;

internal class TorrentAddResponseDto
{
    [JsonPropertyName("id")] public string Id { get; set; }

    [JsonPropertyName("uri")] public string Uri { get; set; }
}
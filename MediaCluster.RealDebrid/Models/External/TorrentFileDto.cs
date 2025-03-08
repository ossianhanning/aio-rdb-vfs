using System.Text.Json.Serialization;

namespace MediaCluster.RealDebrid.Models.External;

internal class TorrentFileDto
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("path")] public string Path { get; set; }

    [JsonPropertyName("bytes")] public long Bytes { get; set; }

    [JsonPropertyName("selected")] public int Selected { get; set; }
}
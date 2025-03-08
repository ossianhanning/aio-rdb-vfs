using System.Text.Json.Serialization;

namespace MediaCluster.RealDebrid.Models.External;

internal class TorrentItemDto
{
    [JsonPropertyName("id")] public string Id { get; set; }

    [JsonPropertyName("filename")] public string Filename { get; set; }

    [JsonPropertyName("hash")] public string Hash { get; set; }

    [JsonPropertyName("bytes")] public long Bytes { get; set; }

    [JsonPropertyName("host")] public string Host { get; set; }

    [JsonPropertyName("split")] public int Split { get; set; }

    [JsonPropertyName("progress")] public double Progress { get; set; }

    [JsonPropertyName("status")] public string Status { get; set; }

    [JsonPropertyName("added")] public string Added { get; set; }

    [JsonPropertyName("links")] public List<string> Links { get; set; }

    [JsonPropertyName("ended")] public string Ended { get; set; }

    [JsonPropertyName("speed")] public int? Speed { get; set; }

    [JsonPropertyName("seeders")] public int? Seeders { get; set; }
}
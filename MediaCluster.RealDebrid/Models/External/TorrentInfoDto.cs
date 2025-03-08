using System.Text.Json.Serialization;

namespace MediaCluster.RealDebrid.Models.External;

internal class TorrentInfoDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("filename")]
    public string Filename { get; set; }

    [JsonPropertyName("original_filename")]
    public string OriginalFilename { get; set; }

    [JsonPropertyName("hash")]
    public string Hash { get; set; }

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("original_bytes")]
    public long OriginalBytes { get; set; }

    [JsonPropertyName("host")]
    public string Host { get; set; }

    [JsonPropertyName("split")]
    public int Split { get; set; }

    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("added")]
    public string Added { get; set; }

    [JsonPropertyName("files")]
    public List<TorrentFileDto> Files { get; set; }

    [JsonPropertyName("links")]
    public List<string> Links { get; set; }

    [JsonPropertyName("ended")]
    public string Ended { get; set; }

    [JsonPropertyName("speed")]
    public double? Speed { get; set; }

    [JsonPropertyName("seeders")]
    public int? Seeders { get; set; }

    public List<string>? Tags { get; set; }
    public string? Category { get; set; }
}
using System.Text.Json.Serialization;

namespace MediaCluster.RealDebrid.Models.External;

internal class LinkCheckResponseDto
{
    [JsonPropertyName("host")] public string Host { get; set; }

    [JsonPropertyName("link")] public string Link { get; set; }

    [JsonPropertyName("filename")] public string Filename { get; set; }

    [JsonPropertyName("filesize")] public long Filesize { get; set; }

    [JsonPropertyName("supported")] public int Supported { get; set; }
}
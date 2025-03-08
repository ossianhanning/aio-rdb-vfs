using System.Text.Json.Serialization;

namespace MediaCluster.RealDebrid.Models.External;

internal class UnrestrictLinkResponseDto
{
    [JsonPropertyName("id")] public string Id { get; set; }

    [JsonPropertyName("filename")] public string Filename { get; set; }

    [JsonPropertyName("mimeType")] public string MimeType { get; set; }

    [JsonPropertyName("filesize")] public long Filesize { get; set; }

    [JsonPropertyName("link")] public string Link { get; set; }

    [JsonPropertyName("host")] public string Host { get; set; }

    [JsonPropertyName("chunks")] public int Chunks { get; set; }

    [JsonPropertyName("crc")] public int Crc { get; set; }

    [JsonPropertyName("download")] public string Download { get; set; }

    [JsonPropertyName("streamable")] public int Streamable { get; set; }

    [JsonPropertyName("generated")] public string Generated { get; set; }
}
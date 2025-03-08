using System.Text.Json.Serialization;
using MediaCluster.MediaAnalyzer.Models.External.JsonConverters;

namespace MediaCluster.MediaAnalyzer.Models.External;

internal class FormatDto
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; }

    [JsonPropertyName("nb_streams")]
    public int NumberOfStreams { get; set; }

    [JsonPropertyName("nb_programs")]
    public int NumberOfPrograms { get; set; }

    [JsonPropertyName("format_name")]
    public string FormatName { get; set; }

    [JsonPropertyName("format_long_name")]
    public string FormatLongName { get; set; }

    [JsonPropertyName("start_time")]
    [JsonConverter(typeof(StringToDoubleConverter))]
    public double? StartTime { get; set; }

    [JsonPropertyName("duration")]
    [JsonConverter(typeof(StringToDoubleConverter))]
    public double? Duration { get; set; }

    [JsonPropertyName("size")]
    [JsonConverter(typeof(StringToLongConverter))]
    public long? Size { get; set; }

    [JsonPropertyName("bit_rate")]
    [JsonConverter(typeof(StringToLongConverter))]
    public long? BitRate { get; set; }

    [JsonPropertyName("probe_score")]
    public int ProbeScore { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}
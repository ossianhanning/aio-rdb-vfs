using System.Text.Json.Serialization;

namespace MediaCluster.MediaAnalyzer.Models.External;

internal class ResultDto
{
    [JsonPropertyName("format")]
    public FormatDto? Format { get; set; }

    [JsonPropertyName("streams")]
    public List<StreamDto>? Streams { get; set; }
}
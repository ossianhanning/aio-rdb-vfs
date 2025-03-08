using System.Text.Json.Serialization;

namespace MediaCluster.MediaAnalyzer.Models.External;

internal class DispositionDto
{
    [JsonPropertyName("default")]
    public int Default { get; set; }

    [JsonPropertyName("forced")]
    public int Forced { get; set; }

    [JsonPropertyName("hearing_impaired")]
    public int HearingImpaired { get; set; }

    [JsonPropertyName("visual_impaired")]
    public int VisualImpaired { get; set; }

    [JsonPropertyName("attached_pic")]
    public int AttachedPic { get; set; }

    [JsonPropertyName("non_diegetic")]
    public int NonDiegetic { get; set; }

    [JsonPropertyName("captions")]
    public int Captions { get; set; }

    [JsonPropertyName("descriptions")]
    public int Descriptions { get; set; }

    [JsonPropertyName("metadata")]
    public int Metadata { get; set; }

    [JsonPropertyName("dependent")]
    public int Dependent { get; set; }

    [JsonPropertyName("still_image")]
    public int StillImage { get; set; }

    [JsonPropertyName("multilayer")]
    public int MultiLayer { get; set; }
}
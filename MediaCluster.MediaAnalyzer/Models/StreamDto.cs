using System.Text.Json.Serialization;
using MediaCluster.MediaAnalyzer.Models.External.JsonConverters;

namespace MediaCluster.MediaAnalyzer.Models.External;

internal class StreamDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("codec_name")]
    public string CodecName { get; set; }

    [JsonPropertyName("codec_long_name")]
    public string CodecLongName { get; set; }

    [JsonPropertyName("profile")]
    public string Profile { get; set; }

    [JsonPropertyName("codec_type")]
    public string CodecType { get; set; }

    [JsonPropertyName("codec_tag_string")]
    public string CodecTagString { get; set; }

    [JsonPropertyName("codec_tag")]
    public string CodecTag { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("coded_width")]
    public int? CodedWidth { get; set; }

    [JsonPropertyName("coded_height")]
    public int? CodedHeight { get; set; }

    [JsonPropertyName("closed_captions")]
    public int? ClosedCaptions { get; set; }

    [JsonPropertyName("film_grain")]
    public int? FilmGrain { get; set; }

    [JsonPropertyName("has_b_frames")]
    public int? HasBFrames { get; set; }

    [JsonPropertyName("sample_aspect_ratio")]
    public string SampleAspectRatio { get; set; }

    [JsonPropertyName("display_aspect_ratio")]
    public string DisplayAspectRatio { get; set; }

    [JsonPropertyName("pix_fmt")]
    public string PixelFormat { get; set; }

    [JsonPropertyName("level")]
    public int? Level { get; set; }

    [JsonPropertyName("chroma_location")]
    public string ChromaLocation { get; set; }

    [JsonPropertyName("field_order")]
    public string FieldOrder { get; set; }

    [JsonPropertyName("refs")]
    public int? Refs { get; set; }

    [JsonPropertyName("is_avc")]
    public string IsAvc { get; set; }

    [JsonPropertyName("nal_length_size")]
    public string NalLengthSize { get; set; }

    [JsonPropertyName("r_frame_rate")]
    public string FrameRate { get; set; }

    [JsonPropertyName("avg_frame_rate")]
    public string AverageFrameRate { get; set; }

    [JsonPropertyName("time_base")]
    public string TimeBase { get; set; }

    [JsonPropertyName("start_pts")]
    public long? StartPts { get; set; }

    [JsonPropertyName("start_time")]
    [JsonConverter(typeof(StringToDoubleConverter))]
    public double? StartTime { get; set; }

    [JsonPropertyName("duration")]
    [JsonConverter(typeof(StringToDoubleConverter))]
    public double? Duration { get; set; }

    [JsonPropertyName("bits_per_raw_sample")]
    [JsonConverter(typeof(StringToIntConverter))]
    public int? BitsPerRawSample { get; set; }

    [JsonPropertyName("extradata_size")]
    public int? ExtraDataSize { get; set; }
    
    [JsonPropertyName("channels")]
    public int? Channels { get; set; }

    [JsonPropertyName("sample_rate")]
    [JsonConverter(typeof(StringToIntConverter))]
    public int? SampleRate { get; set; }

    [JsonPropertyName("disposition")]
    public DispositionDto Disposition { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string> Tags { get; set; }
}
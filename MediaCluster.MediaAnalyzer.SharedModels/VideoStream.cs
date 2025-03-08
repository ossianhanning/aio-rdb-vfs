namespace MediaCluster.MediaAnalyzer.Models;

public class VideoStream
{
    public int Index { get; set; }
    public string? CodecName { get; set; }
    public string? Profile { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? DisplayAspectRatio { get; set; }
    public double? Duration { get; set; }
    public bool IsHDR { get; set; }
    public string? Language { get; set; }
    public string? FrameRate { get; set; }
    public int? BitDepth { get; set; }
}
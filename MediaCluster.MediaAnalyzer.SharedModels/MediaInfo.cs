namespace MediaCluster.MediaAnalyzer.Models;

public class MediaInfo
{
    public List<VideoStream>? VideoStreams { get; set; }
    public List<AudioStream>? AudioStreams { get; set; }
    public List<SubtitleStream>? SubtitleStreams { get; set; }
    public double? Duration { get; set; }
    public long? BitRate { get; set; }
    public string? FormatName { get; set; }
    public string? FormatLongName { get; set; }
    public long? Size { get; set; }
    public double? StartTime { get; set; }
}
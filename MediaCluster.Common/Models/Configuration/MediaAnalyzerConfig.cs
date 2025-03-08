namespace MediaCluster.Common.Models.Configuration;

public class MediaAnalyzerConfig
{
    public string FFprobePath { get; set; } = "ffprobe";
    public bool AutoAnalyzeMedia { get; set; } = true;
    public List<string> MediaFileExtensions { get; set; } = new()
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv",
        ".flv", ".webm", ".m4v", ".mpg", ".mpeg"
    };
}
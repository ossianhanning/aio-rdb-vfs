using MediaCluster.CacheSystem;

namespace MediaCluster.Common.Models.Configuration;

public class AppConfig
{
    public RealDebridConfig RealDebrid { get; set; } = new();
    public FileSystemConfig FileSystem { get; set; } = new();
    public QBittorrentConfig QBittorrent { get; set; } = new();
    public MediaAnalyzerConfig MediaAnalyzer { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
    public CacheConfig Cache { get; set; } = new();
}
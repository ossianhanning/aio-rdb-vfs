namespace MediaCluster.Common.Models.Configuration;

public class LoggingConfig
{
    public string LogLevel { get; set; } = "Information";
    public string LogPath { get; set; } = "logs";
    public int MaxLogFileSizeMb { get; set; } = 100;
    public int MaxLogFileCount { get; set; } = 31;
    public bool LogToConsole { get; set; } = true;
}
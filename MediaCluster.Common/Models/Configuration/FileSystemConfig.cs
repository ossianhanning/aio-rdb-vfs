namespace MediaCluster.Common.Models.Configuration;

public class FileSystemConfig
{
    /// <summary>Path to put any cache chunks for virtual files</summary>
    public string CachePath { get; set; } = "D:\\dev\\rdvfs\\cache";
    /// <summary>Path for "local" files and folders to merge with virtual files and folders</summary>
    public string FileSystemLocalPath { get; set; } = "D:\\dev\\rdvfs\\local";
    /// <summary>sPath where the local folder are merged with virtual files and folders</summary>
    public string FileSystemMergedPath { get; set; } = "D:\\dev\\rdvfs\\merged";
}
using MediaCluster.Common.Models.Configuration;
using MediaCluster.RealDebrid.SharedModels;

namespace MediaCluster.QBittorrentApi.Models;

internal static class DomainMapper
{
    public static TorrentInfoDto ToDomain(this RemoteContainer @this, FileSystemConfig fileSystemConfig) =>
        new()
        {
            Hash = @this.TorrentHash,
            Name = @this.Name,
            Size = @this.Files.Sum(file => (long)file.Size),
            Progress = @this.RemoteStatus == RemoteStatus.Downloaded ? 1.0 : 
                (@this.RemoteStatus == RemoteStatus.Downloading ? 0.5 : 0.0), // Example estimation
            DownloadSpeed = @this.LastDownloadSpeed ?? 0,
            Eta = CalculateEta(@this),
            State = MapTorrentState(@this),
            SequentialDownload = false,
            FirstLastPiecePriority = false,
            Category = @this.Category,
            Label = string.Join(",", @this.Tags ?? []),
            ContentPath = Directory.GetParent(GetSavePath(@this, fileSystemConfig))!.FullName,
            SavePath = GetSavePath(@this, fileSystemConfig),
            AddedOn = new DateTimeOffset(@this.Added).ToUnixTimeSeconds(),
            CompletionOn = @this.RemoteStatus == RemoteStatus.Downloaded ? 
                new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() : 0,
            Tracker = "https://real-debrid.com/tracker",
            DownloadLimit = 0,
            UploadLimit = 0,
            RealDebridId = @this.HostId,
        };
    
    private static string MapTorrentState(RemoteContainer container)
    {
        // Handle problematic torrents first
        if (container.TorrentState == TorrentState.Problematic)
        {
            return "error";
        }
        
        // Handle dormant torrents
        if (container.TorrentState == TorrentState.Dormant)
        {
            return "pausedDL";
        }
        
        // Map active torrents based on RealDebrid status
        return container.RemoteStatus switch
        {
            RemoteStatus.MagnetConversion => "metaDL",
            RemoteStatus.WaitingFilesSelection => "queuedDL",
            RemoteStatus.Queued => "queuedDL",
            RemoteStatus.Downloading => "downloading",
            RemoteStatus.Compressing => "forcedDL",
            RemoteStatus.Uploading => "forcedUP",
            RemoteStatus.Downloaded => "stoppedUP",
            RemoteStatus.Error => "error",
            RemoteStatus.MagnetError => "error",
            RemoteStatus.Virus => "error",
            RemoteStatus.Dead => "error",
            RemoteStatus.StalledDownload => "stalledDL",
            _ => "missingFiles"
        };
    }
    
    private static long CalculateEta(RemoteContainer container)
    {
        // For simplicity, return 0 for completed or problematic torrents
        if (container.RemoteStatus == RemoteStatus.Downloaded || 
            container.TorrentState == TorrentState.Problematic)
        {
            return 0;
        }
        
        // For active downloads, return a reasonable estimate (30 minutes)
        return 1800;
    }
    
    private static string GetSavePath(RemoteContainer container, FileSystemConfig fileSystemConfig)
    {
        if (container.Files.Count == 0)
            return fileSystemConfig.FileSystemMergedPath;
            
        // Get directory of first file
        var firstFile = container.Files.First();
        var dir = Path.GetDirectoryName(firstFile.LocalPath.Replace('/', Path.DirectorySeparatorChar));
        
        return Path.Combine(fileSystemConfig.FileSystemMergedPath, dir);
    }
    
    public static List<TorrentInfoDto> ToDomain(this IEnumerable<RemoteContainer> @this, FileSystemConfig fileSystemConfig) =>
        @this.Select(t => t.ToDomain(fileSystemConfig)).ToList();
}
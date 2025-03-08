using MediaCluster.RealDebrid.SharedModels;

namespace MediaCluster.QBittorrentApi.Helpers;

public static class EnumStatusMapper
{
    private static readonly Dictionary<RemoteStatus, string> _statusMap = new()
    {
        [RemoteStatus.MagnetConversion] = "metaDL",
        [RemoteStatus.WaitingFilesSelection] = "queuedDL",
        [RemoteStatus.Queued] = "queuedDL",
        [RemoteStatus.Downloading] = "downloading",
        [RemoteStatus.Compressing] = "forcedDL",
        [RemoteStatus.Uploading] = "forcedUP",
        [RemoteStatus.Downloaded] = "completed",
        [RemoteStatus.Error] = "error",
        [RemoteStatus.MagnetError] = "error",
        [RemoteStatus.Virus] = "error",
        [RemoteStatus.Dead] = "error",
        [RemoteStatus.StalledDownload] = "stalledDL"
    };

    public static string ToQBittorrentString(this RemoteStatus status) => 
        _statusMap.GetValueOrDefault(status, "missingFiles");
}
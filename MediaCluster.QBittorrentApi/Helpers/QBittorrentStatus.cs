using System.ComponentModel;

namespace MediaCluster.QBittorrentApi.Helpers;

public enum QBittorrentStatus
{
    [Description("metaDL")] MetadataDownloading,
    [Description("queuedDL")] QueuedDownload,
    [Description("downloading")] Downloading,
    [Description("forcedDL")] ForcedDownload,
    [Description("forcedUP")] ForcedUpload,
    [Description("completed")] Completed,
    [Description("error")] Error,
    [Description("stalledDL")] StalledDownload,
    [Description("missingFiles")] MissingFiles
}
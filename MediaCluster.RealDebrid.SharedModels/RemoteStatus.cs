using System.ComponentModel;

namespace MediaCluster.RealDebrid.SharedModels;

public enum RemoteStatus
{
    [Description("magnet_conversion")] MagnetConversion,
    [Description("waiting_files_selection")] WaitingFilesSelection,
    [Description("queued")] Queued,
    [Description("downloading")] Downloading,
    [Description("compressing")] Compressing,
    [Description("uploading")] Uploading,
    [Description("downloaded")] Downloaded,
    [Description("error")] Error,
    [Description("magnet_error")] MagnetError,
    [Description("virus")] Virus,
    [Description("dead")] Dead,
    [Description("stalledDL")] StalledDownload,
    Missing
}
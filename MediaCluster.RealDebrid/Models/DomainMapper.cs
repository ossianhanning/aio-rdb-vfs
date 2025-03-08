using MediaCluster.RealDebrid.Models.External;
using MediaCluster.RealDebrid.SharedModels;

namespace MediaCluster.RealDebrid.Models;

internal static class DomainMapper
{
    public static RemoteContainer ToDomain(this TorrentItemDto @this) =>
        new()
        {
            HostId = @this.Id,
            Name = @this.Filename,
            TorrentHash = @this.Hash,
            RemoteStatus = @this.Status.ToRemoteStatus(),
            Added = DateTime.Parse(@this.Added),
            Files = new List<RemoteFile>()
        };

    public static List<RemoteContainer> ToDomain(this IEnumerable<TorrentItemDto> @this) =>
        @this.Select(ToDomain).ToList();
    
    public static RemoteContainer ToDomain(this TorrentInfoDto @this) =>
        new()
        {
            HostId = @this.Id,
            Name = @this.Filename,
            TorrentHash = @this.Hash,
            RemoteStatus = @this.Status.ToRemoteStatus(),
            Added = DateTime.Parse(@this.Added),
            Files = new List<RemoteFile>(),
            Tags = @this.Tags,
            Category = @this.Category,
        };

    public static List<RemoteContainer> ToDomain(this IEnumerable<TorrentInfoDto> @this) =>
        @this.Select(ToDomain).ToList();

    internal static RemoteStatus ToRemoteStatus(this string status) =>
        status switch
        {
            "magnet_conversion" => RemoteStatus.MagnetConversion,
            "waiting_files_selection" => RemoteStatus.WaitingFilesSelection,
            "queued" => RemoteStatus.Queued,
            "downloading" => RemoteStatus.Downloading,
            "compressing" => RemoteStatus.Compressing,
            "uploading" => RemoteStatus.Uploading,
            "downloaded" => RemoteStatus.Downloaded,
            "error" => RemoteStatus.Error,
            "magnet_error" => RemoteStatus.MagnetError,
            "virus" => RemoteStatus.Virus,
            "dead" => RemoteStatus.Dead,
            "stalledDL" => RemoteStatus.StalledDownload,
            _ => throw new ArgumentException($"Invalid RemoteStatus value: {status}", nameof(status))
        };
}

using MediaCluster.RealDebrid.SharedModels;

namespace MediaCluster.RealDebrid;

public interface ITorrentInformationStore
{
    public TaskCompletionSource ReadySource { get; }
    IEnumerable<RemoteContainer> GetAllTorrents();
}
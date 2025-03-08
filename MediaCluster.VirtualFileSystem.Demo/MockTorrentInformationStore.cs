using MediaCluster.RealDebrid;
using MediaCluster.RealDebrid.SharedModels;

namespace MediaCluster.MergedFileSystem.Test;

public class MockTorrentInformationStore : ITorrentInformationStore
{
    public TaskCompletionSource ReadySource { get; } = new();
    public IEnumerable<RemoteContainer> GetAllTorrents()
    {
        throw new NotImplementedException();
    }
}
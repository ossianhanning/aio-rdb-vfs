using MediaCluster.Common.Models.Configuration;
using Microsoft.Extensions.Options;

namespace MediaCluster.RealDebrid;

public class TorrentCleanupService(
    TorrentInformationStore store,
    IOptions<AppConfig> options,
    RealDebridClient realDebridClient,
    ILogger<TorrentCleanupService> logger) : BackgroundService
{
    private readonly TimeSpan _pollingInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for store to initialize first
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupDuplicateTorrentsAsync(stoppingToken);
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in torrent cleanup service");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private async Task CleanupDuplicateTorrentsAsync(CancellationToken stoppingToken)
    {
        var allTorrents = store.GetAllTorrents().ToList();
        var hashGroups = allTorrents.GroupBy(t => t.TorrentHash.ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .ToList();
        
        if (!hashGroups.Any())
        {
            logger.LogDebug("No duplicate torrents found");
            return;
        }
        
        logger.LogInformation("Found {Count} torrents with duplicates", hashGroups.Count);
        
        var torrentsToDelete = new List<string>();
        
        foreach (var group in hashGroups)
        {
            var sorted = group.OrderByDescending(t => t.Added).ToList();
            var keep = sorted.First(); // Keep the newest one
            var toDelete = sorted.Skip(1).ToList(); // Delete the rest
            
            logger.LogInformation("Keeping torrent {TorrentId} ({Name}), deleting {Count} duplicates",
                keep.HostId, keep.Name, toDelete.Count);

            foreach (var torrent in toDelete)
            {
                try
                {
                    await realDebridClient.DeleteTorrentAsync(torrent.HostId);
                    logger.LogInformation("Deleted torrent {TorrentId} from Real Debrid", torrent.HostId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to delete torrent {TorrentId} from Real Debrid", torrent.HostId);
                }

                torrentsToDelete.Add(torrent.HostId);
            }

            if (torrentsToDelete.Any())
            {
                await store.PurgeTorrents(torrentsToDelete);
            }
        }
    }
}
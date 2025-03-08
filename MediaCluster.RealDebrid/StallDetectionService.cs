using MediaCluster.Common.Models.Configuration;
using MediaCluster.RealDebrid.Models;
using MediaCluster.RealDebrid.Models.External;
using MediaCluster.RealDebrid.SharedModels;
using Microsoft.Extensions.Options;

namespace MediaCluster.RealDebrid;

/// <summary>
/// Service for detecting and managing stalled or problematic torrents
/// </summary>
public class StallDetectionService(
    TorrentInformationStore store,
    IOptions<AppConfig> options,
    RealDebridClient realDebridClient,
    ILogger<StallDetectionService> logger) : BackgroundService
{
    private readonly RealDebridConfig _config = options.Value.RealDebrid;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromMinutes(10); // Check every 10 minutes

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for store to initialize first
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DetectAndHandleStalledTorrentsAsync(stoppingToken);
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in stall detection service");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private async Task DetectAndHandleStalledTorrentsAsync(CancellationToken stoppingToken)
    {
        // Get all active downloading torrents
        var activeTorrents = store.GetAllTorrents()
            .Where(t => 
                t.TorrentState == TorrentState.Active && 
                (t.RemoteStatus == RemoteStatus.Downloading || 
                 t.RemoteStatus == RemoteStatus.Queued || 
                 t.RemoteStatus == RemoteStatus.StalledDownload))
            .ToList();
        
        if (activeTorrents.Count == 0)
        {
            logger.LogDebug("No active downloading torrents to check for stalls");
            return;
        }
        
        logger.LogInformation("Checking {Count} active torrents for stalls", activeTorrents.Count);
        
        // Get fresh info from RealDebrid for these torrents
        var activeTorrentInfos = new Dictionary<string, TorrentInfoDto>();
        
        foreach (var torrent in activeTorrents)
        {
            if (stoppingToken.IsCancellationRequested)
                break;
                
            try
            {
                var torrentInfo = await realDebridClient.GetTorrentInfoAsync(torrent.HostId);
                activeTorrentInfos[torrent.HostId] = torrentInfo;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting torrent info for {TorrentId}", torrent.HostId);
            }
        }
        
        // Process each torrent
        foreach (var torrent in activeTorrents)
        {
            if (stoppingToken.IsCancellationRequested)
                break;
                
            try
            {
                if (!activeTorrentInfos.TryGetValue(torrent.HostId, out var torrentInfo))
                {
                    continue;
                }
                
                // Update performance tracking data
                var remoteStatus = torrentInfo.Status.ToRemoteStatus();
                long downloadSpeed = GetDownloadSpeed(torrentInfo);
                int seeders = GetSeeders(torrentInfo);
                
                bool statusChanged = torrent.RemoteStatus != remoteStatus;
                bool speedChanged = torrent.LastDownloadSpeed != downloadSpeed;
                
                torrent.RemoteStatus = remoteStatus;
                torrent.LastDownloadSpeed = downloadSpeed;
                torrent.Seeders = seeders;
                
                // Detect stalled downloads
                if (remoteStatus == RemoteStatus.Downloading || remoteStatus == RemoteStatus.StalledDownload)
                {
                    bool isStalled = false;
                    
                    // 1. Check if marked as stalled by RealDebrid
                    if (remoteStatus == RemoteStatus.StalledDownload)
                    {
                        isStalled = true;
                    }
                    
                    // 2. Check download speed
                    if (downloadSpeed < _config.StallSpeedThresholdBytesPerSec)
                    {
                        if (torrent.StallDetectedAt == null)
                        {
                            torrent.StallDetectedAt = DateTime.UtcNow;
                        }
                        else
                        {
                            var stallDuration = DateTime.UtcNow - torrent.StallDetectedAt.Value;
                            if (stallDuration.TotalMinutes > _config.StallDetectionTimeMinutes)
                            {
                                isStalled = true;
                            }
                        }
                    }
                    else
                    {
                        // Reset stall detection if speed is good
                        torrent.StallDetectedAt = null;
                    }
                    
                    // 3. No seeders
                    if (seeders <= 0)
                    {
                        if (torrent.StallDetectedAt == null)
                        {
                            torrent.StallDetectedAt = DateTime.UtcNow;
                        }
                        else
                        {
                            var stallDuration = DateTime.UtcNow - torrent.StallDetectedAt.Value;
                            if (stallDuration.TotalMinutes > _config.StallDetectionTimeMinutes)
                            {
                                isStalled = true;
                            }
                        }
                    }
                    
                    // Handle stalled torrents
                    if (isStalled)
                    {
                        logger.LogWarning("Detected stalled torrent: {TorrentId} ({Name}), " +
                            "Status: {Status}, Speed: {Speed} bytes/s, Seeders: {Seeders}",
                            torrent.HostId, torrent.Name, remoteStatus, downloadSpeed, seeders);
                            
                        // Move to problematic state
                        torrent.TorrentState = TorrentState.Problematic;
                        torrent.ProblemReason = seeders <= 0 ? TorrentProblemReason.NoSeeders : TorrentProblemReason.Stalled;
                        torrent.ProblemDetails = $"Stalled download: Status={remoteStatus}, " +
                            $"Speed={downloadSpeed} bytes/s, Seeders={seeders}";
                            
                        await store.MoveToProblematicAsync(torrent);
                        
                        // Delete from RealDebrid to free up slot
                        try
                        {
                            await realDebridClient.DeleteTorrentAsync(torrent.HostId);
                            logger.LogInformation("Deleted stalled torrent from RealDebrid: {TorrentId}", torrent.HostId);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error deleting stalled torrent from RealDebrid: {TorrentId}", torrent.HostId);
                        }
                    }
                    else if (speedChanged || statusChanged)
                    {
                        // Save changes to disk if something important changed
                        await store.SaveTorrentToDiskAsync(torrent);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing torrent {TorrentId} for stall detection", torrent.HostId);
            }
        }
    }
    
    private long GetDownloadSpeed(TorrentInfoDto torrentInfo)
    {
        return (long) Math.Ceiling(torrentInfo.Speed ?? 0d);
    }
    
    private int GetSeeders(TorrentInfoDto torrentInfo)
    {
        return torrentInfo.Seeders ?? 0;
    }
}
using MediaCluster.Common.Models.Configuration;
using MediaCluster.RealDebrid.Models.External;
using MediaCluster.RealDebrid.SharedModels;
using Microsoft.Extensions.Options;

namespace MediaCluster.RealDebrid;

/// <summary>
/// Service for managing dormant torrents (torrents temporarily removed from RealDebrid)
/// </summary>
public class DormantTorrentService(
    TorrentInformationStore store,
    IOptions<AppConfig> options,
    RealDebridClient realDebridClient,
    RealDebridRepository realDebridRepository,
    ILogger<DormantTorrentService> logger) : BackgroundService
{
    private readonly RealDebridConfig _config = options.Value.RealDebrid;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromHours(4); // Check every 4 hours
    private readonly SemaphoreSlim _processingLock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.EnableDormantTorrents)
        {
            logger.LogInformation("Dormant torrents feature is disabled");
            return;
        }

        // Wait for store to initialize first
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTorrentsAsync(stoppingToken);
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in dormant torrent service");
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
        }
    }

    private async Task ProcessTorrentsAsync(CancellationToken stoppingToken)
    {
        await _processingLock.WaitAsync(stoppingToken);
        
        try
        {
            // 1. Make dormant torrents that haven't been accessed recently
            await MakeDormantTorrentsAsync(stoppingToken);
            
            // 2. Verify a batch of dormant torrents to ensure they still work
            await VerifyDormantTorrentsAsync(stoppingToken);
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private async Task MakeDormantTorrentsAsync(CancellationToken stoppingToken)
    {
        // Find active downloaded torrents that haven't been accessed recently
        var cutoffTime = DateTime.UtcNow.AddHours(-_config.KeepActiveDurationHours);
        
        var candidatesForDormancy = store.GetAllTorrents()
            .Where(t => 
                t.TorrentState == TorrentState.Active && 
                t.RemoteStatus == RemoteStatus.Downloaded &&
                (t.LastAccessed == null || t.LastAccessed < cutoffTime))
            .ToList();
        
        if (candidatesForDormancy.Count == 0)
        {
            logger.LogDebug("No candidates for dormancy found");
            return;
        }
        
        logger.LogInformation("Found {Count} torrents to make dormant", candidatesForDormancy.Count);
        
        foreach (var torrent in candidatesForDormancy)
        {
            if (stoppingToken.IsCancellationRequested)
                break;
                
            try
            {
                // Get the full torrent info to verify file count matches link count
                var torrentInfo = await realDebridClient.GetTorrentInfoAsync(torrent.HostId);
                
                // Verify that the number of links matches selected files
                var selectedFileCount = torrentInfo.Files.Count(f => f.Selected == 1);
                if (torrentInfo.Links.Count != selectedFileCount)
                {
                    logger.LogWarning("Mismatch between selected files ({Selected}) and links ({Links}) for torrent {Id}",
                        selectedFileCount, torrentInfo.Links.Count, torrent.HostId);
                    
                    // Mark as problematic
                    torrent.TorrentState = TorrentState.Problematic;
                    torrent.ProblemReason = TorrentProblemReason.BrokenLinks;
                    torrent.ProblemDetails = $"Mismatch between selected files ({selectedFileCount}) and links ({torrentInfo.Links.Count})";
                    
                    await store.MoveToProblematicAsync(torrent);
                    continue;
                }
                
                // Verify all links are valid before making dormant
                bool allLinksValid = true;
                
                // Try to check each link
                foreach (var link in torrentInfo.Links)
                {
                    try
                    {
                        var linkCheck = await realDebridClient.CheckLinkAsync(link);
                        
                        // If link is not downloadable, mark as invalid
                        if ((linkCheck?.Supported ?? 0) == 0)
                        {
                            allLinksValid = false;
                            
                            logger.LogWarning("Link verification failed for link in torrent {TorrentId}: {Link}",
                                torrent.HostId, link);
                                
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error checking link for torrent {TorrentId}", torrent.HostId);
                        allLinksValid = false;
                        break;
                    }
                }
                
                if (allLinksValid)
                {
                    // Remove from RealDebrid but keep in our store
                    await realDebridClient.DeleteTorrentAsync(torrent.HostId);
                    
                    // Update state
                    torrent.TorrentState = TorrentState.Dormant;
                    torrent.LastVerified = DateTime.UtcNow;
                    
                    logger.LogInformation("Made torrent dormant: {TorrentId} ({Name})", 
                        torrent.HostId, torrent.Name);
                    
                    await store.SaveTorrentToDiskAsync(torrent);
                }
                else
                {
                    // Mark as problematic
                    torrent.TorrentState = TorrentState.Problematic;
                    torrent.ProblemReason = TorrentProblemReason.LinkVerificationFailed;
                    
                    logger.LogWarning("Marked torrent as problematic due to link verification failure: {TorrentId} ({Name})",
                        torrent.HostId, torrent.Name);
                    
                    await store.MoveToProblematicAsync(torrent);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing torrent {TorrentId} for dormancy", torrent.HostId);
            }
        }
    }
    
    private async Task VerifyDormantTorrentsAsync(CancellationToken stoppingToken)
    {
        // Get a batch of dormant torrents to verify
        var dormantTorrents = store.GetAllTorrents()
            .Where(t => t.TorrentState == TorrentState.Dormant)
            .OrderBy(t => t.LastVerified ?? DateTime.MinValue)
            .Take(_config.DormantTorrentsVerificationBatchSize)
            .ToList();
            
        if (dormantTorrents.Count == 0)
        {
            logger.LogDebug("No dormant torrents to verify");
            return;
        }
        
        logger.LogInformation("Verifying {Count} dormant torrents", dormantTorrents.Count);
        
        foreach (var torrent in dormantTorrents)
        {
            if (stoppingToken.IsCancellationRequested)
                break;
                
            try
            {
                // To verify a dormant torrent, we need to temporarily re-add it to RealDebrid
                bool isValid = await VerifyDormantTorrentAsync(torrent, stoppingToken);
                torrent.LastVerified = DateTime.UtcNow;
                torrent.VerificationAttempts++;
                
                if (isValid)
                {
                    logger.LogInformation("Verified dormant torrent is still valid: {TorrentId} ({Name})",
                        torrent.HostId, torrent.Name);
                        
                    await store.SaveTorrentToDiskAsync(torrent);
                }
                else
                {
                    // Mark as problematic
                    torrent.TorrentState = TorrentState.Problematic;
                    torrent.ProblemReason = TorrentProblemReason.LinkVerificationFailed;
                    
                    logger.LogWarning("Dormant torrent failed verification: {TorrentId} ({Name})",
                        torrent.HostId, torrent.Name);
                        
                    await store.MoveToProblematicAsync(torrent);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error verifying dormant torrent {TorrentId}", torrent.HostId);
            }
        }
    }
    
    /// <summary>
    /// Verify a dormant torrent by temporarily re-adding it to RealDebrid
    /// </summary>
    private async Task<bool> VerifyDormantTorrentAsync(RemoteContainer torrent, CancellationToken stoppingToken)
    {
        // We need to re-add the torrent to RealDebrid
        string magnetLink = $"magnet:?xt=urn:btih:{torrent.TorrentHash}";
        string temporaryTorrentId = string.Empty;
        
        try
        {
            // Add the magnet link to RealDebrid
            var response = await realDebridClient.AddMagnetAsync(magnetLink);
            temporaryTorrentId = response.Id;
            
            await realDebridClient.SelectTorrentFilesAsync(response.Id, "all");
            
            // Wait for the torrent to be ready (should be instant since we've had it before)
            var timeout = TimeSpan.FromSeconds(_config.InitialTorrentWaitTimeSeconds);
            var pollingInterval = TimeSpan.FromSeconds(_config.InitialTorrentPollingIntervalSeconds);
            
            var startTime = DateTime.UtcNow;
            TorrentInfoDto torrentInfo;
            
            while (!stoppingToken.IsCancellationRequested)
            {
                torrentInfo = await realDebridClient.GetTorrentInfoAsync(response.Id);
                
                if (torrentInfo.Status == "downloaded")
                {
                    logger.LogInformation("Verification torrent is ready: {TorrentId}", response.Id);
                    
                    // Check if the number of links matches selected files
                    var selectedFileCount = torrentInfo.Files.Count(f => f.Selected == 1);
                    if (torrentInfo.Links.Count != selectedFileCount)
                    {
                        logger.LogWarning("Mismatch between selected files ({Selected}) and links ({Links}) during verification",
                            selectedFileCount, torrentInfo.Links.Count);
                        return false;
                    }
                    
                    // Try to check each link
                    foreach (var link in torrentInfo.Links)
                    {
                        try
                        {
                            var linkCheck = await realDebridClient.CheckLinkAsync(link);
                            
                            // If link is not downloadable, verification failed
                            if ((linkCheck?.Supported ?? 0) == 0)
                            {
                                logger.LogWarning("Link verification failed during dormant check: {Link}", link);
                                return false;
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error checking link during dormant verification");
                            return false;
                        }
                    }
                    
                    // All links verified successfully
                    return true;
                }
                
                // Check if we hit an error status
                if (torrentInfo.Status is "error" or "magnet_error" or "virus" or "dead")
                {
                    logger.LogWarning("Torrent verification failed with status: {Status}", torrentInfo.Status);
                    return false;
                }
                
                // Check if timeout occurred
                if (DateTime.UtcNow - startTime > timeout)
                {
                    logger.LogWarning("Timeout waiting for verification torrent: {TorrentId}", response.Id);
                    return false;
                }
                
                await Task.Delay(pollingInterval, stoppingToken);
            }
            
            stoppingToken.ThrowIfCancellationRequested();
            return false; // Should never reach here unless canceled
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error verifying dormant torrent");
            return false;
        }
        finally
        {
            // Always clean up the temporary torrent
            if (!string.IsNullOrEmpty(temporaryTorrentId))
            {
                try
                {
                    await realDebridClient.DeleteTorrentAsync(temporaryTorrentId);
                    logger.LogInformation("Deleted temporary verification torrent: {TorrentId}", temporaryTorrentId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error deleting temporary verification torrent: {TorrentId}", temporaryTorrentId);
                }
            }
        }
    }
    
    /// <summary>
    /// Restore a dormant torrent to active state for access
    /// </summary>
    public async Task<RemoteContainer> RestoreDormantTorrentAsync(string torrentHash, CancellationToken stoppingToken = default)
    {
        await _processingLock.WaitAsync(stoppingToken);
        
        try
        {
            // Find the dormant torrent by hash
            var torrent = store.GetTorrentsByHash(torrentHash)
                .FirstOrDefault(t => t.TorrentState == TorrentState.Dormant);
                
            if (torrent == null)
            {
                throw new InvalidOperationException($"Dormant torrent with hash {torrentHash} not found");
            }
            
            logger.LogInformation("Restoring dormant torrent: {TorrentId} ({Name})", 
                torrent.HostId, torrent.Name);
                
            // We need to re-add the torrent to RealDebrid
            string magnetLink = $"magnet:?xt=urn:btih:{torrent.TorrentHash}";
            
            try
            {
                // Add the magnet link to RealDebrid
                var response = await realDebridClient.AddMagnetAsync(magnetLink);
                await realDebridClient.SelectTorrentFilesAsync(response.Id, "all");
                
                // Get updated torrent info
                var torrentInfo = await realDebridClient.GetTorrentInfoAsync(response.Id);
                
                // Wait for the torrent to be ready (already downloaded since we've had it before)
                var timeout = TimeSpan.FromSeconds(_config.InitialTorrentWaitTimeSeconds);
                var pollingInterval = TimeSpan.FromSeconds(_config.InitialTorrentPollingIntervalSeconds);
                
                var startTime = DateTime.UtcNow;
                
                while (!stoppingToken.IsCancellationRequested)
                {
                    torrentInfo = await realDebridClient.GetTorrentInfoAsync(response.Id);
                    
                    if (torrentInfo.Status == "downloaded")
                    {
                        logger.LogInformation("Restored torrent is ready: {TorrentId} ({Name})", 
                            response.Id, torrentInfo.Filename);
                        break;
                    }
                    
                    // Check if we hit an error status
                    if (torrentInfo.Status is "error" or "magnet_error" or "virus" or "dead")
                    {
                        logger.LogWarning("Torrent restoration failed with status: {Status}", torrentInfo.Status);
                        throw new InvalidOperationException($"Torrent failed with status: {torrentInfo.Status}");
                    }
                    
                    // Check if timeout occurred
                    if (DateTime.UtcNow - startTime > timeout)
                    {
                        logger.LogWarning("Timeout waiting for restored torrent: {TorrentId}", response.Id);
                        throw new TimeoutException("Timeout waiting for torrent to be ready");
                    }
                    
                    await Task.Delay(pollingInterval, stoppingToken);
                }
                
                // Verify that the number of links matches selected files
                var selectedFileCount = torrentInfo.Files.Count(f => f.Selected == 1);
                if (torrentInfo.Links.Count != selectedFileCount)
                {
                    logger.LogWarning("Mismatch between selected files ({Selected}) and links ({Links}) for restored torrent {Id}",
                        selectedFileCount, torrentInfo.Links.Count, response.Id);
                        
                    torrent.TorrentState = TorrentState.Problematic;
                    torrent.ProblemReason = TorrentProblemReason.BrokenLinks;
                    torrent.ProblemDetails = $"Mismatch between selected files ({selectedFileCount}) and links ({torrentInfo.Links.Count})";
                    
                    await store.MoveToProblematicAsync(torrent);
                    
                    throw new InvalidOperationException("Mismatch between selected files and links");
                }
                
                // Update our container with the new RealDebrid ID
                torrent.HostId = response.Id;
                torrent.TorrentState = TorrentState.Active;
                torrent.LastAccessed = DateTime.UtcNow;
                
                // Re-unrestrict all links
                var unrestrictedLinks = new List<UnrestrictLinkResponseDto>();
                var brokenLinks = new List<string>();
                
                foreach (var link in torrentInfo.Links)
                {
                    try
                    {
                        var unrestrictedLink = await realDebridClient.UnrestrictLinkAsync(link);
                        unrestrictedLinks.Add(unrestrictedLink);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to unrestrict link {Link} for restored torrent {TorrentId}",
                            link, response.Id);
                        brokenLinks.Add(link);
                    }
                }
                
                // Update torrent files with new unrestricted links
                await store.UpdateTorrentFilesAsync(response.Id, torrentInfo, unrestrictedLinks, brokenLinks);
                
                if (brokenLinks.Count > 0)
                {
                    logger.LogWarning("Restored torrent has {Count} broken links", brokenLinks.Count);
                    
                    // If all links are broken, mark as problematic
                    if (brokenLinks.Count == torrentInfo.Links.Count)
                    {
                        torrent.TorrentState = TorrentState.Problematic;
                        torrent.ProblemReason = TorrentProblemReason.BrokenLinks;
                        torrent.ProblemDetails = "All links are broken after restoration";
                        
                        await store.MoveToProblematicAsync(torrent);
                        
                        throw new InvalidOperationException("All links in restored torrent are broken");
                    }
                }
                
                return torrent;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error restoring dormant torrent {TorrentId}", torrent.HostId);
                
                // Mark as problematic if restore failed
                torrent.TorrentState = TorrentState.Problematic;
                torrent.ProblemReason = TorrentProblemReason.RealDebridApiError;
                torrent.ProblemDetails = ex.Message;
                
                await store.MoveToProblematicAsync(torrent);
                
                throw;
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }
}
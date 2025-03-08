using MediaCluster.Common.Models.Configuration;
using MediaCluster.RealDebrid.Models;
using MediaCluster.RealDebrid.Models.External;
using MediaCluster.RealDebrid.SharedModels;
using Microsoft.Extensions.Options;

namespace MediaCluster.RealDebrid;

public class TorrentMonitorService(
    IOptions<AppConfig> options,
    ILoggerFactory loggerFactory,
    RealDebridClient realDebridClient,
    TorrentInformationStore store) : BackgroundService
{
    private readonly RealDebridConfig _config = options.Value.RealDebrid;
    private readonly ILogger<TorrentMonitorService> _logger = loggerFactory.CreateLogger<TorrentMonitorService>();
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _completedCheckInterval = TimeSpan.FromMinutes(5);
    private DateTime _lastCompletedCheck = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await store.InitializeAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int pageSize = 100; //_config.ItemsPerPage;
                var allTorrents = new List<TorrentItemDto>();
                var page = 1;
                List<TorrentItemDto> currentPage;

                do
                {
                    currentPage = (await realDebridClient.GetTorrentsAsync(page: page, limit: pageSize));
                    allTorrents.AddRange(currentPage);
                    page++;
                } while (currentPage.Count == pageSize);

                var now = DateTime.UtcNow;
                if (now - _lastCompletedCheck > _completedCheckInterval)
                {
                    await ProcessCompletedTorrentsAsync(allTorrents);
                    _lastCompletedCheck = now;
                }

                await store.UpdateAllRemoteContainersAsync(allTorrents);

                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in torrent monitoring service");
                throw;
                await Task.Delay(_pollingInterval, stoppingToken);
            }
        }
    }

    private async Task ProcessCompletedTorrentsAsync(List<TorrentItemDto> remoteTorrents)
    {
        var completedTorrents = remoteTorrents
            .Where(t => t.Status.ToRemoteStatus() == RemoteStatus.Downloaded)
            .ToList();

        foreach (var torrent in completedTorrents)
        {
            var storedTorrent = store.GetTorrentById(torrent.Id);

            if (storedTorrent is { RemoteStatus: RemoteStatus.Downloaded })
            {
                continue;
            }

            try
            {
                _logger.LogInformation("Processing completed torrent: {TorrentId} ({Name})",
                    torrent.Id, torrent.Filename);

                // Get detailed torrent info to verify file count
                var torrentInfo = await realDebridClient.GetTorrentInfoAsync(torrent.Id);

                if (storedTorrent == null)
                {
                    await store.AddRemoteContainerAsync(torrentInfo.ToDomain());
                }

                // Check for blocked file extensions
                await CheckForBlockedFileExtensionsAsync(torrent);

                // If the torrent was marked problematic by the extension check, skip further processing
                storedTorrent = store.GetTorrentById(torrent.Id);
                if (storedTorrent?.TorrentState == TorrentState.Problematic)
                {
                    continue;
                }

                // Verify that the number of links matches selected files
                var selectedFileCount = torrentInfo.Files.Count(f => f.Selected == 1);
                if (torrent.Links.Count != selectedFileCount)
                {
                    _logger.LogWarning(
                        "Mismatch between selected files ({Selected}) and links ({Links}) for torrent {Id}",
                        selectedFileCount, torrent.Links.Count, torrent.Id);
                    continue;
                }

                // Process each link to unrestrict it
                var unrestrictedLinks = new List<UnrestrictLinkResponseDto>();
                var brokenLinks = new List<string>();
                foreach (var link in torrent.Links)
                {
                    try
                    {
                        var unrestrictedLink = await realDebridClient.UnrestrictLinkAsync(link);
                        unrestrictedLinks.Add(unrestrictedLink);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to unrestrict link {Link} for torrent {Id}", link, torrent.Id);
                        brokenLinks.Add(link);
                    }
                }

                await store.UpdateTorrentFilesAsync(torrent.Id, torrentInfo, unrestrictedLinks, brokenLinks);

                _logger.LogInformation("Successfully processed torrent {Id} with {Count} files",
                    torrent.Id, unrestrictedLinks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing completed torrent {Id}", torrent.Id);
            }
        }
    }

    private async Task CheckForBlockedFileExtensionsAsync(TorrentItemDto torrent)
    {
        if (string.IsNullOrEmpty(_config.BlockedFileExtensions))
            return;

        var blockedExtensions = _config.BlockedFileExtensions
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().ToLowerInvariant())
            .ToHashSet();

        // Skip if no extensions to block
        if (blockedExtensions.Count == 0)
            return;

        try
        {
            var torrentInfo = await realDebridClient.GetTorrentInfoAsync(torrent.Id);

            foreach (var file in torrentInfo.Files)
            {
                var extension = Path.GetExtension(file.Path)?.TrimStart('.')?.ToLowerInvariant();

                if (!string.IsNullOrEmpty(extension) && blockedExtensions.Contains(extension))
                {
                    _logger.LogWarning("Torrent {TorrentId} ({Name}) contains blocked file extension: {Extension}",
                        torrent.Id, torrent.Filename, extension);

                    // Mark as problematic in store
                    var container = store.GetTorrentById(torrent.Id);
                    if (container != null)
                    {
                        container.TorrentState = TorrentState.Problematic;
                        container.ProblemReason = TorrentProblemReason.Other;
                        container.ProblemDetails = $"Contains blocked file extension: {extension}";

                        await store.MoveToProblematicAsync(container);
                    }

                    // Delete from RealDebrid
                    await realDebridClient.DeleteTorrentAsync(torrent.Id);
                    _logger.LogInformation("Deleted torrent with blocked extension from RealDebrid: {TorrentId}",
                        torrent.Id);

                    break; // No need to check other files
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking torrent {TorrentId} for blocked extensions", torrent.Id);
        }
    }
}
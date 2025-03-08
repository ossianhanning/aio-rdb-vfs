using System.Text.RegularExpressions;
using MediaCluster.Common.Models.Configuration;
using MediaCluster.RealDebrid.Models;
using MediaCluster.RealDebrid.Models.External;
using MediaCluster.RealDebrid.SharedModels;
using Microsoft.Extensions.Options;

namespace MediaCluster.RealDebrid;

public class RealDebridRepository(
    TorrentInformationStore torrentInformationStore,
    RealDebridClient realDebridClient,
    IOptions<AppConfig> _config,
    ILogger<RealDebridRepository> logger,
    TorrentInformationStore store)
{
    /// <summary>
    /// Get all torrents regardless of pagination limits
    /// </summary>
    /// <param name="filter">Optional filter: "active" to list active torrents only</param>
    /// <returns>Complete list of torrents</returns>
    internal async Task<List<RemoteContainer>> GetAllTorrentsAsync(string filter = null)
    {
        const int pageSize = 5000;
        var allTorrents = new List<TorrentItemDto>();
        var page = 1;
        List<TorrentItemDto> currentPage;

        do
        {
            currentPage = await realDebridClient.GetTorrentsAsync(page: page, limit: pageSize, filter: filter);
            allTorrents.AddRange(currentPage);
            page++;
        } while (currentPage.Count == pageSize); // If we got a full page, there might be more

        return allTorrents.ToDomain().ToList();
    }

    /// <summary>
    /// Check if a torrent is complete and get its download links
    /// </summary>
    /// <param name="id">Torrent ID</param>
    /// <returns>Tuple with completion status and download links if complete</returns>
    internal async Task<(bool isComplete, List<string>?)> CheckTorrentCompletionAsync(string id)
    {
        var torrentInfo = await realDebridClient.GetTorrentInfoAsync(id);
        bool isComplete = torrentInfo.Status == "downloaded";
        return (isComplete, torrentInfo.Links?.ToList());
    }

    /// <summary>
    /// Unrestrict all links from a completed torrent
    /// </summary>
    /// <param name="id">Torrent ID</param>
    /// <returns>List of unrestricted links</returns>
    internal async Task<List<UnrestrictLinkResponseDto>> UnrestrictTorrentLinksAsync(string id)
    {
        var (isComplete, links) = await CheckTorrentCompletionAsync(id);

        if (!isComplete || links == null || links.Count == 0)
            return new List<UnrestrictLinkResponseDto>();

        var unrestrictedLinks = new List<UnrestrictLinkResponseDto>();

        foreach (var link in links)
        {
            var unrestricted = await realDebridClient.UnrestrictLinkAsync(link);
            unrestrictedLinks.Add(unrestricted);
        }

        return unrestrictedLinks;
    }

    /// <summary>
    /// Monitor a torrent until it completes
    /// </summary>
    /// <param name="id">Torrent ID</param>
    /// <param name="pollingIntervalMs">Polling interval in milliseconds</param>
    /// <param name="timeoutMs">Timeout in milliseconds (0 for no timeout)</param>
    /// <returns>True if torrent completed, false if timeout occurred</returns>
    internal async Task<bool> WaitForTorrentCompletionAsync(string id, int pollingIntervalMs = 5000, int timeoutMs = 0)
    {
        var startTime = DateTime.UtcNow;

        while (true)
        {
            var torrentInfo = await realDebridClient.GetTorrentInfoAsync(id);

            if (torrentInfo.Status == "downloaded")
                return true;

            if (timeoutMs > 0 && (DateTime.UtcNow - startTime).TotalMilliseconds > timeoutMs)
                return false;

            await Task.Delay(pollingIntervalMs);
        }
    }

    public async Task DeleteTorrentAsync(string torrentId)
    {
        await realDebridClient.DeleteTorrentAsync(torrentId);
    }

    /// <summary>
    /// Add a torrent file, wait for it to start, and select all files
    /// </summary>
    /// <param name="torrentFileContent">Content of the torrent file</param>
    /// <param name="waitForDownload">Whether to wait for the torrent to start downloading</param>
    /// <param name="host">Optional hoster domain</param>
    /// <param name="category">Optional category</param>
    /// <param name="tags">Optional tags</param>
    /// <returns>Torrent ID</returns>
    public async Task<string> AddTorrentAndStartAsync(
        byte[] torrentFileContent, 
        bool waitForDownload = true,
        string host = "real-debrid.com", 
        string? category = null, 
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        // Check for blocked extensions
        var (hasBlocked, extension) = CheckForBlockedExtensions(torrentFileContent);
        if (hasBlocked)
        {
            throw new InvalidOperationException($"Torrent contains blocked file extension: {extension}");
        }
    
        var response = await realDebridClient.AddTorrentAsync(torrentFileContent, host, category, tags);
        await realDebridClient.SelectTorrentFilesAsync(response.Id, "all");
    
        var torrentInfo = await realDebridClient.GetTorrentInfoAsync(response.Id);
        torrentInfo.Category = category;
        torrentInfo.Tags = tags;
    
        var container = torrentInfo.ToDomain();
        container.LastAccessed = DateTime.UtcNow;
        await torrentInformationStore.AddRemoteContainerAsync(container);
    
        if (waitForDownload)
        {
            await WaitForTorrentToStartAsync(response.Id, cancellationToken);
            torrentInfo = await realDebridClient.GetTorrentInfoAsync(response.Id);
            var selectedFileCount = torrentInfo.Files.Count(f => f.Selected == 1);
            if (torrentInfo.Links.Count != selectedFileCount)
            {
                logger.LogWarning(
                    "Mismatch between selected files ({Selected}) and links ({Links}) for torrent {Id}",
                    selectedFileCount, torrentInfo.Links.Count, torrentInfo.Id);
                return response.Id;
            }

            // Process each link to unrestrict it
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
                    logger.LogError(ex, "Failed to unrestrict link {Link} for torrent {Id}", link, torrentInfo.Id);
                    brokenLinks.Add(link);
                }
            }

            await store.UpdateTorrentFilesAsync(torrentInfo.Id, torrentInfo, unrestrictedLinks, brokenLinks);

            logger.LogInformation("Successfully processed torrent {Id} with {Count} files",
                torrentInfo.Id, unrestrictedLinks.Count);
        }
    
        return response.Id;
    }

    /// <summary>
    /// Add a magnet link, wait for it to start, and select all files
    /// </summary>
    /// <param name="magnetLink">Magnet link</param>
    /// <param name="waitForDownload">Whether to wait for the torrent to start downloading</param>
    /// <param name="host">Optional hoster domain</param>
    /// <param name="category">Optional category</param>
    /// <param name="tags">Optional tags</param>
    /// <returns>Torrent ID</returns>
    public async Task<string> AddMagnetAndStartAsync(
        string magnetLink,
        bool waitForDownload = true,
        string host = "real-debrid.com",
        string? category = null,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var response = await realDebridClient.AddMagnetAsync(magnetLink, host);
        await realDebridClient.SelectTorrentFilesAsync(response.Id, "all");

        var torrentInfo = await realDebridClient.GetTorrentInfoAsync(response.Id);
        torrentInfo.Category = category;
        torrentInfo.Tags = tags;

        var container = torrentInfo.ToDomain();
        container.RemoteStatus = RemoteStatus.Downloading; // Don't let it be set to downloaded
        container.LastAccessed = DateTime.UtcNow;
        await torrentInformationStore.AddRemoteContainerAsync(container);

        if (waitForDownload)
        {
            await WaitForTorrentToStartAsync(response.Id, cancellationToken);
            torrentInfo = await realDebridClient.GetTorrentInfoAsync(response.Id);
            var selectedFileCount = torrentInfo.Files.Count(f => f.Selected == 1);
            if (torrentInfo.Links.Count != selectedFileCount)
            {
                logger.LogWarning(
                    "Mismatch between selected files ({Selected}) and links ({Links}) for torrent {Id}",
                    selectedFileCount, torrentInfo.Links.Count, torrentInfo.Id);
                return response.Id;
            }

            // Process each link to unrestrict it
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
                    logger.LogError(ex, "Failed to unrestrict link {Link} for torrent {Id}", link, torrentInfo.Id);
                    brokenLinks.Add(link);
                }
            }

            await store.UpdateTorrentFilesAsync(torrentInfo.Id, torrentInfo, unrestrictedLinks, brokenLinks);

            logger.LogInformation("Successfully processed torrent {Id} with {Count} files",
                torrentInfo.Id, unrestrictedLinks.Count);
        }

        return response.Id;
    }

    /// <summary>
    /// Wait for a torrent to start downloading or finish with a timeout
    /// </summary>
    private async Task WaitForTorrentToStartAsync(string torrentId, CancellationToken cancellationToken = default)
    {
        var timeout = TimeSpan.FromSeconds(_config.Value.RealDebrid.InitialTorrentWaitTimeSeconds);
        var pollingInterval = TimeSpan.FromSeconds(_config.Value.RealDebrid.InitialTorrentPollingIntervalSeconds);
        var startTime = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            var torrentInfo = await realDebridClient.GetTorrentInfoAsync(torrentId);
            var status = torrentInfo.Status.ToRemoteStatus();

            // Successfully started or completed
            if (status == RemoteStatus.Downloading ||
                status == RemoteStatus.Downloaded ||
                status == RemoteStatus.Compressing ||
                status == RemoteStatus.Uploading)
            {
                return;
            }

            // Error conditions
            if (status == RemoteStatus.Error ||
                status == RemoteStatus.MagnetError ||
                status == RemoteStatus.Virus ||
                status == RemoteStatus.Dead ||
                status == RemoteStatus.StalledDownload)
            {
                throw new InvalidOperationException($"Torrent failed to start: {status}");
            }

            // Check timeout
            if (DateTime.UtcNow - startTime > timeout)
            {
                throw new TimeoutException($"Timed out waiting for torrent {torrentId} to start downloading");
            }

            await Task.Delay(pollingInterval, cancellationToken);
        }

        // Cancelled
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Check if a torrent contains any blocked file extensions
    /// </summary>
    /// <param name="torrentFileContent">Content of the torrent file</param>
    /// <returns>Tuple with (hasBlockedExtension, detectedExtension)</returns>
    private (bool hasBlockedExtension, string detectedExtension) CheckForBlockedExtensions(byte[] torrentFileContent)
    {
        if (string.IsNullOrEmpty(_config.Value.RealDebrid.BlockedFileExtensions))
            return (false, string.Empty);

        var blockedExtensions = _config.Value.RealDebrid.BlockedFileExtensions
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().ToLowerInvariant())
            .ToHashSet();

        // Skip if no extensions to block
        if (blockedExtensions.Count == 0)
            return (false, string.Empty);

        try
        {
            // Parse the torrent file
            using var stream = new MemoryStream(torrentFileContent);

            // This is a basic check - for a proper implementation, use a torrent parsing library
            // Since we don't have a direct reference to one, this is just a pattern-matching approach
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            // Simple pattern match for file paths
            var filePaths = ExtractFilePaths(content);

            foreach (var path in filePaths)
            {
                var extension = Path.GetExtension(path)?.TrimStart('.')?.ToLowerInvariant();

                if (!string.IsNullOrEmpty(extension) && blockedExtensions.Contains(extension))
                {
                    return (true, extension);
                }
            }
        }
        catch
        {
            // If parsing fails, we'll allow the torrent but log a warning
            return (false, string.Empty);
        }

        return (false, string.Empty);
    }

    /// <summary>
    /// Very basic method to extract file paths from a torrent file
    /// </summary>
    private List<string> ExtractFilePaths(string torrentContent)
    {
        var paths = new List<string>();

        // Extremely simplified - in a real implementation, use a proper torrent parser
        // This is just a pattern-matching approach that might catch some obvious cases
        var pathPattern = @"path(\d+):([^:]+)";
        var matches = Regex.Matches(torrentContent, pathPattern);

        foreach (Match match in matches)
        {
            if (match.Groups.Count > 2)
            {
                paths.Add(match.Groups[2].Value);
            }
        }

        return paths;
    }
}
using MediaCluster.Common.Models.Configuration;
using MediaCluster.QBittorrentApi.Models;
using MediaCluster.RealDebrid;
using MediaCluster.RealDebrid.SharedModels;
using Microsoft.Extensions.Options;

namespace MediaCluster.QBittorrentApi
{
    internal class QBittorrentApiService : IQBittorrentApiService
    {
        private readonly TorrentInformationStore _store;
        private readonly RealDebridRepository _realDebridRepository;
        private readonly ILogger<QBittorrentApiService> _logger;
        private readonly AppConfig _config;

        // API version
        private const string ApiVersion = "4.3.9";

        /// <summary>
        /// Create a new QBittorrent API service
        /// </summary>
        public QBittorrentApiService(
            IOptions<AppConfig> config,
            ILogger<QBittorrentApiService> logger,
            TorrentInformationStore store,
            RealDebridRepository realDebridRepository)
        {
            _logger = logger;
            _store = store;
            _realDebridRepository = realDebridRepository;
            _config = config.Value;

            _logger.LogInformation("QBittorrentApiService initialized with API version: {ApiVersion}", ApiVersion);
        }

        /// <inheritdoc/>
        public string GetApiVersion()
        {
            return ApiVersion;
        }

        /// <inheritdoc/>
public async Task<IReadOnlyList<TorrentInfoDto>> GetTorrentsAsync(
    string? filter = null,
    string? category = null,
    string? sort = null,
    bool reverse = false,
    int limit = 10000,
    int offset = 0,
    string? hashes = null,
    CancellationToken cancellationToken = default)
{
    _logger.LogDebug(
        "GetTorrents: filter={Filter}, category={Category}, sort={Sort}, reverse={Reverse}, limit={Limit}, offset={Offset}, hashes={Hashes}",
        filter, category, sort, reverse, limit, offset, hashes);

    try
    {
        var remoteTorrents = _store.GetAllTorrents();

        var hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(hashes))
        {
            var hashArray = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
            foreach (var hash in hashArray)
            {
                hashSet.Add(hash.Trim());
            }
        }

        // Convert to QBittorrent format
        var torrents = remoteTorrents
            .ToDomain(_config.FileSystem)
            .Where(t => string.IsNullOrEmpty(hashes) || hashSet.Contains(t.Hash))
            .Where(t => string.IsNullOrEmpty(category) || t.Category == category)
            .ToList();

        // Apply status filter if specified
        if (!string.IsNullOrEmpty(filter))
        {
            if (filter.Equals("downloading", StringComparison.OrdinalIgnoreCase))
            {
                torrents = torrents.Where(t =>
                    t.State == "downloading" ||
                    t.State == "stalledDL" ||
                    t.State == "queuedDL" ||
                    t.State == "metaDL").ToList();
            }
            else if (filter.Equals("seeding", StringComparison.OrdinalIgnoreCase))
            {
                torrents = torrents.Where(t =>
                    t.State == "uploading" ||
                    t.State == "stalledUP" ||
                    t.State == "queuedUP").ToList();
            }
            else if (filter.Equals("completed", StringComparison.OrdinalIgnoreCase))
            {
                torrents = torrents.Where(t =>
                    t.State == "uploading" ||
                    t.State == "stalledUP" ||
                    t.State == "queuedUP" ||
                    t.Progress == 1.0).ToList();
            }
            else if (filter.Equals("paused", StringComparison.OrdinalIgnoreCase))
            {
                torrents = torrents.Where(t => t.State == "pausedDL" || t.State == "pausedUP").ToList();
            }
            else if (filter.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                torrents = torrents.Where(t =>
                    t.State == "downloading" ||
                    t.State == "uploading" ||
                    t.DownloadSpeed > 0 ||
                    t.Progress < 1.0).ToList();
            }
            else if (filter.Equals("inactive", StringComparison.OrdinalIgnoreCase))
            {
                torrents = torrents.Where(t =>
                    t.State != "downloading" &&
                    t.State != "uploading" &&
                    t.DownloadSpeed == 0).ToList();
            }
            else if (filter.Equals("errored", StringComparison.OrdinalIgnoreCase))
            {
                torrents = torrents.Where(t => t.State == "error").ToList();
            }
        }

        // Sort if specified
        if (!string.IsNullOrEmpty(sort))
        {
            torrents = SortTorrents(torrents, sort, reverse);
        }

        // Apply pagination
        torrents = torrents.Skip(offset).Take(limit).ToList();

        _logger.LogDebug("Returning {Count} torrents", torrents.Count);
        return torrents;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting torrents: {Message}", ex.Message);
        throw;
    }
}

/// <inheritdoc/>
public async Task<TorrentPropertiesDto> GetTorrentPropertiesAsync(string hash,
    CancellationToken cancellationToken = default)
{
    _logger.LogDebug("GetTorrentProperties: hash={Hash}", hash);

    try
    {
        var torrent = _store.GetAllTorrents().SingleOrDefault(t => t.TorrentHash == hash);

        if (torrent == null)
        {
            throw new InvalidOperationException($"Torrent with hash {hash} not found");
        }

        // Convert to QBittorrent properties
        var properties = new TorrentPropertiesDto
        {
            SavePath = Path.Combine(
                _config.FileSystem.FileSystemMergedPath,
                Path.GetDirectoryName(torrent.Files.FirstOrDefault()?.LocalPath ?? "") ?? Path.Combine(_config.RealDebrid.DefaultVirtualFolderName, torrent.Name)),
            CreationDate = new DateTimeOffset(torrent.Added).ToUnixTimeSeconds(),
            AdditionDate = new DateTimeOffset(torrent.Added).ToUnixTimeSeconds(),
            CompletionDate = torrent.RemoteStatus == RemoteStatus.Downloaded ? 
                          new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() : 0,
            DownloadSpeed = torrent.LastDownloadSpeed ?? 0,
            ETA = torrent.TorrentState == TorrentState.Problematic ? 0 : 1800, // 30 minutes for active
            TotalSize = torrent.Files.Sum(f => f.Size),
        };

        _logger.LogDebug("Returning properties for torrent {Hash}", hash);
        return properties;
    }
    catch (Exception ex) when (!(ex is KeyNotFoundException))
    {
        _logger.LogError(ex, "Error getting torrent properties for {Hash}: {Message}", hash, ex.Message);
        throw;
    }
}

        /// <summary>
        /// Sort a list of torrents by the specified field
        /// </summary>
        private List<TorrentInfoDto> SortTorrents(List<TorrentInfoDto> torrents, string sort, bool reverse)
        {
            IOrderedEnumerable<TorrentInfoDto> ordered;

            switch (sort.ToLower())
            {
                case "name":
                    ordered = torrents.OrderBy(t => t.Name);
                    break;
                case "size":
                    ordered = torrents.OrderBy(t => t.Size);
                    break;
                case "progress":
                    ordered = torrents.OrderBy(t => t.Progress);
                    break;
                case "dlspeed":
                    ordered = torrents.OrderBy(t => t.DownloadSpeed);
                    break;
                case "eta":
                    ordered = torrents.OrderBy(t => t.Eta);
                    break;
                case "state":
                    ordered = torrents.OrderBy(t => t.State);
                    break;
                case "category":
                    ordered = torrents.OrderBy(t => t.Category);
                    break;
                case "tags":
                    ordered = torrents.OrderBy(t => t.Label);
                    break;
                case "added_on":
                    ordered = torrents.OrderBy(t => t.AddedOn);
                    break;
                case "completion_on":
                    ordered = torrents.OrderBy(t => t.CompletionOn);
                    break;
                case "tracker":
                    ordered = torrents.OrderBy(t => t.Tracker);
                    break;
                default:
                    // Default to sorting by added date
                    ordered = torrents.OrderBy(t => t.AddedOn);
                    break;
            }

            if (reverse)
            {
                return ordered.Reverse().ToList();
            }

            return ordered.ToList();
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<FileInfoDto>> GetTorrentFilesAsync(string hash,
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("GetTorrentFiles: hash={Hash}", hash);

            try
            {

                var torrent = _store.GetAllTorrents().SingleOrDefault(t => t.TorrentHash == hash);

                if (torrent == null)
                {
                    throw new InvalidOperationException($"Torrent with hash {hash} not found");
                }

                // Convert to QBittorrent format
                var result = torrent.Files.Select((file, index) => new FileInfoDto
                {
                    Index = index,
                    Name = Path.Combine(Path.GetFileName(Path.GetDirectoryName(Path.Combine(_config.FileSystem.FileSystemMergedPath, file.LocalPath.Replace("/", "\\"))) ?? string.Empty), Path.GetFileName(file.LocalPath)).Replace("\\", "/"),
                    Size = file.Size,
                    Progress = torrent.RemoteStatus == RemoteStatus.Downloaded ? 1 : 0,
                    Priority = 1,
                    IsSeed = false,
                    PieceRange = [],
                    Availability = 1.0,
                    Path = Path.Combine(_config.FileSystem.FileSystemMergedPath, file.LocalPath.Replace("/", "\\")),
                }).ToList();

                _logger.LogDebug("Returning {Count} files for torrent {Hash}", result.Count, hash);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting torrent files for {Hash}: {Message}", hash, ex.Message);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> AddTorrentAsync(
            IEnumerable<Stream>? torrentFiles = null,
            string? urls = null,
            string? savePath = null,
            string? cookie = null,
            string? category = null,
            string? tags = null,
            bool skip_checking = false,
            bool paused = false,
            bool stopped = false,
            bool root_folder = false,
            bool sequentialDownload = false,
            bool firstLastPiecePrio = false,
            string? contentLayout = null,
            float? ratioLimit = null,
            long? seedingTimeLimit = null,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "AddTorrent: files={FilesProvided}, urls={UrlsProvided}, savePath={SavePath}, category={Category}, tags={Tags}",
                torrentFiles != null, !string.IsNullOrEmpty(urls), savePath, category, tags);

            try
            {
                bool anyAdded = false;

                if (torrentFiles != null)
                {
                    foreach (var torrentFileStream in torrentFiles)
                    {
                        try
                        {
                            using var memoryStream = new MemoryStream();
                            await torrentFileStream.CopyToAsync(memoryStream, cancellationToken);
                            byte[] torrentData = memoryStream.ToArray();

                            string torrentId = await _realDebridRepository.AddTorrentAndStartAsync(torrentData, category: category, tags: tags?.Split(",").ToList());

                            _logger.LogInformation("Added torrent file to RealDebrid, ID: {TorrentId}", torrentId);
                            anyAdded = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error adding torrent file: {Message}", ex.Message);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(urls))
                {
                    var magnetLinks = urls.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var magnetLink in magnetLinks)
                    {
                        if (string.IsNullOrWhiteSpace(magnetLink))
                            continue;

                        try
                        {
                            string torrentId = await _realDebridRepository.AddMagnetAndStartAsync(magnetLink, category: category, tags: tags?.Split(",").ToList());

                            _logger.LogInformation("Added magnet link to RealDebrid, ID: {TorrentId}", torrentId);
                            anyAdded = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error adding magnet link: {Message}", ex.Message);
                        }
                    }
                }

                return anyAdded;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding torrent: {Message}", ex.Message);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteTorrentsAsync(string hashes, bool deleteFiles = false,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("DeleteTorrents: hashes={Hashes}, deleteFiles={DeleteFiles}", hashes, deleteFiles);

            try
            {
                var hashArray = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries);
                bool anyDeleted = false;

                foreach (var hash in hashArray)
                {
                    try
                    {
                        var torrents = _store.GetAllTorrents().Where(t => t.TorrentHash == hash).ToList();

                        if (!torrents.Any())
                        {
                            continue;
                        }

                        foreach (var torrent in torrents)
                        {
                            await _realDebridRepository.DeleteTorrentAsync(torrent.HostId);
                            await _store.PurgeTorrents([torrent.HostId]);

                            _logger.LogInformation("Deleted torrent with hash {Hash}, ID: {TorrentId}", hash,
                                torrent.HostId);
                            anyDeleted = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting torrent with hash {Hash}: {Message}", hash, ex.Message);
                    }
                }

                return anyDeleted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting torrents: {Message}", ex.Message);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<PreferencesDto> GetPreferencesAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("GetPreferences");

            // Create preferences with reasonable defaults
            var preferences = new PreferencesDto
            {
                DhtEnabled = false,
                SavePath = Path.Combine(_config.FileSystem.FileSystemMergedPath, _config.RealDebrid.DefaultVirtualFolderName),
            };

            return preferences;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<TrackerDto>> GetTrackersAsync(string hash,
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("GetTrackers: hash={Hash}", hash);

            var trackers = new List<TrackerDto>
            {
                new TrackerDto
                {
                    Url = "https://real-debrid.com/tracker",
                    Status = 2, // Working
                    NumPeers = 0,
                    NumSeeds = 0,
                    NumLeeches = 0,
                    NumDownloaded = 0,
                    Message = "This is a RealDebrid torrent"
                }
            };

            return trackers;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<string, CategoryDto>> GetCategoriesAsync(
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("GetCategories");

            var categories = new Dictionary<string, CategoryDto>();

            foreach (var category in _config.QBittorrent.Categories)
            {
                categories[category] = new CategoryDto
                {
                    Name = category,
                    SavePath = Path.Combine(_config.FileSystem.FileSystemMergedPath, _config.RealDebrid.DefaultVirtualFolderName),
                };
            }

            return categories;
        }

        /// <inheritdoc/>
        public async Task<bool> CreateCategoryAsync(string category, string? savePath = null,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("CreateCategory: category={Category}, savePath={SavePath}", category, savePath);

            if (!_config.QBittorrent.Categories.Contains(category))
            {
                _config.QBittorrent.Categories.Add(category);
                _logger.LogInformation("Added category: {Category}", category);
                return true;
            }

            return false;
        }
    }
}
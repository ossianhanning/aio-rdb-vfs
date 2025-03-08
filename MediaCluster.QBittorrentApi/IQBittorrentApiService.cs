using MediaCluster.QBittorrentApi.Models;

namespace MediaCluster.QBittorrentApi
{
    public interface IQBittorrentApiService
    {
        string GetApiVersion();
        
        /// <summary>
        /// Get information about all torrents
        /// </summary>
        /// <param name="filter">Optional filter (status)</param>
        /// <param name="category">Optional category filter</param>
        /// <param name="sort">Optional sort field</param>
        /// <param name="reverse">Whether to reverse the sort order</param>
        /// <param name="limit">Maximum number of torrents to return</param>
        /// <param name="offset">Offset in the result list</param>
        /// <param name="hashes">Optional filter by torrent hashes (pipe-separated)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of torrents matching the filters</returns>
        internal Task<IReadOnlyList<TorrentInfoDto>> GetTorrentsAsync(
            string? filter = null,
            string? category = null,
            string? sort = null,
            bool reverse = false,
            int limit = 10000,
            int offset = 0,
            string? hashes = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Get properties of a specific torrent
        /// </summary>
        /// <param name="hash">Torrent hash</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Torrent properties</returns>
        internal Task<TorrentPropertiesDto> GetTorrentPropertiesAsync(string hash, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Get files in a torrent
        /// </summary>
        /// <param name="hash">Torrent hash</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of files in the torrent</returns>
        internal Task<IReadOnlyList<FileInfoDto>> GetTorrentFilesAsync(string hash, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Add a new torrent
        /// </summary>
        /// <param name="torrentFiles">Torrent files to upload</param>
        /// <param name="urls">Magnet URLs (one per line)</param>
        /// <param name="savePath">Path to save the torrent</param>
        /// <param name="cookie">Cookie to use for download</param>
        /// <param name="category">Category to assign</param>
        /// <param name="tags">Tags to assign (comma separated)</param>
        /// <param name="skip_checking">Skip hash checking</param>
        /// <param name="paused">Add in paused state</param>
        /// <param name="stopped">Add in stopped state</param>
        /// <param name="root_folder">Create root folder</param>
        /// <param name="sequentialDownload">Download files sequentially</param>
        /// <param name="firstLastPiecePrio">Prioritize first and last pieces</param>
        /// <param name="contentLayout">Content layout (Original, Subfolder, NoSubfolder)</param>
        /// <param name="ratioLimit">Ratio limit</param>
        /// <param name="seedingTimeLimit">Seeding time limit</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if successful</returns>
        internal Task<bool> AddTorrentAsync(
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
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Delete torrents
        /// </summary>
        /// <param name="hashes">Torrent hashes (pipe-separated)</param>
        /// <param name="deleteFiles">Whether to delete files as well</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if successful</returns>
        internal Task<bool> DeleteTorrentsAsync(string hashes, bool deleteFiles = false, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Get application preferences
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Application preferences</returns>
        internal Task<PreferencesDto> GetPreferencesAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Get torrent trackers
        /// </summary>
        /// <param name="hash">Torrent hash</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of trackers</returns>
        internal Task<IReadOnlyList<TrackerDto>> GetTrackersAsync(string hash, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Get torrent categories
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary of categories</returns>
        internal Task<IReadOnlyDictionary<string, CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Create a new category
        /// </summary>
        /// <param name="category">Category name</param>
        /// <param name="savePath">Save path for the category</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if successful</returns>
        internal Task<bool> CreateCategoryAsync(string category, string? savePath = null, CancellationToken cancellationToken = default);
    }
}

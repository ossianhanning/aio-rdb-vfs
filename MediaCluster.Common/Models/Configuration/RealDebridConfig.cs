namespace MediaCluster.Common.Models.Configuration
{
    /// <summary>
    /// RealDebrid configuration
    /// </summary>
    public class RealDebridConfig
    {
        /// <summary>
        /// RealDebrid API key
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;
        
        /// <summary>
        /// Rate limit for the RealDebrid API (requests per minute)
        /// </summary>
        public int RequestsPerMinute { get; set; } = 200;
        
        /// <summary>
        /// Maximum number of torrents to load initially
        /// </summary>
        public int MaxTorrentsToLoad { get; set; } = 100;
        
        /// <summary>
        /// How long to cache torrent metadata (in minutes)
        /// </summary>
        public int CacheDurationMinutes { get; set; } = 15;
        
        /// <summary>Default folder name to put new downloads in</summary>
        public string DefaultVirtualFolderName { get; set; } = "download";

        /// <summary>Number of items to fetch per page from RealDebrid API</summary>
        public int ItemsPerPage { get; set; } = 2500;
        
        /// <summary>
        /// Whether to use dormant torrents feature to optimize RealDebrid slots
        /// </summary>
        public bool EnableDormantTorrents { get; set; } = true;
        
        /// <summary>
        /// Keep recently accessed torrents active for this many hours
        /// </summary>
        public int KeepActiveDurationHours { get; set; } = 48;
        
        /// <summary>
        /// Max time to wait for torrent to finish on initial add (seconds)
        /// </summary>
        public int InitialTorrentWaitTimeSeconds { get; set; } = 300;
        
        /// <summary>
        /// Polling interval when waiting for torrent to finish on initial add (seconds)
        /// </summary>
        public int InitialTorrentPollingIntervalSeconds { get; set; } = 5;
        
        /// <summary>
        /// Number of dormant torrents to verify per cycle
        /// </summary>
        public int DormantTorrentsVerificationBatchSize { get; set; } = 5;
        
        /// <summary>
        /// Consider download stalled if no progress for this many minutes
        /// </summary>
        public int StallDetectionTimeMinutes { get; set; } = 30;
        
        /// <summary>
        /// Consider download stalled if speed below this value (bytes/sec) for stall detection time
        /// </summary>
        public int StallSpeedThresholdBytesPerSec { get; set; } = 1024; // 1 KB/s
        
        /// <summary>
        /// Directory where problematic torrents are stored
        /// </summary>
        public string ProblematicTorrentsDir { get; set; } = "Problematic";
        
        /// <summary>
        /// File extensions to block (comma-separated)
        /// </summary>
        public string BlockedFileExtensions { get; set; } = "m2ts,rar,zip,iso,exe";
    }
}
namespace MediaCluster.RealDebrid.SharedModels
{
    public class RemoteContainer
    {
        public string HostId { get; set; }
        public string Name { get; set; }
        public string TorrentHash { get; set; }
        public RemoteStatus RemoteStatus { get; set; }
        public DateTime Added { get; set; }
        public List<RemoteFile> Files { get; set; } = new();
        
        // Dormant torrent state tracking
        public TorrentState TorrentState { get; set; } = TorrentState.Active;
        public DateTime? LastVerified { get; set; }
        public DateTime? LastAccessed { get; set; }
        public int VerificationAttempts { get; set; }
        
        // Download performance tracking
        public DateTime? ProgressLastChanged { get; set; }
        public long? LastDownloadSpeed { get; set; }
        public int? Seeders { get; set; }
        public DateTime? StallDetectedAt { get; set; }
        
        // Reason for failure when applicable
        public TorrentProblemReason? ProblemReason { get; set; }
        public string? ProblemDetails { get; set; }
        public List<string>? Tags { get; set; }
        public string? Category { get; set; }
    }
    
    public enum TorrentState
    {
        /// <summary>
        /// Torrent is active on RealDebrid
        /// </summary>
        Active,
        
        /// <summary>
        /// Torrent is verified and removed from RealDebrid to save slots, but can be re-added when needed
        /// </summary>
        Dormant,
        
        /// <summary>
        /// Torrent had issues and is no longer usable
        /// </summary>
        Problematic
    }
    
    public enum TorrentProblemReason
    {
        None,
        BrokenLinks,
        LinkVerificationFailed,
        Stalled,
        NoSeeders,
        RealDebridApiError,
        Manual,
        Other
    }
}
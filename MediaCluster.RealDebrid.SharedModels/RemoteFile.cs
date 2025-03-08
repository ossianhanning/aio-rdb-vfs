using MediaCluster.MediaAnalyzer.Models;
using System.Text.Json.Serialization;

namespace MediaCluster.RealDebrid.SharedModels;

public class RemoteFile
{
    public int FileId { get; set; }
    public required string HostId { get; set; }
    public required long Size { get; set; }
    public required string RestrictedLink { get; set; }
    public DateTime? UnrestrictedDate { get; set; }
    public required string DownloadUrl { get; set; }
    public required string LocalPath { get; set; }
    public bool DeletedLocally { get; set; }
    public MediaInfo? MediaInfo { get; set; }
    [JsonIgnore] public DateTime CreatedDate => this.Parent?.Added ?? DateTime.Now;
    [JsonIgnore] public DateTime ModifiedDate => this.Parent?.Added ?? DateTime.Now;
    [JsonIgnore] public DateTime AccessedDate => this.Parent?.Added ?? DateTime.Now;
    [JsonIgnore] public RemoteContainer? Parent { get; set; }
    public string? OsdbHash { get; set; }
}
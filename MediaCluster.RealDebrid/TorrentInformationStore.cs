using System.Collections.Concurrent;
using System.Text.Json;
using MediaCluster.Common.Models.Configuration;
using MediaCluster.RealDebrid.Models;
using MediaCluster.RealDebrid.Models.External;
using MediaCluster.RealDebrid.SharedModels;
using Microsoft.Extensions.Options;
using MediaCluster.CacheSystem;
using MediaCluster.Common;

namespace MediaCluster.RealDebrid;

public partial class TorrentInformationStore : IDisposable, ITorrentInformationStore
{
    private readonly ILogger<TorrentInformationStore> _logger;
    private readonly string _storePath;
    private readonly string _deletedPath;
    private readonly string _problematicPath;
    private readonly RealDebridConfig _config;
    private readonly FileSystemWatcher _fileWatcher;
    private readonly ConcurrentDictionary<string, RemoteContainer> _torrents = new();
    private readonly SemaphoreSlim _fileOperationLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private bool _initialized;
    private VirtualFileSystem _fileSystem;
    public TaskCompletionSource ReadySource { get; } = new();

    public TorrentInformationStore(IOptions<AppConfig> options, ILogger<TorrentInformationStore> logger, VirtualFileSystem fileSystem)
    {
        _logger = logger;
        _config = options.Value.RealDebrid;
        _fileSystem = fileSystem;
        
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MediaCluster",
            "Torrents");
        
        _storePath = Path.Combine(appDataPath, "Active");
        _deletedPath = Path.Combine(appDataPath, "Deleted");
        
        Directory.CreateDirectory(_storePath);
        Directory.CreateDirectory(_deletedPath);
        
        _fileWatcher = new FileSystemWatcher(_storePath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            Filter = "*.trd",
            EnableRaisingEvents = false,
            IncludeSubdirectories = false
        };
        
        _problematicPath = Path.Combine(appDataPath, _config.ProblematicTorrentsDir);
        Directory.CreateDirectory(_problematicPath);
        
        _fileWatcher.Deleted += OnLocalTorrentFileDeleted;
        
        _fileSystem.FileDeleted += OnVirtualFileDeleted;
        _fileSystem.FileMoved += OnVirtualFileMoved;
        _fileSystem.FolderMoved += OnVirtualFolderMoved;
    }
    
    private async void OnVirtualFileDeleted(object? sender, VirtualFileSystemEventArgs e)
    {
        await _fileOperationLock.WaitAsync();
        try
        {
            // Find the container containing this file and save it
            var affectedContainer = FindContainerForPath(e.Path);
            if (affectedContainer != null)
            {
                _logger.LogInformation("Saving changes for deleted file {Path}", e.Path);
                await SaveTorrentToDiskAsync(affectedContainer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving changes for deleted file {Path}", e.Path);
        }
        finally
        {
            _fileOperationLock.Release();
        }
    }

    private async void OnVirtualFileMoved(object? sender, VirtualFileSystemMoveEventArgs e)
    {
        await _fileOperationLock.WaitAsync();
        try
        {
            // Find the container containing this file and save it
            var affectedContainer = FindContainerForPath(e.NewPath);
            if (affectedContainer != null)
            {
                _logger.LogInformation("Saving changes for moved file {OldPath} -> {NewPath}", e.OldPath, e.NewPath);
                await SaveTorrentToDiskAsync(affectedContainer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving changes for moved file {OldPath} -> {NewPath}", e.OldPath, e.NewPath);
        }
        finally
        {
            _fileOperationLock.Release();
        }
    }

    private async void OnVirtualFolderMoved(object? sender, VirtualFileSystemMoveEventArgs e)
    {
        await _fileOperationLock.WaitAsync();
        try
        {
            // This might affect multiple containers, so find and save all affected ones
            var affectedContainers = FindContainersForFolderMove(e.OldPath, e.NewPath);
            foreach (var container in affectedContainers)
            {
                _logger.LogInformation("Saving changes for container {ContainerId} affected by folder move", container.HostId);
                await SaveTorrentToDiskAsync(container);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving changes for moved folder {OldPath} -> {NewPath}", e.OldPath, e.NewPath);
        }
        finally
        {
            _fileOperationLock.Release();
        }
    }

    private RemoteContainer? FindContainerForPath(string path)
    {
        // Normalize path for comparison
        path = path.Replace('\\', '/').TrimStart('/');
        
        foreach (var container in _torrents.Values)
        {
            foreach (var file in container.Files)
            {
                if (string.Equals(file.LocalPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return container;
                }
            }
        }
        
        return null;
    }

    private IEnumerable<RemoteContainer> FindContainersForFolderMove(string oldFolderPath, string newFolderPath)
    {
        // Normalize paths for comparison
        oldFolderPath = oldFolderPath.Replace('\\', '/').TrimStart('/');
        if (!oldFolderPath.EndsWith('/')) oldFolderPath += '/';
        
        newFolderPath = newFolderPath.Replace('\\', '/').TrimStart('/');
        if (!newFolderPath.EndsWith('/')) newFolderPath += '/';

        var affectedContainers = new HashSet<RemoteContainer>();
        
        foreach (var container in _torrents.Values)
        {
            bool containerAffected = false;
            
            foreach (var file in container.Files)
            {
                // Check if this file was under the moved folder
                if (file.LocalPath.StartsWith(oldFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    containerAffected = true;
                    break;
                }
            }
            
            if (containerAffected)
            {
                affectedContainers.Add(container);
            }
        }
        
        return affectedContainers;
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;
        
        await LoadTorrentsFromDiskAsync();
        _fileWatcher.EnableRaisingEvents = true;
        _initialized = true;
        
        _logger.LogInformation("TorrentInformationStore initialized with {Count} torrents", _torrents.Count);
    }

    private async Task LoadTorrentsFromDiskAsync()
    {
        await _fileOperationLock.WaitAsync();
        
        try
        {
            // Load active torrents
            foreach (var file in Directory.GetFiles(_storePath, "*.trd"))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    var torrent = JsonSerializer.Deserialize<RemoteContainer>(content);

                    if (torrent == null)
                    {
                        throw new InvalidDataException("Invalid torrent");
                    }

                    foreach (var remoteFile in torrent.Files.Where(remoteFile => remoteFile.Parent == null))
                    {
                        remoteFile.Parent = torrent;
                    }
                    
                    _torrents[torrent.HostId] = torrent;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load torrent from file {File}", file);
                }
            }
            
            // Load deleted torrents (to prevent re-adding them)
            foreach (var file in Directory.GetFiles(_deletedPath, "*.trd"))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    var torrent = JsonSerializer.Deserialize<RemoteContainer>(content);
                    
                    if (torrent != null)
                    {
                        torrent.Files.ForEach(f => f.DeletedLocally = true);
                        _torrents[torrent.HostId] = torrent;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load deleted torrent from file {File}", file);
                }
            }
            
            BuildFileSystem();
        }
        finally
        {
            _fileOperationLock.Release();
        }
    }

    private void BuildFileSystem()
    {
        foreach (var torrent in _torrents.Values)
            foreach (var file in torrent.Files.Where(f => !f.DeletedLocally))
            {
                _fileSystem.AddFile(file.LocalPath, file);
            }
        
        ReadySource.SetResult();
    }

    private async void OnLocalTorrentFileDeleted(object sender, FileSystemEventArgs e)
    {
        var torrentId = Path.GetFileNameWithoutExtension(e.Name);
        if (string.IsNullOrEmpty(torrentId))
            return;
        
        await _fileOperationLock.WaitAsync();
        
        try
        {
            if (_torrents.TryGetValue(torrentId, out var torrent))
            {
                _logger.LogInformation("Torrent file {TorrentId} was deleted, marking as deleted", torrentId);
                
                // Mark all files as deleted
                torrent.Files.ForEach(f =>
                {
                    _fileSystem.DeleteFile(f.LocalPath);
                    f.DeletedLocally = true;
                });
                
                // Move to deleted folder
                await SaveTorrentToDiskAsync(torrent, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling deleted torrent file {File}", e.Name);
        }
        finally
        {
            _fileOperationLock.Release();
        }
    }

    internal async Task<bool> AddRemoteContainerAsync(RemoteContainer remoteTorrent)
    {
        return _torrents.TryAdd(remoteTorrent.HostId, remoteTorrent);
    }

    internal async Task UpdateAllRemoteContainersAsync(List<TorrentItemDto> remoteTorrents)
    {
        await _fileOperationLock.WaitAsync();
        
        try
        {
            var newTorrents = new ConcurrentDictionary<string, RemoteContainer>();
            
            foreach (var remoteTorrent in remoteTorrents)
            {
                if (_torrents.TryGetValue(remoteTorrent.Id, out var existingTorrent))
                {
                    if (existingTorrent.RemoteStatus != remoteTorrent.Status.ToRemoteStatus())
                    {
                        existingTorrent.RemoteStatus = remoteTorrent.Status.ToRemoteStatus();
                        await SaveTorrentToDiskAsync(existingTorrent);
                    }
                    
                    newTorrents[remoteTorrent.Id] = existingTorrent;
                }
                else
                {
                    var newTorrent = new RemoteContainer
                    {
                        HostId = remoteTorrent.Id,
                        Name = remoteTorrent.Filename,
                        TorrentHash = remoteTorrent.Hash,
                        RemoteStatus = remoteTorrent.Status.ToRemoteStatus(),
                        Added = DateTime.Parse(remoteTorrent.Added),
                        Files = new List<RemoteFile>()
                    };
                    
                    newTorrents[remoteTorrent.Id] = newTorrent;
                    await SaveTorrentToDiskAsync(newTorrent);
                }
            }
            
            var deletedTorrents = _torrents.Values
                .Where(t => !newTorrents.ContainsKey(t.HostId) && !t.Files.All(f => f.DeletedLocally))
                .ToList();
            
            foreach (var deletedTorrent in deletedTorrents)
            {
                deletedTorrent.Files.ForEach(f =>
                {
                    _fileSystem.DeleteFile(f.LocalPath);
                    f.DeletedLocally = true;
                });
                
                // TODO: Handle deleting torrent from deleted folder (or moving it out) if it gets re-added
                await SaveTorrentToDiskAsync(deletedTorrent, true);
            }
            
            _torrents.Clear();
            foreach (var (id, torrent) in newTorrents)
            {
                _torrents[id] = torrent;
            }
        }
        finally
        {
            _fileOperationLock.Release();
        }
    }

    internal async Task UpdateTorrentFilesAsync(string torrentId,
        TorrentInfoDto torrentInfo,
        List<UnrestrictLinkResponseDto> unrestrictedLinks,
        List<string> brokenLinks)
    {
        var torrentInfoFiles = torrentInfo.Files.Where(f => f.Selected == 1).ToList();
        
        if ((unrestrictedLinks.Count + brokenLinks.Count) != torrentInfoFiles.Count)
            throw new InvalidOperationException($"Mismatch between selected files ({torrentInfoFiles.Count}) and unrestricted links ({unrestrictedLinks.Count})");
        
        await _fileOperationLock.WaitAsync();
        
        try
        {
            if (!_torrents.TryGetValue(torrentId, out var torrent))
            {
                _logger.LogWarning("Attempted to update files for unknown torrent {TorrentId}", torrentId);
                return;
            }

            if (brokenLinks.Count > 0)
            {
                var unrestrictedFiles = torrentInfo.Links.Select((l, index) => new { Link = l, Index = index })
                    .Where(w => !brokenLinks.Contains(w.Link)).ToList();
            
                torrentInfoFiles = torrentInfoFiles.Select((f, index) => new { File = f, Index = index })
                    .Where(w => unrestrictedFiles.Any(a => a.Index == w.Index)).Select(s => s.File).ToList();
            }
            
            var newFiles = new List<RemoteFile>();
            
            for (var i = 0; i < unrestrictedLinks.Count; i++)
            {
                var link = unrestrictedLinks[i];
                var file = torrentInfoFiles[i];
                
                var fileName = Path.GetFileName(file.Path);

                newFiles.Add(new RemoteFile
                {
                    FileId = file.Id,
                    HostId = link.Id,
                    Size = link.Filesize,
                    RestrictedLink = link.Link,
                    UnrestrictedDate = DateTime.UtcNow,
                    DownloadUrl = link.Download,
                    LocalPath = $"{Path.TrimEndingDirectorySeparator(_config.DefaultVirtualFolderName)}\\{torrentInfo.OriginalFilename}\\{fileName}",
                    DeletedLocally = false,
                    MediaInfo = null,
                    Parent = torrent
                });
            }
            
            torrent.Files = newFiles;
            torrent.RemoteStatus = RemoteStatus.Downloaded;

            torrent.Files.ForEach(f =>
            {
                _fileSystem.AddFile(f);
            });
            
            await SaveTorrentToDiskAsync(torrent);
        }
        finally
        {
            _fileOperationLock.Release();
        }
    }

    public async Task SaveTorrentToDiskAsync(RemoteContainer torrent, bool deleted = false)
    {
        string path;
    
        if (deleted)
        {
            path = _deletedPath;
        }
        else if (torrent.TorrentState == TorrentState.Problematic)
        {
            path = _problematicPath;
        }
        else
        {
            path = _storePath;
        }
    
        var filePath = Path.Combine(path, $"{torrent.HostId}.trd");
    
        var json = JsonSerializer.Serialize(torrent, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Move a torrent to the problematic directory
    /// </summary>
    public async Task MoveToProblematicAsync(RemoteContainer torrent)
    {
        string sourcePath = Path.Combine(_storePath, $"{torrent.HostId}.trd");
        string destinationPath = Path.Combine(_problematicPath, $"{torrent.HostId}.trd");

        // If the file exists in the active directory, move it
        if (File.Exists(sourcePath))
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(sourcePath, destinationPath);

            _logger.LogInformation("Moved torrent {TorrentId} to problematic directory", torrent.HostId);
        }
        else
        {
            // Just save to the problematic directory
            await SaveTorrentToDiskAsync(torrent);

            _logger.LogInformation("Saved torrent {TorrentId} to problematic directory", torrent.HostId);
        }
    }

    public IEnumerable<RemoteContainer> GetAllTorrents() => _torrents.Values;
    
    public RemoteContainer? GetTorrentById(string id) => 
        _torrents.GetValueOrDefault(id);
    
    public IEnumerable<RemoteContainer> GetTorrentsByHash(string hash) => 
        _torrents.Values.Where(t => t.TorrentHash.Equals(hash, StringComparison.OrdinalIgnoreCase));
    
    public async Task PurgeTorrents(List<string> torrentIds)
    {
        await _fileOperationLock.WaitAsync();
        
        try
        {
            foreach (var id in torrentIds)
            {
                if (_torrents.TryRemove(id, out var torrent))
                {
                    var filePath = Path.Combine(_storePath, $"{id}.trd");
                    var deletedPath = Path.Combine(_deletedPath, $"{id}.trd");
                    
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                    
                    if (File.Exists(deletedPath))
                        File.Delete(deletedPath);
                }
            }
        }
        finally
        {
            _fileOperationLock.Release();
        }
    }
    
    public void Dispose()
    {
        _fileOperationLock.Dispose();
        _fileWatcher.Dispose();
    }
}

public abstract class VirtualNode(string name, IVirtualFolder? parent) : IVirtualNode
{
    public string Name { get; internal set; } = name;
    public IVirtualFolder? Parent { get; internal set; } = parent;

    public string GetFullPath()
    {
        if (Parent == null)
            return "/";
        
        var parentPath = Parent.GetFullPath();
        return parentPath == "/" ? $"/{Name}" : $"{parentPath}/{Name}";
    }
}

/// <summary>
/// Represents a virtual file in the file system
/// </summary>
public class VirtualFile(string name, IVirtualFolder parent, RemoteFile remoteFile, ICacheProvider cacheProvider)
    : VirtualNode(name, parent), IVirtualFile
{
    public RemoteFile RemoteFile { get; } = remoteFile;

    /// <summary>
    /// Read file content from the given offset and length
    /// </summary>
    public async Task<byte[]> ReadFileContentAsync(long offset, int length)
    {
        return await cacheProvider.ReadAsync(RemoteFile, offset, length);
    }
}

/// <summary>
/// Represents a virtual folder in the file system
/// </summary>
public class VirtualFolder : VirtualNode, IVirtualFolder
{
    public List<IVirtualFolder> Subfolders { get; } = new();
    public List<IVirtualFile> Files { get; } = new();

    public VirtualFolder(string name, IVirtualFolder? parent)
        : base(name, parent)
    {
    }
}
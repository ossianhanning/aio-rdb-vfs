using System.Collections.Concurrent;

namespace MediaCluster.MergedFileSystem;

/// <summary>
/// Caches file and directory nodes to improve performance
/// </summary>
internal class NodeCache
{
    /// <summary>
    /// Cache of file and directory nodes by path
    /// </summary>
    private readonly ConcurrentDictionary<string, FileNode> _nodeCache = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Cache of directory contents by path
    /// </summary>
    private readonly ConcurrentDictionary<string, List<DirEntry>> _dirCache = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Gets a node from the cache
    /// </summary>
    public FileNode? GetNode(string path)
    {
        if (_nodeCache.TryGetValue(path, out var node))
        {
            return node;
        }
        
        return null;
    }
    
    /// <summary>
    /// Adds a node to the cache
    /// </summary>
    public void AddNode(string path, FileNode node)
    {
        _nodeCache[path] = node;
    }
    
    /// <summary>
    /// Removes a node from the cache
    /// </summary>
    public void RemoveNode(string path)
    {
        _nodeCache.TryRemove(path, out _);
    }
    
    /// <summary>
    /// Invalidates a directory in the cache
    /// </summary>
    public void InvalidateDirectory(string path)
    {
        _dirCache.TryRemove(path, out _);
    }
    
    /// <summary>
    /// Invalidates a directory and all its subdirectories in the cache
    /// </summary>
    public void InvalidateDirectoryAndSubdirectories(string directoryPath)
    {
        // First remove the directory itself
        InvalidateDirectory(directoryPath);
        
        // Remove all subdirectories
        var prefix = directoryPath.EndsWith("\\") ? directoryPath : directoryPath + "\\";
        
        // Remove all nodes that start with this prefix
        var keysToRemove = new List<string>();
        foreach (var key in _nodeCache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                keysToRemove.Add(key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            _nodeCache.TryRemove(key, out _);
        }
        
        // Remove all directory entries that start with this prefix
        keysToRemove.Clear();
        foreach (var key in _dirCache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                keysToRemove.Add(key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            _dirCache.TryRemove(key, out _);
        }
    }
    
    /// <summary>
    /// Gets directory entries from the cache
    /// </summary>
    public List<DirEntry>? GetDirectoryEntries(string path)
    {
        if (_dirCache.TryGetValue(path, out var entries))
        {
            return entries;
        }
        
        return null;
    }
    
    /// <summary>
    /// Adds directory entries to the cache
    /// </summary>
    public void AddDirectoryEntries(string path, List<DirEntry> entries)
    {
        _dirCache[path] = entries;
    }
}
using MediaCluster.Common;
using FileInfo = Fsp.Interop.FileInfo;

namespace MediaCluster.MergedFileSystem;

/// <summary>
/// Represents a file or directory node in the merged file system
/// </summary>
internal class FileNode
{
    /// <summary>
    /// The file system path of this node
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Is this node a directory?
    /// </summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// Is this node from the local file system?
    /// </summary>
    public bool IsLocal { get; set; }

    /// <summary>
    /// Creation time of the file/directory
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// Last access time of the file/directory
    /// </summary>
    public DateTime LastAccessTime { get; set; }

    /// <summary>
    /// Last write time of the file/directory
    /// </summary>
    public DateTime LastWriteTime { get; set; }

    /// <summary>
    /// File size in bytes (0 for directories)
    /// <summary>
    /// File size in bytes (0 for directories)
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Reference to the virtual file (if this is a virtual file)
    /// </summary>
    public IVirtualFile? VirtualFile { get; set; }

    /// <summary>
    /// Reference to the virtual folder (if this is a virtual folder)
    /// </summary>
    public IVirtualFolder? VirtualFolder { get; set; }

    /// <summary>
    /// Creates a new file node with the given path
    /// </summary>
    public FileNode(string path)
    {
        Path = path;
        CreationTime = DateTime.Now;
        LastAccessTime = DateTime.Now;
        LastWriteTime = DateTime.Now;
    }

    /// <summary>
    /// Gets file info for this node
    /// </summary>
    public void GetFileInfo(out FileInfo fileInfo)
    {
        fileInfo = default;
        
        // Set attributes
        fileInfo.FileAttributes = IsDirectory ? 
            (uint)System.IO.FileAttributes.Directory : 
            (uint)System.IO.FileAttributes.Normal;
        
        fileInfo.ReparseTag = 0;
        fileInfo.FileSize = (ulong)FileSize;
        
        // Calculate allocation size (round up to next ALLOCATION_UNIT)
        const int ALLOCATION_UNIT = 4096;
        fileInfo.AllocationSize = (fileInfo.FileSize + (ulong)ALLOCATION_UNIT - 1) / 
            (ulong)ALLOCATION_UNIT * (ulong)ALLOCATION_UNIT;
        
        // Set timestamps
        fileInfo.CreationTime = (ulong)CreationTime.ToFileTimeUtc();
        fileInfo.LastAccessTime = (ulong)LastAccessTime.ToFileTimeUtc();
        fileInfo.LastWriteTime = (ulong)LastWriteTime.ToFileTimeUtc();
        fileInfo.ChangeTime = fileInfo.LastWriteTime;
        
        // These are zero for now
        fileInfo.IndexNumber = 0;
        fileInfo.HardLinks = 0;
    }
}

/// <summary>
/// Represents a directory entry for ReadDirectoryEntry
/// </summary>
internal class DirEntry
{
    /// <summary>
    /// The name of the file or directory (just the name, not the full path)
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// The node associated with this directory entry
    /// </summary>
    public FileNode Node { get; set; } = null!;
}
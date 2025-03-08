using FileInfo = Fsp.Interop.FileInfo;

namespace MediaCluster.MergedFileSystem;

/// <summary>
/// Represents a file descriptor for a file or directory in the merged file system
/// </summary>
internal class FileDescriptor : IDisposable
{
    /// <summary>
    /// The node associated with this file descriptor
    /// </summary>
    public FileNode Node { get; }
    
    /// <summary>
    /// The local path for the file/directory (empty for virtual files/directories)
    /// </summary>
    public string LocalPath { get; }
    
    /// <summary>
    /// The file stream for local files (lazy initialized)
    /// </summary>
    private FileStream? _fileStream;
    
    /// <summary>
    /// Directory entries for ReadDirectoryEntry
    /// </summary>
    public List<DirEntry>? DirEntries { get; set; }
    
    /// <summary>
    /// Creates a new file descriptor for a file node
    /// </summary>
    public FileDescriptor(FileNode node, string localPath)
    {
        Node = node;
        LocalPath = localPath;
    }
    
    /// <summary>
    /// Gets the file info for this file descriptor
    /// </summary>
    public int GetFileInfo(out FileInfo fileInfo)
    {
        Node.GetFileInfo(out fileInfo);
        return 0; // STATUS_SUCCESS
    }
    
    /// <summary>
    /// Closes any open streams
    /// </summary>
    public void CloseStreams()
    {
        if (_fileStream != null)
        {
            _fileStream.Dispose();
            _fileStream = null;
        }
    }
    
    /// <summary>
    /// Disposes any resources held by this file descriptor
    /// </summary>
    public void Dispose()
    {
        CloseStreams();
        GC.SuppressFinalize(this);
    }
}
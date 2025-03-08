using MediaCluster.RealDebrid.SharedModels;

namespace MediaCluster.Common;

public interface IVirtualFileSystem
{
    public IVirtualFolder Root { get; }
    event EventHandler<VirtualFileSystemEventArgs>? FileAdded;
    event EventHandler<VirtualFileSystemEventArgs>? FileDeleted;
    event EventHandler<VirtualFileSystemMoveEventArgs>? FileMoved;
    event EventHandler<VirtualFileSystemEventArgs>? FolderAdded;
    event EventHandler<VirtualFileSystemEventArgs>? FolderDeleted;
    event EventHandler<VirtualFileSystemMoveEventArgs>? FolderMoved;
    Task<byte[]> ReadFileContentAsync(string path, long offset, int length);
    void MoveFile(string sourcePath, string targetPath);
    void MoveFolder(string sourcePath, string targetPath);
    void DeleteFile(string path);
    void DeleteFolder(string path);
    bool FileExists(string path);
    bool FolderExists(string path);
    IVirtualNode? FindNode(string path);
}

public class VirtualFileSystemEventArgs(string path) : EventArgs
{
    public string Path { get; } = path;
}

public class VirtualFileSystemMoveEventArgs(string oldPath, string newPath) : EventArgs
{
    public string OldPath { get; } = oldPath;
    public string NewPath { get; } = newPath;
}
public interface IVirtualNode
{
    string Name { get; }
    IVirtualFolder? Parent { get; }
    string GetFullPath();
}

public interface IVirtualFile : IVirtualNode
{
    RemoteFile RemoteFile { get; }
    Task<byte[]> ReadFileContentAsync(long offset, int length);
}

public interface IVirtualFolder : IVirtualNode
{
    List<IVirtualFolder> Subfolders { get; }
    List<IVirtualFile> Files { get; }
}

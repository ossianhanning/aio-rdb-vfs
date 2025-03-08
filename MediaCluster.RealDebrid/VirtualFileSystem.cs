using System.Text.RegularExpressions;
using MediaCluster.CacheSystem;
using MediaCluster.Common;
using MediaCluster.RealDebrid.SharedModels;

namespace MediaCluster.RealDebrid;

public sealed class VirtualFileSystem(ICacheProvider cacheProvider) : IVirtualFileSystem
{
    public IVirtualFolder Root { get; } = new VirtualFolder("/", null);
    
    // Events for file system operations
    public event EventHandler<VirtualFileSystemEventArgs>? FileAdded;
    public event EventHandler<VirtualFileSystemEventArgs>? FileDeleted;
    public event EventHandler<VirtualFileSystemMoveEventArgs>? FileMoved;
    public event EventHandler<VirtualFileSystemEventArgs>? FolderAdded;
    public event EventHandler<VirtualFileSystemEventArgs>? FolderDeleted;
    public event EventHandler<VirtualFileSystemMoveEventArgs>? FolderMoved;

    // Path validation constants
    private static readonly int MaxPathLength = 260;
    private static readonly Regex InvalidCharsRegex = new Regex(@"[<>:""/\\|?*]");
    private static readonly string[] ReservedNames = { "CON", "PRN", "AUX", "NUL", 
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", 
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
    
    // Cache provider for file content

    public VirtualFile AddFile(string path, RemoteFile remoteFile)
    {
        remoteFile.LocalPath = path;
        var directoryPath = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "/";
        var fileName = Path.GetFileName(path);
        
        // Ensure directory exists
        var folder = EnsureDirectoryExists(directoryPath);
        
        // Sanitize filename
        var sanitizedName = SanitizeName(fileName);
        
        // Check for duplicates and make unique if necessary
        sanitizedName = GetUniqueFileName(folder, sanitizedName);
        
        // Create and add the file
        var file = new VirtualFile(sanitizedName, folder, remoteFile, cacheProvider);
        folder.Files.Add(file);
        UpdateRemoteFilePath(file);
        OnFileAdded(file.GetFullPath());
        
        return file;
    }

    public VirtualFile AddFile(RemoteFile remoteFile)
    {
        if (string.IsNullOrEmpty(remoteFile.LocalPath))
        {
            throw new ArgumentException("RemoteFile.LocalPath cannot be null or empty.");
        }
        
        return AddFile(remoteFile.LocalPath, remoteFile);
    }

    public async Task<byte[]> ReadFileContentAsync(string path, long offset, int length)
    {
        var node = FindNode(path);
        if (node is not VirtualFile file)
        {
            throw new FileNotFoundException($"File not found: {path}");
        }
        
        return await cacheProvider.ReadAsync(file.RemoteFile, offset, length);
    }

    public void MoveFile(string sourcePath, string targetPath)
    {
        var sourceNode = FindNode(sourcePath);
        if (sourceNode is not VirtualFile file)
        {
            throw new FileNotFoundException($"File not found: {sourcePath}");
        }
        
        var targetDirPath = Path.GetDirectoryName(targetPath)?.Replace('\\', '/') ?? "/";
        var targetFileName = Path.GetFileName(targetPath);
        
        // Ensure target directory exists
        var targetFolder = EnsureDirectoryExists(targetDirPath);
        
        // Sanitize target filename
        var sanitizedName = SanitizeName(targetFileName);
        
        // Check for duplicates and make unique if necessary
        sanitizedName = GetUniqueFileName(targetFolder, sanitizedName);
        
        var oldPath = file.GetFullPath();
        
        // Move the file
        if (file.Parent != null)
        {
            file.Parent.Files.Remove(file);
        }
        
        file.Name = sanitizedName;
        file.Parent = targetFolder;
        targetFolder.Files.Add(file);
        
        // Update RemoteFile.LocalPath
        UpdateRemoteFilePath(file);
        
        OnFileMoved(oldPath, file.GetFullPath());
    }
    
    public void MoveFolder(string sourcePath, string targetPath)
    {
        var sourceNode = FindNode(sourcePath);
        if (sourceNode is not VirtualFolder folder)
        {
            throw new DirectoryNotFoundException($"Directory not found: {sourcePath}");
        }
        
        // Cannot move root
        if (folder == Root)
        {
            throw new InvalidOperationException("Cannot move the root folder.");
        }
        
        // Check if target parent exists
        var targetParentPath = Path.GetDirectoryName(targetPath)?.Replace('\\', '/') ?? "/";
        var targetFolderName = Path.GetFileName(targetPath);
        
        var targetParentFolder = FindNode(targetParentPath) as IVirtualFolder;
        if (targetParentFolder == null)
        {
            targetParentFolder = EnsureDirectoryExists(targetParentPath);
        }
        
        // Sanitize target folder name
        var sanitizedName = SanitizeName(targetFolderName);
        
        // Check for duplicates and make unique if necessary
        sanitizedName = GetUniqueFolderName(targetParentFolder, sanitizedName);
        
        var oldPath = folder.GetFullPath();
        
        // Move the folder
        if (folder.Parent != null)
        {
            folder.Parent.Subfolders.Remove(folder);
        }
        
        folder.Name = sanitizedName;
        folder.Parent = targetParentFolder;
        targetParentFolder.Subfolders.Add(folder);
        
        // Update all RemoteFile.LocalPath values for files in this folder and subfolders
        UpdateRemoteFilePathsRecursively(folder);
        
        OnFolderMoved(oldPath, folder.GetFullPath());
    }
    
    public void DeleteFile(string path)
    {
        var node = FindNode(path);
        if (node is not VirtualFile file)
        {
            throw new FileNotFoundException($"File not found: {path}");
        }
        
        // Mark as deleted
        file.RemoteFile.DeletedLocally = true;
        
        // Remove from parent
        if (file.Parent != null)
        {
            file.Parent.Files.Remove(file);
        }
        
        OnFileDeleted(path);
    }
    
    public void DeleteFolder(string path)
    {
        var node = FindNode(path);
        if (node is not VirtualFolder folder)
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }
        
        // Cannot delete root
        if (folder == Root)
        {
            throw new InvalidOperationException("Cannot delete the root folder.");
        }
        
        // Mark all files as deleted recursively
        MarkFilesAsDeletedRecursively(folder);
        
        // Remove from parent
        if (folder.Parent != null)
        {
            folder.Parent.Subfolders.Remove(folder);
        }
        
        OnFolderDeleted(path);
    }

    public IVirtualNode? FindNode(string path)
    {
        path = path?.Replace('\\', '/') ?? "/";
        
        if (string.IsNullOrEmpty(path) || path == "/")
            return Root;

        path = path.TrimStart('\\');
        
        var parts = path.Trim('/').Split('/');
        var current = (IVirtualNode)Root;
        
        foreach (var part in parts)
        {
            if (current is IVirtualFolder folder)
            {
                var subfolder = folder.Subfolders.FirstOrDefault(f => f.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (subfolder != null)
                {
                    current = subfolder;
                    continue;
                }
                
                var file = folder.Files.FirstOrDefault(f => f.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (file != null)
                {
                    current = file;
                    continue;
                }
            }
            
            return null; 
        }
        
        return current;
    }
    
    public bool FileExists(string path)
    {
        return FindNode(path) is VirtualFile;
    }
    
    public bool FolderExists(string path)
    {
        return FindNode(path) is VirtualFolder;
    }
    
    private string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "Unnamed";
        }
        
        // Remove invalid characters
        var sanitized = InvalidCharsRegex.Replace(name, "_");
        
        // Check reserved names
        var nameWithoutExt = Path.GetFileNameWithoutExtension(sanitized).ToUpperInvariant();
        if (ReservedNames.Contains(nameWithoutExt))
        {
            var extension = Path.GetExtension(sanitized);
            sanitized = $"{nameWithoutExt}_File{extension}";
        }
        
        // Trim trailing spaces and periods
        sanitized = sanitized.TrimEnd(' ', '.');
        
        // Ensure name isn't too long (leave room for path)
        if (sanitized.Length > 255)
        {
            var extension = Path.GetExtension(sanitized);
            sanitized = sanitized.Substring(0, 255 - extension.Length) + extension;
        }
        
        return sanitized.Length > 0 ? sanitized : "Unnamed";
    }
    
    private string GetUniqueFileName(IVirtualFolder folder, string fileName)
    {
        if (!folder.Files.Any(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
        {
            return fileName;
        }
        
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        
        int counter = 1;
        string newName;
        
        do
        {
            newName = $"{nameWithoutExt} ({counter}){extension}";
            counter++;
        } while (folder.Files.Any(f => f.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)));
        
        return newName;
    }
    
    private string GetUniqueFolderName(IVirtualFolder parentFolder, string folderName)
    {
        if (!parentFolder.Subfolders.Any(f => f.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
        {
            return folderName;
        }
        
        int counter = 1;
        string newName;
        
        do
        {
            newName = $"{folderName} ({counter})";
            counter++;
        } while (parentFolder.Subfolders.Any(f => f.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)));
        
        return newName;
    }
    
    private IVirtualFolder EnsureDirectoryExists(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return Root;
        
        var parts = path.Trim('/').Split('/');
        var current = Root;
        
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
                continue;
                
            var sanitizedName = SanitizeName(part);
            var subfolder = current.Subfolders.FirstOrDefault(f => f.Name.Equals(sanitizedName, StringComparison.OrdinalIgnoreCase));
            
            if (subfolder == null)
            {
                subfolder = new VirtualFolder(sanitizedName, current);
                current.Subfolders.Add(subfolder);
                OnFolderAdded(subfolder.GetFullPath());
            }
            
            current = subfolder;
        }
        
        return current;
    }
    
    private void UpdateRemoteFilePath(IVirtualFile file)
    {
        // Update the RemoteFile.LocalPath to reflect the current full path
        file.RemoteFile.LocalPath = file.GetFullPath().TrimStart('/');
    }
    
    private void UpdateRemoteFilePathsRecursively(IVirtualFolder folder)
    {
        // Update all files in this folder
        foreach (var file in folder.Files)
        {
            UpdateRemoteFilePath(file);
        }
        
        // Recursively update all files in subfolders
        foreach (var subfolder in folder.Subfolders)
        {
            UpdateRemoteFilePathsRecursively(subfolder);
        }
    }
    
    private void MarkFilesAsDeletedRecursively(IVirtualFolder folder)
    {
        // Mark all files in this folder as deleted
        foreach (var file in folder.Files)
        {
            file.RemoteFile.DeletedLocally = true;
        }
        
        // Recursively mark all files in subfolders as deleted
        foreach (var subfolder in folder.Subfolders)
        {
            MarkFilesAsDeletedRecursively(subfolder);
        }
    }

    private void OnFileAdded(string path) => FileAdded?.Invoke(this, new VirtualFileSystemEventArgs(path));
    private void OnFileDeleted(string path) => FileDeleted?.Invoke(this, new VirtualFileSystemEventArgs(path));
    private void OnFileMoved(string oldPath, string newPath) => FileMoved?.Invoke(this, new VirtualFileSystemMoveEventArgs(oldPath, newPath));
    private void OnFolderAdded(string path) => FolderAdded?.Invoke(this, new VirtualFileSystemEventArgs(path));
    private void OnFolderDeleted(string path) => FolderDeleted?.Invoke(this, new VirtualFileSystemEventArgs(path));
    private void OnFolderMoved(string oldPath, string newPath) => FolderMoved?.Invoke(this, new VirtualFileSystemMoveEventArgs(oldPath, newPath));
}
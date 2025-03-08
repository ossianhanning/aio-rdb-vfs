using Fsp;
using MediaCluster.Common;
using MediaCluster.Common.Models.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using VolumeInfo = Fsp.Interop.VolumeInfo;
using FileInfo = Fsp.Interop.FileInfo;

namespace MediaCluster.MergedFileSystem;

internal class MergedFileSystem : FileSystemBase
{
    private const int ALLOCATION_UNIT = 4096;
    private const uint FILE_DIRECTORY_FILE = 0x00000001;
    private const uint FILE_NON_DIRECTORY_FILE = 0x00000040;

    private readonly ILogger _logger;
    private readonly FileSystemConfig _config;
    private readonly IVirtualFileSystem _virtualFileSystem;
    private readonly string _localPath;
    private readonly NodeCache _nodeCache;

    public MergedFileSystem(
        ILogger logger,
        FileSystemConfig config,
        IVirtualFileSystem virtualFileSystem)
    {
        _logger = logger;
        _config = config;
        _virtualFileSystem = virtualFileSystem;
        _localPath = Path.GetFullPath(config.FileSystemLocalPath).TrimEnd(Path.DirectorySeparatorChar);
        _nodeCache = new NodeCache();

        // Register virtual file system events
        _virtualFileSystem.FileAdded += VirtualFileSystem_FileAdded;
        _virtualFileSystem.FileDeleted += VirtualFileSystem_FileDeleted;
        _virtualFileSystem.FileMoved += VirtualFileSystem_FileMoved;
        _virtualFileSystem.FolderAdded += VirtualFileSystem_FolderAdded;
        _virtualFileSystem.FolderDeleted += VirtualFileSystem_FolderDeleted;
        _virtualFileSystem.FolderMoved += VirtualFileSystem_FolderMoved;
    }

    #region Virtual file system event handlers

    private void VirtualFileSystem_FileAdded(object? sender, VirtualFileSystemEventArgs e)
    {
        _logger.LogDebug("Virtual file added: {Path}", e.Path);
        // Clear cache for the parent directory
        _nodeCache.InvalidateDirectory(Path.GetDirectoryName(e.Path) ?? "\\");
    }

    private void VirtualFileSystem_FileDeleted(object? sender, VirtualFileSystemEventArgs e)
    {
        _logger.LogDebug("Virtual file deleted: {Path}", e.Path);
        // Clear cache for the parent directory
        _nodeCache.InvalidateDirectory(Path.GetDirectoryName(e.Path) ?? "\\");
    }

    private void VirtualFileSystem_FileMoved(object? sender, VirtualFileSystemMoveEventArgs e)
    {
        _logger.LogDebug("Virtual file moved: {OldPath} -> {NewPath}", e.OldPath, e.NewPath);
        // Clear cache for both old and new parent directories
        _nodeCache.InvalidateDirectory(Path.GetDirectoryName(e.OldPath) ?? "\\");
        _nodeCache.InvalidateDirectory(Path.GetDirectoryName(e.NewPath) ?? "\\");
    }

    private void VirtualFileSystem_FolderAdded(object? sender, VirtualFileSystemEventArgs e)
    {
        _logger.LogDebug("Virtual folder added: {Path}", e.Path);
        // Clear cache for the parent directory
        _nodeCache.InvalidateDirectory(Path.GetDirectoryName(e.Path) ?? "\\");
    }

    private void VirtualFileSystem_FolderDeleted(object? sender, VirtualFileSystemEventArgs e)
    {
        _logger.LogDebug("Virtual folder deleted: {Path}", e.Path);
        // Clear cache for the parent directory
        _nodeCache.InvalidateDirectory(Path.GetDirectoryName(e.Path) ?? "\\");
        // Clear cache for this directory and all subdirectories
        _nodeCache.InvalidateDirectoryAndSubdirectories(e.Path);
    }

    private void VirtualFileSystem_FolderMoved(object? sender, VirtualFileSystemMoveEventArgs e)
    {
        _logger.LogDebug("Virtual folder moved: {OldPath} -> {NewPath}", e.OldPath, e.NewPath);
        // Clear cache for both old and new parent directories
        _nodeCache.InvalidateDirectory(Path.GetDirectoryName(e.OldPath) ?? "\\");
        _nodeCache.InvalidateDirectory(Path.GetDirectoryName(e.NewPath) ?? "\\");
        // Clear cache for this directory and all subdirectories
        _nodeCache.InvalidateDirectoryAndSubdirectories(e.OldPath);
    }

    #endregion

    #region Helper methods

    private static void ThrowIoExceptionWithHResult(int hResult)
        => throw new IOException(message: null, hResult);

    private static void ThrowIoExceptionWithWin32(int error)
        => ThrowIoExceptionWithHResult(unchecked((int)(0x80070000 | error)));

    private static void ThrowIoExceptionWithNtStatus(int status)
        => ThrowIoExceptionWithWin32((int)Win32FromNtStatus(status));

    private string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "\\";

        path = path.Replace('/', '\\');

        if (!path.StartsWith("\\"))
            path = "\\" + path;

        return path.TrimEnd('\\') == "" ? "\\" : path.TrimEnd('\\');
    }

    private string GetLocalPath(string normalizedPath)
    {
        if (normalizedPath == "\\")
            return _localPath;

        return Path.Combine(_localPath, normalizedPath.TrimStart('\\'));
    }

    private bool IsVirtualFile(string normalizedPath)
    {
        return _virtualFileSystem.FileExists(normalizedPath);
    }

    private bool IsVirtualFolder(string normalizedPath)
    {
        return _virtualFileSystem.FolderExists(normalizedPath);
    }

    private bool LocalFileExists(string normalizedPath)
    {
        return File.Exists(GetLocalPath(normalizedPath));
    }

    private bool LocalDirectoryExists(string normalizedPath)
    {
        return Directory.Exists(GetLocalPath(normalizedPath));
    }

    private FileNode? FindNode(string normalizedPath)
    {
        try
        {
            _logger.LogDebug("FindNode: Searching for {Path}", normalizedPath);
            
            var cachedNode = _nodeCache.GetNode(normalizedPath);
            if (cachedNode != null)
            {
                _logger.LogDebug("FindNode: Found cached node for {Path} (IsDir={IsDir}, IsLocal={IsLocal})", 
                    normalizedPath, cachedNode.IsDirectory, cachedNode.IsLocal);
                return cachedNode;
            }

            // Check local first
            if (LocalFileExists(normalizedPath))
            {
                _logger.LogDebug("FindNode: Found local file: {Path}", normalizedPath);
                var fileInfo = new System.IO.FileInfo(GetLocalPath(normalizedPath));
                var node = new FileNode(normalizedPath)
                {
                    IsDirectory = false,
                    IsLocal = true,
                    LastAccessTime = fileInfo.LastAccessTime,
                    LastWriteTime = fileInfo.LastWriteTime,
                    CreationTime = fileInfo.CreationTime,
                    FileSize = fileInfo.Length
                };

                _nodeCache.AddNode(normalizedPath, node);
                return node;
            }
            else if (LocalDirectoryExists(normalizedPath))
            {
                _logger.LogDebug("FindNode: Found local directory: {Path}", normalizedPath);
                var dirInfo = new DirectoryInfo(GetLocalPath(normalizedPath));
                var node = new FileNode(normalizedPath)
                {
                    IsDirectory = true,
                    IsLocal = true,
                    LastAccessTime = dirInfo.LastAccessTime,
                    LastWriteTime = dirInfo.LastWriteTime,
                    CreationTime = dirInfo.CreationTime,
                    FileSize = 0
                };

                // Important: Check if this directory also has a virtual counterpart
                if (IsVirtualFolder(normalizedPath))
                {
                    _logger.LogDebug("FindNode: Local directory also has virtual counterpart: {Path}", normalizedPath);
                    var virtualFolder = FindVirtualFolder(normalizedPath);
                    if (virtualFolder != null)
                    {
                        _logger.LogDebug("FindNode: Attaching virtual folder to local directory node: {Path}", normalizedPath);
                        node.VirtualFolder = virtualFolder;
                    }
                }

                _nodeCache.AddNode(normalizedPath, node);
                return node;
            }
            else if (IsVirtualFile(normalizedPath))
            {
                _logger.LogDebug("FindNode: Found virtual file: {Path}", normalizedPath);
                // Get file details from virtual file system
                var virtualFile = FindVirtualFile(normalizedPath);
                if (virtualFile != null)
                {
                    var node = new FileNode(normalizedPath)
                    {
                        IsDirectory = false,
                        IsLocal = false,
                        LastAccessTime = virtualFile.RemoteFile.AccessedDate.ToLocalTime(),
                        LastWriteTime = virtualFile.RemoteFile.ModifiedDate.ToLocalTime(),
                        CreationTime = virtualFile.RemoteFile.CreatedDate.ToLocalTime(),
                        FileSize = virtualFile.RemoteFile.Size,
                        VirtualFile = virtualFile
                    };

                    _nodeCache.AddNode(normalizedPath, node);
                    _logger.LogDebug("FindNode: Created node for virtual file: {Path}", normalizedPath);
                    return node;
                }
                else
                {
                    _logger.LogWarning("FindNode: Virtual file exists but failed to retrieve: {Path}", normalizedPath);
                }
            }
            else if (IsVirtualFolder(normalizedPath))
            {
                _logger.LogDebug("FindNode: Found virtual folder: {Path}", normalizedPath);
                // Get folder details from virtual file system
                var virtualFolder = FindVirtualFolder(normalizedPath);
                if (virtualFolder != null)
                {
                    var now = DateTime.Now;
                    var node = new FileNode(normalizedPath)
                    {
                        IsDirectory = true,
                        IsLocal = false,
                        LastAccessTime = now,
                        LastWriteTime = now,
                        CreationTime = now,
                        FileSize = 0,
                        VirtualFolder = virtualFolder
                    };

                    _nodeCache.AddNode(normalizedPath, node);
                    _logger.LogDebug("FindNode: Created node for virtual folder: {Path}", normalizedPath);
                    return node;
                }
                else
                {
                    _logger.LogWarning("FindNode: Virtual folder exists but failed to retrieve: {Path}", normalizedPath);
                }
            }

            _logger.LogDebug("FindNode: No node found for {Path}", normalizedPath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding node for path: {Path}", normalizedPath);
            return null;
        }
    }

    private IVirtualFile? FindVirtualFile(string normalizedPath)
    {
        try
        {
            if (_virtualFileSystem.FileExists(normalizedPath))
            {
                _logger.LogDebug("FindVirtualFile: Found file via direct lookup: {Path}", normalizedPath);
                
                // Since we know the file exists but don't have a direct reference,
                // we need to navigate to it
                string directoryPath = Path.GetDirectoryName(normalizedPath) ?? "\\";
                string fileName = Path.GetFileName(normalizedPath);
                
                IVirtualFolder? parentFolder = FindVirtualFolder(directoryPath);
                if (parentFolder != null)
                {
                    var file = parentFolder.Files.Find(f => string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));
                    if (file != null)
                    {
                        _logger.LogDebug("FindVirtualFile: Retrieved file reference for: {Path}", normalizedPath);
                        return file;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding virtual file: {Path}", normalizedPath);
            return null;
        }

        return null;
    }

    private IVirtualFolder? FindVirtualFolder(string normalizedPath)
    {
        try
        {
            // Check if it's the root folder
            if (normalizedPath == "\\")
            {
                _logger.LogDebug("FindVirtualFolder: Returning root folder for: {Path}", normalizedPath);
                return _virtualFileSystem.Root;
            }
            
            if (_virtualFileSystem.FolderExists(normalizedPath))
            {
                _logger.LogDebug("FindVirtualFolder: Found folder via direct lookup: {Path}", normalizedPath);
                
                // Get the path parts to navigate the hierarchy
                var parts = normalizedPath.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
                
                // Navigate the folder hierarchy
                IVirtualFolder currentFolder = _virtualFileSystem.Root;
                foreach (var part in parts)
                {
                    var nextFolder = currentFolder.Subfolders
                        .FirstOrDefault(f => string.Equals(f.Name, part, StringComparison.OrdinalIgnoreCase));
                    
                    if (nextFolder == null)
                    {
                        _logger.LogWarning("FindVirtualFolder: Folder exists but navigation failed: {Path}", normalizedPath);
                        return null;
                    }
                    
                    currentFolder = nextFolder;
                }
                
                _logger.LogDebug("FindVirtualFolder: Successfully navigated to folder: {Path}", normalizedPath);
                return currentFolder;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding virtual folder: {Path}", normalizedPath);
        }
        
        return null;
    }

    private static byte[] GenerateVfsSecurityDescriptor()
    {
        var securityIdentifier = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        var allowAccessRule = new FileSystemAccessRule(
            securityIdentifier,
            FileSystemRights.ReadAndExecute | FileSystemRights.ReadAttributes |
            FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.Synchronize |
            FileSystemRights.Write,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow
        );

        var denyAccessRule = new FileSystemAccessRule(
            securityIdentifier,
            FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny
        );

        var fileSecurity = new FileSecurity();
        fileSecurity.SetOwner(systemSid);
        fileSecurity.SetGroup(systemSid);
        fileSecurity.SetAccessRuleProtection(true, preserveInheritance: false);
        fileSecurity.AddAccessRule(allowAccessRule);
        fileSecurity.AddAccessRule(denyAccessRule);

        return fileSecurity.GetSecurityDescriptorBinaryForm();
    }

    #endregion

    #region FileSystemBase implementation
    
    public override int Init(object host0)
    {
        _logger.LogInformation("Initializing merged file system");
        var host = (FileSystemHost)host0;
        host.SectorSize = ALLOCATION_UNIT;
        host.SectorsPerAllocationUnit = 1;
        host.MaxComponentLength = 255;
        host.FileInfoTimeout = 10000;
        host.CaseSensitiveSearch = false;
        host.CasePreservedNames = true;
        host.UnicodeOnDisk = true;
        host.PersistentAcls = true;
        host.PostCleanupWhenModifiedOnly = true;
        host.PassQueryDirectoryPattern = true;
        host.FlushAndPurgeOnCleanup = true;
        host.VolumeCreationTime = (ulong)DateTime.UtcNow.ToFileTimeUtc();
        host.VolumeSerialNumber = 0;
        
        // Make sure the virtual root is properly included
        _logger.LogInformation("Ensuring virtual root is accessible");
        var rootNode = FindNode("\\");
        if (rootNode != null && !rootNode.IsLocal)
        {
            _logger.LogInformation("Found root node, and it's virtual");
        }
        else if (rootNode != null && rootNode.VirtualFolder == null)
        {
            _logger.LogInformation("Root node found but doesn't have VirtualFolder set - fixing this");
            rootNode.VirtualFolder = _virtualFileSystem.Root;
            _nodeCache.AddNode("\\", rootNode);
        }
        else if (rootNode == null)
        {
            _logger.LogInformation("Root node not found, creating it with virtual folder reference");
            var newRootNode = new FileNode("\\")
            {
                IsDirectory = true,
                IsLocal = true, // The root is both local and virtual
                LastAccessTime = DateTime.Now,
                LastWriteTime = DateTime.Now,
                CreationTime = DateTime.Now,
                FileSize = 0,
                VirtualFolder = _virtualFileSystem.Root // Link to virtual root
            };
            _nodeCache.AddNode("\\", newRootNode);
        }
        
        // Verify the root is set up properly
        rootNode = FindNode("\\");
        if (rootNode != null && rootNode.VirtualFolder != null)
        {
            _logger.LogInformation("Root node has VirtualFolder with {Count} subfolders and {FileCount} files",
                rootNode.VirtualFolder.Subfolders.Count, rootNode.VirtualFolder.Files.Count);
            
            // Log all subfolders in root's virtual folder for debugging
            foreach (var subfolder in rootNode.VirtualFolder.Subfolders)
            {
                _logger.LogDebug("Root's VirtualFolder subfolder: {Name}, Path: {Path}", 
                    subfolder.Name, subfolder.GetFullPath());
            }
        }
        else
        {
            _logger.LogWarning("Root node still doesn't have a proper VirtualFolder reference");
        }
        
        _logger.LogInformation("Merged file system initialized");
        return STATUS_SUCCESS;
    }

    public override int GetVolumeInfo(out VolumeInfo volumeInfo)
    {
        volumeInfo = default;
        
        try
        {
            var driveInfo = new DriveInfo(Path.GetPathRoot(_localPath) ?? "C:");
            volumeInfo.TotalSize = (ulong)driveInfo.TotalSize;
            volumeInfo.FreeSize = (ulong)driveInfo.AvailableFreeSpace;
            
            _logger.LogDebug("GetVolumeInfo: TotalSize={TotalSize}, FreeSize={FreeSize}", 
                volumeInfo.TotalSize, volumeInfo.FreeSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting volume info");
            // Default values if we can't get drive info
            volumeInfo.TotalSize = 1024L * 1024L * 1024L * 100L; // 100 GB
            volumeInfo.FreeSize = 1024L * 1024L * 1024L * 50L;   // 50 GB
        }
        
        return STATUS_SUCCESS;
    }

    public override int GetSecurityByName(
        string fileName,
        out uint fileAttributes,
        ref byte[] securityDescriptor)
    {
        _logger.LogDebug("GetSecurityByName: {FileName}", fileName);
        
        var normalizedPath = NormalizePath(fileName);
        _logger.LogDebug("GetSecurityByName: Normalized path: {Path}", normalizedPath);
        
        var node = FindNode(normalizedPath);
        
        if (node == null)
        {
            fileAttributes = 0;
            _logger.LogDebug("GetSecurityByName: Not found");
            
            // Special debug check - is it a virtual file or folder?
            if (IsVirtualFile(normalizedPath))
            {
                _logger.LogWarning("GetSecurityByName: Virtual file exists but FindNode failed: {Path}", normalizedPath);
                var virtualFile = FindVirtualFile(normalizedPath);
                _logger.LogWarning("GetSecurityByName: Direct FindVirtualFile result: {Result}", virtualFile != null);
            }
            else if (IsVirtualFolder(normalizedPath))
            {
                _logger.LogWarning("GetSecurityByName: Virtual folder exists but FindNode failed: {Path}", normalizedPath);
                var virtualFolder = FindVirtualFolder(normalizedPath);
                _logger.LogWarning("GetSecurityByName: Direct FindVirtualFolder result: {Result}", virtualFolder != null);
            }
            
            return STATUS_OBJECT_NAME_NOT_FOUND;
        }
        
        if (node.IsDirectory)
        {
            fileAttributes = (uint)FileAttributes.Directory;
            _logger.LogDebug("GetSecurityByName: It's a directory (IsLocal={IsLocal}, HasVirtualFolder={HasVirtualFolder})", 
                node.IsLocal, node.VirtualFolder != null);
        }
        else
        {
            fileAttributes = (uint)FileAttributes.Normal;
            _logger.LogDebug("GetSecurityByName: It's a file (IsLocal={IsLocal}, HasVirtualFile={HasVirtualFile})", 
                node.IsLocal, node.VirtualFile != null);
        }

        securityDescriptor = GenerateVfsSecurityDescriptor();
        
        return STATUS_SUCCESS;
    }

    public override int Create(
        string fileName,
        uint createOptions,
        uint grantedAccess,
        uint fileAttributes,
        byte[] securityDescriptor,
        ulong allocationSize,
        out object? fileNode,
        out object? fileDesc0,
        out FileInfo fileInfo,
        out string normalizedName)
    {
        _logger.LogDebug("Create: {FileName}, createOptions=0x{CreateOptions:X}, grantedAccess=0x{GrantedAccess:X}", 
            fileName, createOptions, grantedAccess);
        
        fileNode = default;
        fileDesc0 = default;
        fileInfo = default;
        normalizedName = fileName;
        
        try
        {
            var normalizedPath = NormalizePath(fileName);
            
            // Check if the node already exists
            var existingNode = FindNode(normalizedPath);
            if (existingNode != null)
            {
                _logger.LogWarning("Create: Node already exists: {Path}", normalizedPath);
                return STATUS_OBJECT_NAME_COLLISION;
            }
            
            // Check if the parent directory exists
            var parentPath = Path.GetDirectoryName(normalizedPath) ?? "\\";
            var parentNode = FindNode(parentPath);
            
            if (parentNode == null)
            {
                _logger.LogWarning("Create: Parent directory not found: {ParentPath}", parentPath);
                return STATUS_OBJECT_PATH_NOT_FOUND;
            }
            
            if (!parentNode.IsDirectory)
            {
                _logger.LogWarning("Create: Parent is not a directory: {ParentPath}", parentPath);
                return STATUS_NOT_A_DIRECTORY;
            }
            
            // Create the file or directory in the local file system
            var localPath = GetLocalPath(normalizedPath);
            bool isDirectory = (createOptions & FILE_DIRECTORY_FILE) != 0;
            
            if (isDirectory)
            {
                _logger.LogDebug("Create: Creating directory: {LocalPath}", localPath);
                Directory.CreateDirectory(localPath);
            }
            else
            {
                _logger.LogDebug("Create: Creating file: {LocalPath}", localPath);
                
                // Ensure parent directory exists in the local file system
                var localParentDir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(localParentDir) && !Directory.Exists(localParentDir))
                {
                    Directory.CreateDirectory(localParentDir);
                }
                
                using (var stream = new FileStream(
                    localPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read | FileShare.Write | FileShare.Delete,
                    4096,
                    FileOptions.None))
                {
                    if (allocationSize > 0)
                    {
                        stream.SetLength((long)allocationSize);
                    }
                }
                
                if (fileAttributes != 0)
                {
                    File.SetAttributes(localPath, (FileAttributes)fileAttributes);
                }
            }
            
            // Create a new file descriptor
            var node = new FileNode(normalizedPath)
            {
                IsDirectory = isDirectory,
                IsLocal = true,
                CreationTime = DateTime.Now,
                LastAccessTime = DateTime.Now,
                LastWriteTime = DateTime.Now,
                FileSize = (long) allocationSize
            };
            
            // Check if we also have a virtual counterpart for directories
            if (isDirectory && IsVirtualFolder(normalizedPath))
            {
                _logger.LogDebug("Create: Directory also has a virtual counterpart, linking it");
                node.VirtualFolder = FindVirtualFolder(normalizedPath);
            }
            
            var fd = new FileDescriptor(node, localPath);
            
            // Update the node cache
            _nodeCache.AddNode(normalizedPath, node);
            
            fileNode = node;
            fileDesc0 = fd;
            
            // Get file info
            fd.GetFileInfo(out fileInfo);
            
            _logger.LogDebug("Create: Successfully created {Type}: {Path}", 
                isDirectory ? "directory" : "file", normalizedPath);
            
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating file: {FileName}", fileName);
            return STATUS_INVALID_PARAMETER;
        }
    }
    
    public override int Open(
        string fileName,
        uint createOptions,
        uint grantedAccess,
        out object? fileNode,
        out object? fileDesc0,
        out FileInfo fileInfo,
        out string normalizedName)
    {
        _logger.LogDebug("Open: {FileName}, createOptions=0x{CreateOptions:X}, grantedAccess=0x{GrantedAccess:X}", 
            fileName, createOptions, grantedAccess);
        
        fileNode = default;
        fileDesc0 = default;
        fileInfo = default;
        normalizedName = fileName;
        
        try
        {
            var normalizedPath = NormalizePath(fileName);
            var node = FindNode(normalizedPath);
            
            if (node == null)
            {
                _logger.LogWarning("Open: Node not found: {Path}", normalizedPath);
                
                // Special debug check - is it a virtual file or folder?
                if (IsVirtualFile(normalizedPath))
                {
                    _logger.LogWarning("Open: Virtual file exists but FindNode failed: {Path}", normalizedPath);
                    
                    // If FindNode failed, try to create the node directly
                    var virtualFile = FindVirtualFile(normalizedPath);
                    if (virtualFile != null)
                    {
                        _logger.LogDebug("Open: Creating node for virtual file directly: {Path}", normalizedPath);
                        var newNode = new FileNode(normalizedPath)
                        {
                            IsDirectory = false,
                            IsLocal = false,
                            LastAccessTime = virtualFile.RemoteFile.AccessedDate.ToLocalTime(),
                            LastWriteTime = virtualFile.RemoteFile.ModifiedDate.ToLocalTime(),
                            CreationTime = virtualFile.RemoteFile.CreatedDate.ToLocalTime(),
                            FileSize = virtualFile.RemoteFile.Size,
                            VirtualFile = virtualFile
                        };
                        
                        _nodeCache.AddNode(normalizedPath, newNode);
                        node = newNode;
                    }
                    else
                    {
                        return STATUS_OBJECT_NAME_NOT_FOUND;
                    }
                }
                else if (IsVirtualFolder(normalizedPath))
                {
                    _logger.LogWarning("Open: Virtual folder exists but FindNode failed: {Path}", normalizedPath);
                    
                    // If FindNode failed, try to create the node directly
                    var virtualFolder = FindVirtualFolder(normalizedPath);
                    if (virtualFolder != null)
                    {
                        _logger.LogDebug("Open: Creating node for virtual folder directly: {Path}", normalizedPath);
                        var now = DateTime.Now;
                        var newNode = new FileNode(normalizedPath)
                        {
                            IsDirectory = true,
                            IsLocal = false,
                            LastAccessTime = now,
                            LastWriteTime = now,
                            CreationTime = now,
                            FileSize = 0,
                            VirtualFolder = virtualFolder
                        };
                        
                        _nodeCache.AddNode(normalizedPath, newNode);
                        node = newNode;
                    }
                    else
                    {
                        return STATUS_OBJECT_NAME_NOT_FOUND;
                    }
                }
                else
                {
                    return STATUS_OBJECT_NAME_NOT_FOUND;
                }
            }
            
            // Check if trying to open a file as directory or directory as file
            bool isDirectory = node.IsDirectory;
            bool openAsDirectory = (createOptions & FILE_DIRECTORY_FILE) != 0;
            bool openAsFile = (createOptions & FILE_NON_DIRECTORY_FILE) != 0;
            
            if ((openAsDirectory && !isDirectory) || (openAsFile && isDirectory))
            {
                _logger.LogWarning("Open: Type mismatch (dir/file): {Path}", normalizedPath);
                return isDirectory ? STATUS_FILE_IS_A_DIRECTORY : STATUS_NOT_A_DIRECTORY;
            }
            
            FileDescriptor fd;
            if (node.IsLocal)
            {
                var localPath = GetLocalPath(normalizedPath);
                fd = new FileDescriptor(node, localPath);
            }
            else
            {
                fd = new FileDescriptor(node, string.Empty);
            }
            
            // Update last access time
            node.LastAccessTime = DateTime.Now;
            
            fileNode = node;
            fileDesc0 = fd;
            
            // Get file info
            fd.GetFileInfo(out fileInfo);
            
            _logger.LogDebug("Open: Successfully opened {Type}: {Path}", 
                isDirectory ? "directory" : "file", normalizedPath);
            
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening file: {FileName}", fileName);
            return STATUS_INVALID_PARAMETER;
        }
    }

    public override int Overwrite(
        object fileNode,
        object fileDesc0,
        uint fileAttributes,
        bool replaceFileAttributes,
        ulong allocationSize,
        out FileInfo fileInfo)
    {
        var node = (FileNode)fileNode;
        var fd = (FileDescriptor)fileDesc0;
        
        _logger.LogDebug("Overwrite: {Path}, fileAttributes=0x{FileAttributes:X}, replaceFileAttributes={ReplaceFileAttributes}", 
            node.Path, fileAttributes, replaceFileAttributes);
        
        fileInfo = default;
        
        try
        {
            if (!node.IsLocal)
            {
                _logger.LogWarning("Overwrite: Cannot overwrite virtual file: {Path}", node.Path);
                return STATUS_ACCESS_DENIED;
            }
            
            var localPath = GetLocalPath(node.Path);
            
            using (var stream = new FileStream(
                localPath,
                FileMode.Truncate,
                FileAccess.ReadWrite,
                FileShare.Read | FileShare.Write | FileShare.Delete,
                4096,
                FileOptions.None))
            {
                if (allocationSize > 0)
                {
                    stream.SetLength((long)allocationSize);
                }
            }
            
            if (fileAttributes != 0)
            {
                if (replaceFileAttributes)
                {
                    File.SetAttributes(localPath, (FileAttributes)fileAttributes);
                }
                else
                {
                    var currentAttributes = File.GetAttributes(localPath);
                    File.SetAttributes(localPath, currentAttributes | (FileAttributes)fileAttributes);
                }
            }
            
            // Update node info
            node.LastWriteTime = DateTime.Now;
            node.FileSize = (long) allocationSize;
            
            // Get updated file info
            fd.GetFileInfo(out fileInfo);
            
            _logger.LogDebug("Overwrite: Successfully overwrote file: {Path}", node.Path);
            
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error overwriting file: {Path}", node.Path);
            return STATUS_INVALID_PARAMETER;
        }
    }

    public override void Cleanup(
        object fileNode,
        object fileDesc0,
        string fileName,
        uint flags)
    {
        var node = (FileNode)fileNode;
        var fd = (FileDescriptor)fileDesc0;
        
        _logger.LogDebug("Cleanup: {FileName}, flags=0x{Flags:X}", fileName, flags);
        
        try
        {
            // Check if the delete flag is set
            if ((flags & CleanupDelete) != 0)
            {
                _logger.LogDebug("Cleanup: Delete requested for {Path}", node.Path);
                
                if (node.IsLocal)
                {
                    var localPath = GetLocalPath(node.Path);
                    
                    if (node.IsDirectory)
                    {
                        if (Directory.Exists(localPath))
                        {
                            // Check if directory is empty
                            if (Directory.GetFileSystemEntries(localPath).Length == 0)
                            {
                                _logger.LogDebug("Cleanup: Deleting local directory: {LocalPath}", localPath);
                                Directory.Delete(localPath);
                            }
                            else
                            {
                                _logger.LogWarning("Cleanup: Cannot delete non-empty directory: {LocalPath}", localPath);
                            }
                        }
                    }
                    else
                    {
                        if (File.Exists(localPath))
                        {
                            _logger.LogDebug("Cleanup: Deleting local file: {LocalPath}", localPath);
                            File.Delete(localPath);
                        }
                    }
                }
                else
                {
                    // Handle virtual file/folder deletion
                    if (node.IsDirectory)
                    {
                        _logger.LogDebug("Cleanup: Deleting virtual directory: {Path}", node.Path);
                        _virtualFileSystem.DeleteFolder(node.Path);
                    }
                    else
                    {
                        _logger.LogDebug("Cleanup: Deleting virtual file: {Path}", node.Path);
                        _virtualFileSystem.DeleteFile(node.Path);
                    }
                }
                
                // Remove from cache
                _nodeCache.RemoveNode(node.Path);
            }
            
            // Update timestamps
            if ((flags & CleanupSetLastAccessTime) != 0 ||
                (flags & CleanupSetLastWriteTime) != 0 ||
                (flags & CleanupSetChangeTime) != 0)
            {
                var now = DateTime.Now;
                
                if (node.IsLocal)
                {
                    var localPath = GetLocalPath(node.Path);
                    
                    if (node.IsDirectory)
                    {
                        var dirInfo = new DirectoryInfo(localPath);
                        if ((flags & CleanupSetLastAccessTime) != 0)
                            dirInfo.LastAccessTime = now;
                        if ((flags & CleanupSetLastWriteTime) != 0)
                            dirInfo.LastWriteTime = now;
                    }
                    else
                    {
                        var fileInfo = new System.IO.FileInfo(localPath);
                        if ((flags & CleanupSetLastAccessTime) != 0)
                            fileInfo.LastAccessTime = now;
                        if ((flags & CleanupSetLastWriteTime) != 0)
                            fileInfo.LastWriteTime = now;
                    }
                }
                
                // Update our node
                if ((flags & CleanupSetLastAccessTime) != 0)
                    node.LastAccessTime = now;
                if ((flags & CleanupSetLastWriteTime) != 0)
                    node.LastWriteTime = now;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup: {FileName}", fileName);
        }
    }

    public override void Close(object fileNode, object fileDesc0)
    {
        var node = (FileNode)fileNode;
        var fd = (FileDescriptor)fileDesc0;
        
        _logger.LogDebug("Close: {Path}", node.Path);
        
        // Close any open streams
        fd.CloseStreams();
    }

    public override int Read(
        object fileNode,
        object fileDesc0,
        IntPtr buffer,
        ulong offset,
        uint length,
        out uint pBytesTransferred)
    {
        var node = (FileNode)fileNode;
        var fd = (FileDescriptor)fileDesc0;
        
        _logger.LogDebug("Read: {Path}, offset={Offset}, length={Length}", node.Path, offset, length);
        
        pBytesTransferred = 0;
        
        try
        {
            if (node.IsDirectory)
            {
                _logger.LogWarning("Read: Cannot read from directory: {Path}", node.Path);
                return STATUS_INVALID_DEVICE_REQUEST;
            }
            
            if ((long) offset >= node.FileSize)
            {
                _logger.LogDebug("Read: Offset beyond EOF");
                return STATUS_END_OF_FILE;
            }
            
            if (node.IsLocal)
            {
                // Read from local file
                var localPath = GetLocalPath(node.Path);
                
                using (var stream = new FileStream(
                    localPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Write | FileShare.Delete,
                    4096,
                    FileOptions.None))
                {
                    stream.Position = (long)offset;
                    
                    var maxBytesToRead = Math.Min(length, node.FileSize - (long)offset);
                    var bytes = new byte[maxBytesToRead];
                    
                    var bytesRead = stream.Read(bytes, 0, bytes.Length);
                    if (bytesRead > 0)
                    {
                        Marshal.Copy(bytes, 0, buffer, bytesRead);
                        pBytesTransferred = (uint)bytesRead;
                    }
                }
            }
            else if (node.VirtualFile != null)
            {
                // Read from virtual file
                _logger.LogDebug("Read: Reading from virtual file: {Path}, Size={Size}", node.Path, node.FileSize);
                
                try
                {
                    var task = node.VirtualFile.ReadFileContentAsync((long)offset, (int)length);
                    var result = task.Result;
                    
                    if (result != null && result.Length > 0)
                    {
                        _logger.LogDebug("Read: Got {BytesRead} bytes from virtual file", result.Length);
                        Marshal.Copy(result, 0, buffer, length < result.Length ? (int) length : result.Length);
                        pBytesTransferred = (uint)result.Length;
                    }
                    else
                    {
                        _logger.LogWarning("Read: No data returned from virtual file");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading from virtual file: {Path}", node.Path);
                    return STATUS_INVALID_PARAMETER;
                }
            }
            else
            {
                _logger.LogWarning("Read: Node has no VirtualFile reference: {Path}", node.Path);
                return STATUS_INVALID_PARAMETER;
            }
            
            _logger.LogDebug("Read: {BytesRead} bytes read", pBytesTransferred);
            
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file: {Path}", node.Path);
            return STATUS_INVALID_PARAMETER;
        }
    }

    public override int Write(
        object fileNode,
        object fileDesc0,
        IntPtr buffer,
        ulong offset,
        uint length,
        bool writeToEndOfFile,
        bool constrainedIo,
        out uint pBytesTransferred,
        out FileInfo fileInfo)
    {
        var node = (FileNode)fileNode;
        var fd = (FileDescriptor)fileDesc0;
        
        _logger.LogDebug("Write: {Path}, offset={Offset}, length={Length}, writeToEndOfFile={WriteToEndOfFile}, constrainedIo={ConstrainedIo}", 
            node.Path, offset, length, writeToEndOfFile, constrainedIo);
        
        pBytesTransferred = 0;
        fileInfo = default;
        
        try
        {
            if (node.IsDirectory)
            {
                _logger.LogWarning("Write: Cannot write to directory: {Path}", node.Path);
                return STATUS_INVALID_DEVICE_REQUEST;
            }
            
            if (!node.IsLocal)
            {
                _logger.LogWarning("Write: Cannot write to virtual file: {Path}", node.Path);
                return STATUS_ACCESS_DENIED;
            }
            
            var localPath = GetLocalPath(node.Path);
            
            using (var stream = new FileStream(
                localPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read | FileShare.Write | FileShare.Delete,
                4096,
                FileOptions.None))
            {
                if (constrainedIo)
                {
                    if (offset >= (ulong)stream.Length)
                    {
                        _logger.LogDebug("Write: Offset beyond EOF with constrainedIo");
                        fd.GetFileInfo(out fileInfo);
                        return STATUS_SUCCESS;
                    }
                    
                    if (offset + length > (ulong)stream.Length)
                    {
                        length = (uint)((ulong)stream.Length - offset);
                    }
                }
                
                if (writeToEndOfFile)
                {
                    stream.Position = stream.Length;
                }
                else
                {
                    stream.Position = (long)offset;
                }
                
                var bytes = new byte[length];
                Marshal.Copy(buffer, bytes, 0, bytes.Length);
                
                stream.Write(bytes, 0, bytes.Length);
                pBytesTransferred = length;
                
                // Update node info
                node.FileSize = stream.Length;
                node.LastWriteTime = DateTime.Now;
            }
            
            fd.GetFileInfo(out fileInfo);
            
            _logger.LogDebug("Write: {BytesWritten} bytes written", pBytesTransferred);
            
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing to file: {Path}", node.Path);
            return STATUS_INVALID_PARAMETER;
        }
    }

    public override int Flush(object fileNode, object fileDesc0, out FileInfo fileInfo)
    {
        var node = (FileNode)fileNode;
        var fd = (FileDescriptor)fileDesc0;
        
        _logger.LogDebug("Flush: {Path}", node?.Path ?? "(null)");
        
        if (node == null)
        {
            fileInfo = default;
            return STATUS_SUCCESS;
        }
        
        // No need to flush virtual files
        fd.GetFileInfo(out fileInfo);
        
        return STATUS_SUCCESS;
    }

    public override int GetFileInfo(object fileNode, object fileDesc0, out FileInfo fileInfo)
    {
        var node = (FileNode)fileNode;
        var fd = (FileDescriptor)fileDesc0;
        
        _logger.LogDebug("GetFileInfo: {Path}", node.Path);
        
        return fd.GetFileInfo(out fileInfo);
    }

    public override int SetBasicInfo(
        object fileNode,
        object fileDesc0,
        uint fileAttributes,
        ulong creationTime,
        ulong lastAccessTime,
        ulong lastWriteTime,
        ulong changeTime,
        out FileInfo fileInfo)
    {
        var node = (FileNode)fileNode;
        var fd = (FileDescriptor)fileDesc0;
        
        _logger.LogDebug("SetBasicInfo: {Path}, fileAttributes=0x{FileAttributes:X}", node.Path, fileAttributes);
        
        fileInfo = default;
        
        try
        {
            // We can't modify attributes of virtual files
            if (!node.IsLocal)
            {
                _logger.LogWarning("SetBasicInfo: Cannot modify attributes of virtual file: {Path}", node.Path);
                fd.GetFileInfo(out fileInfo);
                return STATUS_SUCCESS;
            }
            
            var localPath = GetLocalPath(node.Path);
            
            if (fileAttributes != 0 && fileAttributes != unchecked((uint)-1))
            {
                if (node.IsDirectory)
                {
                    // For directories, we can only set some attributes
                    var allowedAttributes = (uint)(
                        FileAttributes.ReadOnly |
                        FileAttributes.Hidden |
                        FileAttributes.System |
                        FileAttributes.Archive);
                    
                    // Keep directory attribute
                    fileAttributes = (fileAttributes & allowedAttributes) | (uint)FileAttributes.Directory;
                }
                
                File.SetAttributes(localPath, (FileAttributes)fileAttributes);
            }
            
            if (creationTime != 0)
            {
                var dt = DateTime.FromFileTimeUtc((long)creationTime);
                if (node.IsDirectory)
                    Directory.SetCreationTimeUtc(localPath, dt);
                else
                    File.SetCreationTimeUtc(localPath, dt);
                
                node.CreationTime = dt.ToLocalTime();
            }
            
            if (lastAccessTime != 0)
            {
                var dt = DateTime.FromFileTimeUtc((long)lastAccessTime);
                if (node.IsDirectory)
                    Directory.SetLastAccessTimeUtc(localPath, dt);
                else
                    File.SetLastAccessTimeUtc(localPath, dt);
                
                node.LastAccessTime = dt.ToLocalTime();
            }
            
            if (lastWriteTime != 0)
            {
                var dt = DateTime.FromFileTimeUtc((long)lastWriteTime);
                if (node.IsDirectory)
                    Directory.SetLastWriteTimeUtc(localPath, dt);
                else
                    File.SetLastWriteTimeUtc(localPath, dt);
                
                node.LastWriteTime = dt.ToLocalTime();
            }
            
            fd.GetFileInfo(out fileInfo);
            
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting basic info: {Path}", node.Path);
            return STATUS_INVALID_PARAMETER;
        }
    }

    public override int SetFileSize(
        object fileNode,
        object fileDesc0,
        ulong newSize,
        bool setAllocationSize,
        out FileInfo fileInfo)
    {
        var node = (FileNode)fileNode;
        var fd = (FileDescriptor)fileDesc0;
    
        _logger.LogDebug("SetFileSize: {Path}, newSize={NewSize}, setAllocationSize={SetAllocationSize}", 
            node.Path, newSize, setAllocationSize);
    
        fileInfo = default;
    
        try
        {
            if (!node.IsLocal)
            {
                _logger.LogWarning("SetFileSize: Cannot modify size of virtual file: {Path}", node.Path);
                return STATUS_ACCESS_DENIED;
            }
        
            var localPath = GetLocalPath(node.Path);
        
            // Only modify the actual file length when not setting allocation size
            if (!setAllocationSize)
            {
                using (var stream = new FileStream(
                           localPath,
                           FileMode.Open,
                           FileAccess.ReadWrite,
                           FileShare.Read | FileShare.Write | FileShare.Delete,
                           4096,
                           FileOptions.None))
                {
                    stream.SetLength((long)newSize);
                
                    // Update node info
                    node.FileSize = (long) newSize;
                    node.LastWriteTime = DateTime.Now;
                }
            }
            else
            {
                // When setting allocation size, just update the node's information
                // but don't actually change the file length
                _logger.LogDebug("SetFileSize: Updating allocation size only, file content size remains unchanged");
            }
        
            fd.GetFileInfo(out fileInfo);
        
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting file size: {Path}", node.Path);
            return STATUS_INVALID_PARAMETER;
        }
    }
    
    public override int CanDelete(object fileNode, object fileDesc0, string fileName)
    {
        var node = (FileNode)fileNode;
        
        _logger.LogDebug("CanDelete: {FileName}", fileName);
        
        try
        {
            if (node.IsDirectory)
            {
                if (node.IsLocal)
                {
                    var localPath = GetLocalPath(node.Path);
                    
                    if (Directory.Exists(localPath) && Directory.GetFileSystemEntries(localPath).Length > 0)
                    {
                        _logger.LogWarning("CanDelete: Cannot delete non-empty directory: {Path}", node.Path);
                        return STATUS_DIRECTORY_NOT_EMPTY;
                    }
                }
                
                // Check if virtual directory has children (even if it's a local directory with virtual counterpart)
                if (node.VirtualFolder != null && 
                    (node.VirtualFolder.Files.Count > 0 || node.VirtualFolder.Subfolders.Count > 0))
                {
                    _logger.LogWarning("CanDelete: Cannot delete non-empty virtual directory: {Path}", node.Path);
                    return STATUS_DIRECTORY_NOT_EMPTY;
                }
            }
            
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if can delete: {FileName}", fileName);
            return STATUS_INVALID_PARAMETER;
        }
    }

    public override int Rename(
        object fileNode,
        object fileDesc0,
        string fileName,
        string newFileName,
        bool replaceIfExists)
    {
        var node = (FileNode)fileNode;
        
        _logger.LogDebug("Rename: {FileName} -> {NewFileName}, replaceIfExists={ReplaceIfExists}", 
            fileName, newFileName, replaceIfExists);
        
        try
        {
            var normalizedOldPath = NormalizePath(fileName);
            var normalizedNewPath = NormalizePath(newFileName);
            
            // Check if the target already exists
            var targetNode = FindNode(normalizedNewPath);
            if (targetNode != null && !replaceIfExists)
            {
                _logger.LogWarning("Rename: Target already exists: {Path}", normalizedNewPath);
                return STATUS_OBJECT_NAME_COLLISION;
            }
            
            if (targetNode != null && targetNode.IsDirectory)
            {
                var localTargetPath = GetLocalPath(normalizedNewPath);
                if (Directory.Exists(localTargetPath) && Directory.GetFileSystemEntries(localTargetPath).Length > 0)
                {
                    _logger.LogWarning("Rename: Cannot replace non-empty directory: {Path}", normalizedNewPath);
                    return STATUS_DIRECTORY_NOT_EMPTY;
                }
            }
            
            if (node.IsLocal)
            {
                // Move local file or directory
                var localOldPath = GetLocalPath(normalizedOldPath);
                var localNewPath = GetLocalPath(normalizedNewPath);
                
                // Create the target directory if needed
                var targetDir = Path.GetDirectoryName(localNewPath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
                
                if (node.IsDirectory)
                {
                    // If target exists, delete it first
                    if (Directory.Exists(localNewPath) && replaceIfExists)
                    {
                        Directory.Delete(localNewPath);
                    }
                    
                    _logger.LogDebug("Rename: Moving directory: {OldPath} -> {NewPath}", localOldPath, localNewPath);
                    Directory.Move(localOldPath, localNewPath);
                }
                else
                {
                    // If target exists, delete it first
                    if (File.Exists(localNewPath) && replaceIfExists)
                    {
                        File.Delete(localNewPath);
                    }
                    
                    _logger.LogDebug("Rename: Moving file: {OldPath} -> {NewPath}", localOldPath, localNewPath);
                    File.Move(localOldPath, localNewPath);
                }
            }
            else
            {
                // Move virtual file or directory
                if (node.IsDirectory)
                {
                    _logger.LogDebug("Rename: Moving virtual directory: {OldPath} -> {NewPath}", normalizedOldPath, normalizedNewPath);
                    _virtualFileSystem.MoveFolder(normalizedOldPath, normalizedNewPath);
                }
                else
                {
                    _logger.LogDebug("Rename: Moving virtual file: {OldPath} -> {NewPath}", normalizedOldPath, normalizedNewPath);
                    _virtualFileSystem.MoveFile(normalizedOldPath, normalizedNewPath);
                }
            }
            
            // Update cache
            _nodeCache.RemoveNode(normalizedOldPath);
            _nodeCache.InvalidateDirectory(Path.GetDirectoryName(normalizedOldPath) ?? "\\");
            _nodeCache.InvalidateDirectory(Path.GetDirectoryName(normalizedNewPath) ?? "\\");
            
            return STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renaming: {FileName} -> {NewFileName}", fileName, newFileName);
            return STATUS_INVALID_PARAMETER;
        }
    }

    public override int GetSecurity(object fileNode, object fileDesc0, ref byte[] securityDescriptor)
    {
        _logger.LogDebug("GetSecurity: {Path}", ((FileNode)fileNode).Path);
        
        securityDescriptor = GenerateVfsSecurityDescriptor();
        
        return STATUS_SUCCESS;
    }

    public override int SetSecurity(
        object fileNode,
        object fileDesc0,
        AccessControlSections sections,
        byte[] securityDescriptor)
    {
        _logger.LogDebug("SetSecurity: {Path}", ((FileNode)fileNode).Path);
        
        // We don't actually modify the security descriptor, just pretend it worked
        return STATUS_SUCCESS;
    }
    
public override bool ReadDirectoryEntry(
        object fileNode,
        object fileDesc0,
        string pattern,
        string marker,
        ref object context,
        out string fileName,
        out FileInfo fileInfo)
    {
        var node = (FileNode)fileNode;
        var fd = (FileDescriptor)fileDesc0;
        
        _logger.LogDebug("ReadDirectoryEntry: {Path}, pattern={Pattern}, marker={Marker}, context={Context}", 
            node.Path, pattern, marker, context == null ? "null" : (context is int ? "index:" + context : "entries"));
        
        fileName = default;
        fileInfo = default;
        
        if (!node.IsDirectory)
        {
            _logger.LogWarning("ReadDirectoryEntry: Not a directory: {Path}", node.Path);
            return false;
        }
        
        try
        {
            // Handle the context state
            int currentIndex;
            List<DirEntry> entries;
            
            if (context == null)
            {
                // First call - initialize the entries list
                _logger.LogDebug("ReadDirectoryEntry: Initializing directory entries for {Path}", node.Path);
                
                entries = new List<DirEntry>();
                currentIndex = 0;
                
                // Add "." and ".." entries (except for root)
                if (node.Path != "\\")
                {
                    // Add "." entry
                    var dotNode = new FileNode(".")
                    {
                        IsDirectory = true,
                        IsLocal = node.IsLocal,
                        LastAccessTime = node.LastAccessTime,
                        LastWriteTime = node.LastWriteTime,
                        CreationTime = node.CreationTime
                    };
                    
                    entries.Add(new DirEntry { Name = ".", Node = dotNode });
                    
                    // Add ".." entry
                    var parentPath = Path.GetDirectoryName(node.Path) ?? "\\";
                    var parentNode = FindNode(parentPath);
                    
                    if (parentNode != null)
                    {
                        entries.Add(new DirEntry { Name = "..", Node = parentNode });
                    }
                }
                
                // Get a set of already added entry names to prevent duplicates
                var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                // Add local entries
                var localPath = GetLocalPath(node.Path);
                if (Directory.Exists(localPath))
                {
                    try
                    {
                        // Get directories
                        foreach (var dirPath in Directory.GetDirectories(localPath))
                        {
                            var dirInfo = new DirectoryInfo(dirPath);
                            var dirEntryName = dirInfo.Name;
                            
                            // Skip already added entries
                            if (addedNames.Contains(dirEntryName))
                                continue;
                            
                            addedNames.Add(dirEntryName);
                            
                            // Create path for the entry
                            var dirEntryPath = Path.Combine(node.Path, dirEntryName).Replace('/', '\\');
                            
                            var dirEntryNode = new FileNode(dirEntryPath)
                            {
                                IsDirectory = true,
                                IsLocal = true,
                                LastAccessTime = dirInfo.LastAccessTime,
                                LastWriteTime = dirInfo.LastWriteTime,
                                CreationTime = dirInfo.CreationTime
                            };
                            
                            // Check if this directory also has a virtual counterpart
                            if (IsVirtualFolder(dirEntryPath))
                            {
                                var virtualFolder = FindVirtualFolder(dirEntryPath);
                                if (virtualFolder != null)
                                {
                                    dirEntryNode.VirtualFolder = virtualFolder;
                                }
                            }
                            
                            entries.Add(new DirEntry { Name = dirEntryName, Node = dirEntryNode });
                        }
                        
                        // Get files
                        foreach (var filePath in Directory.GetFiles(localPath))
                        {
                            var localFileInfo = new System.IO.FileInfo(filePath);
                            var localFileName = localFileInfo.Name;
                            
                            // Skip already added entries
                            if (addedNames.Contains(localFileName))
                                continue;
                            
                            addedNames.Add(localFileName);
                            
                            var localFileNode = new FileNode(Path.Combine(node.Path, localFileName).Replace('/', '\\'))
                            {
                                IsDirectory = false,
                                IsLocal = true,
                                LastAccessTime = localFileInfo.LastAccessTime,
                                LastWriteTime = localFileInfo.LastWriteTime,
                                CreationTime = localFileInfo.CreationTime,
                                FileSize = localFileInfo.Length
                            };
                            
                            entries.Add(new DirEntry { Name = localFileName, Node = localFileNode });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error getting local directory entries: {Path}", localPath);
                    }
                }
                
                // Add virtual entries
                if (node.VirtualFolder != null)
                {
                    _logger.LogDebug("ReadDirectoryEntry: Adding virtual entries for folder: {Path}", node.Path);
                    _logger.LogDebug("  Virtual folder contains {SubfolderCount} subfolders and {FileCount} files",
                        node.VirtualFolder.Subfolders.Count, node.VirtualFolder.Files.Count);
                    
                    // Add subdirectories
                    foreach (var subfolder in node.VirtualFolder.Subfolders)
                    {
                        var folderName = subfolder.Name;
                        
                        // Skip if already added from local
                        if (addedNames.Contains(folderName))
                            continue;
                        
                        addedNames.Add(folderName);
                        
                        var virtualFolderPath = Path.Combine(node.Path, folderName).Replace('/', '\\');
                        var virtualFolderNode = new FileNode(virtualFolderPath)
                        {
                            IsDirectory = true,
                            IsLocal = false,
                            LastAccessTime = DateTime.Now,
                            LastWriteTime = DateTime.Now,
                            CreationTime = DateTime.Now,
                            VirtualFolder = subfolder
                        };
                        
                        entries.Add(new DirEntry { Name = folderName, Node = virtualFolderNode });
                    }
                    
                    // Add files
                    foreach (var virtualFile in node.VirtualFolder.Files)
                    {
                        var virtualFileName = virtualFile.Name;
                        
                        // Skip if already added from local
                        if (addedNames.Contains(virtualFileName))
                            continue;
                        
                        addedNames.Add(virtualFileName);
                        
                        var virtualFilePath = Path.Combine(node.Path, virtualFileName).Replace('/', '\\');
                        var virtualFileNode = new FileNode(virtualFilePath)
                        {
                            IsDirectory = false,
                            IsLocal = false,
                            LastAccessTime = virtualFile.RemoteFile.AccessedDate.ToLocalTime(),
                            LastWriteTime = virtualFile.RemoteFile.ModifiedDate.ToLocalTime(),
                            CreationTime = virtualFile.RemoteFile.CreatedDate.ToLocalTime(),
                            FileSize = virtualFile.RemoteFile.Size,
                            VirtualFile = virtualFile
                        };
                        
                        entries.Add(new DirEntry { Name = virtualFileName, Node = virtualFileNode });
                    }
                }
                else
                {
                    _logger.LogDebug("ReadDirectoryEntry: No virtual folder associated with: {Path}", node.Path);
                }
                
                // Filter by pattern if provided
                if (!string.IsNullOrEmpty(pattern) && pattern != "*")
                {
                    string regexPattern = "^" + Regex.Escape(pattern)
                        .Replace("\\*", ".*")
                        .Replace("\\?", ".") + "$";
                    
                    var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                    entries = entries
                        .Where(e => regex.IsMatch(e.Name))
                        .ToList();
                }
                
                // Sort entries by name
                entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                
                // Handle marker if present
                if (marker != null)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (string.Equals(entries[i].Name, marker, StringComparison.OrdinalIgnoreCase))
                        {
                            currentIndex = i + 1;
                            break;
                        }
                    }
                }
                
                // Create state object to store with context
                context = new DirectoryEnumerationState(entries, currentIndex);
            }
            else if (context is DirectoryEnumerationState state)
            {
                // Use the existing state
                entries = state.Entries;
                currentIndex = state.CurrentIndex;
            }
            else
            {
                _logger.LogError("ReadDirectoryEntry: Invalid context type: {Type}", context.GetType().Name);
                return false;
            }
            
            // Return the next entry if available
            if (currentIndex < entries.Count)
            {
                var entry = entries[currentIndex];
                fileName = entry.Name;
                
                entry.Node.GetFileInfo(out fileInfo);
                
                // Update the state for the next call
                ((DirectoryEnumerationState)context).CurrentIndex = currentIndex + 1;
                
                _logger.LogDebug("ReadDirectoryEntry: Returning entry {Index}/{Total}: {FileName}", 
                    currentIndex, entries.Count, entry.Name);
                
                return true;
            }
            
            _logger.LogDebug("ReadDirectoryEntry: No more entries (index={Index}, count={Count})", 
                currentIndex, entries.Count);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ReadDirectoryEntry for path: {Path}", node.Path);
            return false;
        }
    }

    // State class for directory enumeration
    private class DirectoryEnumerationState
    {
        public List<DirEntry> Entries { get; }
        public int CurrentIndex { get; set; }

        public DirectoryEnumerationState(List<DirEntry> entries, int currentIndex)
        {
            Entries = entries;
            CurrentIndex = currentIndex;
        }
    }
    
    #endregion
}
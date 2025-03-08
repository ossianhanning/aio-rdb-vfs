using MediaCluster.Common;
using MediaCluster.MediaAnalyzer.Models;
using MediaCluster.RealDebrid.SharedModels;
using System.Text;

namespace MediaCluster.MergedFileSystem.Test;

/// <summary>
/// Mock implementation of IVirtualFileSystem for testing
/// </summary>
public class MockVirtualFileSystem : IVirtualFileSystem
{
    private readonly MockVirtualFolder _root = new MockVirtualFolder("", null, isRoot: true);
    private readonly Dictionary<string, MockVirtualFile> _files = new();
    private readonly Dictionary<string, MockVirtualFolder> _folders = new();

    public event EventHandler<VirtualFileSystemEventArgs>? FileAdded;
    public event EventHandler<VirtualFileSystemEventArgs>? FileDeleted;
    public event EventHandler<VirtualFileSystemMoveEventArgs>? FileMoved;
    public event EventHandler<VirtualFileSystemEventArgs>? FolderAdded;
    public event EventHandler<VirtualFileSystemEventArgs>? FolderDeleted;
    public event EventHandler<VirtualFileSystemMoveEventArgs>? FolderMoved;

    public IVirtualFolder Root => _root;

    public MockVirtualFileSystem()
    {
        // Create basic structure with download folder
        var downloadsFolder = CreateFolder("\\downloads");

        // Create mock container with standard files
        var container1 = new RemoteContainer
        {
            Added = DateTime.Now.AddDays(-5),
            Files = new List<RemoteFile>(),
            Name = "Sample Movie (2023)"
        };

        // Create movie files in the downloads folder
        CreateMockMovieFiles(container1, downloadsFolder);

        // Create a TV show folder with episodes
        var tvShowFolder = CreateFolder("\\downloads\\Sample TV Show (2023)");
        var container2 = new RemoteContainer
        {
            Added = DateTime.Now.AddDays(-3),
            Files = new List<RemoteFile>(),
            Name = "Sample TV Show (2023)"
        };

        CreateMockTvShowFiles(container2, tvShowFolder);

        // Create some misc files in the root
        var miscContainer = new RemoteContainer
        {
            Added = DateTime.Now.AddDays(-1),
            Files = new List<RemoteFile>(),
            Name = "Misc Files"
        };

        CreateMockMiscFiles(miscContainer, _root);

        // Register all folders in the path lookup
        RegisterAllFolders();
    }

    private void RegisterAllFolders()
    {
        // Register root folder
        _folders["\\"] = _root;
        Console.WriteLine("Registered root folder: \\");

        // Recursively register all subfolders
        RegisterSubfolders(_root, "\\");

        // Log registration summary
        Console.WriteLine($"===== Virtual File System Registration Summary =====");
        Console.WriteLine($"Total folders registered: {_folders.Count}");
        Console.WriteLine($"Total files registered: {_files.Count}");
        Console.WriteLine($"Folders:");
        foreach (var folder in _folders.Keys.OrderBy(k => k))
        {
            Console.WriteLine($"  {folder}");
        }

        Console.WriteLine($"Files:");
        foreach (var file in _files.Keys.OrderBy(k => k))
        {
            Console.WriteLine($"  {file}");
        }

        Console.WriteLine($"=================================================");
    }

    private void RegisterSubfolders(MockVirtualFolder folder, string path)
    {
        foreach (var subfolder in folder.Subfolders)
        {
            var subfolderPath = path == "\\" 
                ? $"\\{subfolder.Name}" 
                : $"{path}\\{subfolder.Name}";
        
            subfolderPath = NormalizePath(subfolderPath);
        
            _folders[subfolderPath] = (MockVirtualFolder)subfolder;
            Console.WriteLine($"Registered folder: {subfolderPath}");
        
            // Register files in this folder
            foreach (var file in subfolder.Files)
            {
                var filePath = $"{subfolderPath}\\{file.Name}";
                filePath = NormalizePath(filePath);
            
                _files[filePath] = (MockVirtualFile)file;
                Console.WriteLine($"Registered file: {filePath}");
            }
        
            // Recursively register subfolders
            RegisterSubfolders((MockVirtualFolder)subfolder, subfolderPath);
        }
    }
    
    // Register files at root level too
    public void RegisterRootFiles()
    {
        foreach (var file in _root.Files)
        {
            var filePath = $"\\{file.Name}";
            filePath = NormalizePath(filePath);

            _files[filePath] = (MockVirtualFile)file;
            Console.WriteLine($"Registered root file: {filePath}");
        }
    }
    
    public void RepairRegistration()
    {
        Console.WriteLine("Repairing MockVirtualFileSystem registrations...");
    
        // Clear existing registrations
        _files.Clear();
        _folders.Clear();
    
        // Register root folder
        _folders["\\"] = _root;
        Console.WriteLine("Registered root folder: \\");
    
        // Recursively register all subfolders and files
        RegisterSubfolders(_root, "\\");
    
        // Log registration summary
        Console.WriteLine($"===== Virtual File System Registration Summary =====");
        Console.WriteLine($"Total folders registered: {_folders.Count}");
        Console.WriteLine($"Total files registered: {_files.Count}");
        Console.WriteLine($"Folders:");
        foreach (var folder in _folders.Keys.OrderBy(k => k))
        {
            Console.WriteLine($"  {folder}");
        }
        Console.WriteLine($"Files:");
        foreach (var file in _files.Keys.OrderBy(k => k))
        {
            Console.WriteLine($"  {file}");
        }
        Console.WriteLine($"=================================================");
    }


    private void CreateMockMovieFiles(RemoteContainer container, MockVirtualFolder folder)
    {
        // Main movie file
        CreateVirtualFile(
            container,
            folder,
            "Sample.Movie.2023.1080p.WEBDL.x264.mkv",
            1024L * 1024 * 1024 * 2, // 2GB
            new MediaInfo
            {
                VideoStreams = new List<VideoStream>
                {
                    new VideoStream
                    {
                        Width = 1920,
                        Height = 1080,
                    }
                },
                AudioStreams = new List<AudioStream>
                {
                    new AudioStream
                    {
                        Channels = 6,
                        Language = "eng"
                    }
                },
                SubtitleStreams = new List<SubtitleStream>
                {
                    new SubtitleStream
                    {
                        Language = "eng"
                    }
                }
            });

        // Sample file
        CreateVirtualFile(
            container,
            folder,
            "Sample.Movie.2023.Sample.mp4",
            1024 * 1024 * 50, // 50MB
            null);

        // Subtitle file
        CreateVirtualFile(
            container,
            folder,
            "Sample.Movie.2023.1080p.WEBDL.x264.srt",
            1024 * 25, // 25KB
            null);

        // NFO file
        CreateVirtualFile(
            container,
            folder,
            "Sample.Movie.2023.nfo",
            1024 * 5, // 5KB
            null);
    }

    private void CreateMockTvShowFiles(RemoteContainer container, MockVirtualFolder folder)
    {
        // Create season folder
        var season1Folder = CreateFolder($"{folder.GetFullPath()}\\Season 1");

        // Episode 1
        CreateVirtualFile(
            container,
            season1Folder,
            "Sample.TV.Show.S01E01.1080p.WEBDL.x264.mkv",
            1024 * 1024 * 500, // 500MB
            new MediaInfo
            {
                VideoStreams = new List<VideoStream>
                {
                    new VideoStream
                    {
                        Width = 1920,
                        Height = 1080,
                    }
                },
                AudioStreams = new List<AudioStream>
                {
                    new AudioStream
                    {
                        Channels = 6,
                        Language = "eng"
                    }
                }
            });

        // Episode 2
        CreateVirtualFile(
            container,
            season1Folder,
            "Sample.TV.Show.S01E02.1080p.WEBDL.x264.mkv",
            1024 * 1024 * 550, // 550MB
            new MediaInfo
            {
                VideoStreams = new List<VideoStream>
                {
                    new VideoStream
                    {
                        Width = 1920,
                        Height = 1080,
                    }
                },
                AudioStreams = new List<AudioStream>
                {
                    new AudioStream
                    {
                        Channels = 6,
                        Language = "eng"
                    }
                }
            });

        // Episode 3
        CreateVirtualFile(
            container,
            season1Folder,
            "Sample.TV.Show.S01E03.1080p.WEBDL.x264.mkv",
            1024 * 1024 * 520, // 520MB
            new MediaInfo
            {
                VideoStreams = new List<VideoStream>
                {
                    new VideoStream
                    {
                        Width = 1920,
                        Height = 1080,
                    }
                },
                AudioStreams = new List<AudioStream>
                {
                    new AudioStream
                    {
                        Channels = 6,
                        Language = "eng"
                    }
                }
            });

        // Subtitles folder
        var subtitlesFolder = CreateFolder($"{folder.GetFullPath()}\\Subtitles");

        // Subtitles for all episodes
        CreateVirtualFile(
            container,
            subtitlesFolder,
            "Sample.TV.Show.S01E01.1080p.WEBDL.x264.srt",
            1024 * 30, // 30KB
            null);

        CreateVirtualFile(
            container,
            subtitlesFolder,
            "Sample.TV.Show.S01E02.1080p.WEBDL.x264.srt",
            1024 * 32, // 32KB
            null);

        CreateVirtualFile(
            container,
            subtitlesFolder,
            "Sample.TV.Show.S01E03.1080p.WEBDL.x264.srt",
            1024 * 28, // 28KB
            null);
    }

    private void CreateMockMiscFiles(RemoteContainer container, MockVirtualFolder folder)
    {
        // PDF file
        CreateVirtualFile(
            container,
            folder,
            "Sample Document.pdf",
            1024 * 1024 * 5, // 5MB
            null);

        // Image file
        CreateVirtualFile(
            container,
            folder,
            "Sample Image.jpg",
            1024 * 500, // 500KB
            null);

        // Text file
        CreateVirtualFile(
            container,
            folder,
            "readme.txt",
            1024 * 2, // 2KB
            null);
    }

    private MockVirtualFolder CreateFolder(string path)
    {
        // Normalize path
        path = path.Replace('/', '\\');
        if (!path.StartsWith("\\"))
            path = "\\" + path;

        // Split path into components
        var parts = path.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);

        // Navigate to parent folder
        MockVirtualFolder currentFolder = _root;
        string currentPath = "\\";

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var folderName = part;

            // Check if folder already exists
            var existingFolder = currentFolder.Subfolders
                .FirstOrDefault(f => string.Equals(f.Name, folderName, StringComparison.OrdinalIgnoreCase));

            if (existingFolder != null)
            {
                currentFolder = (MockVirtualFolder)existingFolder;
            }
            else
            {
                // Create new folder
                var newFolder = new MockVirtualFolder(folderName, currentFolder);
                currentFolder.Subfolders.Add(newFolder);

                // Raise event
                var folderPath = i == 0 ? $"\\{folderName}" : $"{currentPath}\\{folderName}";
                FolderAdded?.Invoke(this, new VirtualFileSystemEventArgs(folderPath));

                currentFolder = newFolder;
            }

            currentPath = i == 0 ? $"\\{folderName}" : $"{currentPath}\\{folderName}";
        }

        return currentFolder;
    }

    private MockVirtualFile CreateVirtualFile(
        RemoteContainer container,
        MockVirtualFolder folder,
        string fileName,
        long fileSize,
        MediaInfo? mediaInfo)
    {
        var fileId = Guid.NewGuid().ToString("N");

        var remoteFile = new RemoteFile
        {
            FileId = _files.Count + 1,
            HostId = "mockhost",
            Size = fileSize,
            RestrictedLink = $"https://example.com/restricted/{fileId}",
            DownloadUrl = $"https://example.com/download/{fileId}",
            LocalPath = $"{folder.GetFullPath()}\\{fileName}",
            MediaInfo = mediaInfo,
            Parent = container
        };

        container.Files.Add(remoteFile);

        var virtualFile = new MockVirtualFile(fileName, folder, remoteFile);
        folder.Files.Add(virtualFile);

        // Raise event
        var filePath = $"{folder.GetFullPath()}\\{fileName}";
        FileAdded?.Invoke(this, new VirtualFileSystemEventArgs(filePath));

        return virtualFile;
    }

    public Task<byte[]> ReadFileContentAsync(string path, long offset, int length)
    {
        if (_files.TryGetValue(path, out var file))
        {
            return file.ReadFileContentAsync(offset, length);
        }

        return Task.FromResult(Array.Empty<byte>());
    }

    public void MoveFile(string sourcePath, string targetPath)
    {
        if (!_files.TryGetValue(sourcePath, out var file))
            return;

        var sourceFolder = file.Parent;
        if (sourceFolder == null)
            return;

        var fileName = Path.GetFileName(targetPath);
        var targetFolderPath = Path.GetDirectoryName(targetPath) ?? "\\";

        if (!_folders.TryGetValue(targetFolderPath, out var targetFolder))
            return;

        // Remove from source folder
        sourceFolder.Files.Remove(file);
        _files.Remove(sourcePath);

        // Update file properties
        file.Name = fileName;
        file.Parent = targetFolder;
        file.RemoteFile.LocalPath = targetPath;

        // Add to target folder
        targetFolder.Files.Add(file);
        _files[targetPath] = file;

        // Raise event
        FileMoved?.Invoke(this, new VirtualFileSystemMoveEventArgs(sourcePath, targetPath));
    }

    public void MoveFolder(string sourcePath, string targetPath)
    {
        if (!_folders.TryGetValue(sourcePath, out var folder))
            return;

        var sourceParent = folder.Parent;
        if (sourceParent == null)
            return;

        var folderName = Path.GetFileName(targetPath);
        var targetParentPath = Path.GetDirectoryName(targetPath) ?? "\\";

        if (!_folders.TryGetValue(targetParentPath, out var targetParent))
            return;

        // Remove from source parent
        sourceParent.Subfolders.Remove(folder);

        // Update folder and all children
        UpdateFolderPath(folder, folderName, targetParent, sourcePath, targetPath);

        // Add to target parent
        targetParent.Subfolders.Add(folder);

        // Raise event
        FolderMoved?.Invoke(this, new VirtualFileSystemMoveEventArgs(sourcePath, targetPath));
    }

    private void UpdateFolderPath(
        MockVirtualFolder folder,
        string newName,
        MockVirtualFolder newParent,
        string oldBasePath,
        string newBasePath)
    {
        // Update folder properties
        folder.Name = newName;
        folder.Parent = newParent;

        // Update folder in lookup
        _folders.Remove(oldBasePath);
        _folders[newBasePath] = folder;

        // Update all files in this folder
        foreach (var file in folder.Files.Cast<MockVirtualFile>())
        {
            var oldFilePath = $"{oldBasePath}\\{file.Name}";
            var newFilePath = $"{newBasePath}\\{file.Name}";

            _files.Remove(oldFilePath);
            file.RemoteFile.LocalPath = newFilePath;
            _files[newFilePath] = file;
        }

        // Recursively update subfolders
        foreach (var subfolder in folder.Subfolders.Cast<MockVirtualFolder>())
        {
            var oldSubfolderPath = $"{oldBasePath}\\{subfolder.Name}";
            var newSubfolderPath = $"{newBasePath}\\{subfolder.Name}";

            UpdateFolderPath(
                subfolder,
                subfolder.Name,
                folder,
                oldSubfolderPath,
                newSubfolderPath);
        }
    }

    public void DeleteFile(string path)
    {
        if (!_files.TryGetValue(path, out var file))
            return;

        var folder = file.Parent;
        if (folder == null)
            return;

        // Remove from folder
        folder.Files.Remove(file);
        _files.Remove(path);

        // Mark as deleted
        file.RemoteFile.DeletedLocally = true;

        // Raise event
        FileDeleted?.Invoke(this, new VirtualFileSystemEventArgs(path));
    }

    public void DeleteFolder(string path)
    {
        if (!_folders.TryGetValue(path, out var folder))
            return;

        var parent = folder.Parent;
        if (parent == null)
            return;

        // Remove from parent
        parent.Subfolders.Remove(folder);

        // Remove folder and all contents from lookup
        RemoveFolderFromLookup(folder, path);

        // Raise event
        FolderDeleted?.Invoke(this, new VirtualFileSystemEventArgs(path));
    }

    private void RemoveFolderFromLookup(MockVirtualFolder folder, string path)
    {
        // Remove folder from lookup
        _folders.Remove(path);

        // Remove all files in this folder
        foreach (var file in folder.Files)
        {
            var filePath = $"{path}\\{file.Name}";
            _files.Remove(filePath);
        }

        // Recursively remove subfolders
        foreach (var subfolder in folder.Subfolders)
        {
            var subfolderPath = $"{path}\\{subfolder.Name}";
            RemoveFolderFromLookup((MockVirtualFolder)subfolder, subfolderPath);
        }
    }

    public bool FileExists(string path)
    {
        try
        {
            // Normalize the path for comparison
            path = NormalizePath(path);

            // Check in our lookup dictionary
            bool exists = _files.ContainsKey(path);

            // For debugging
            if (exists)
            {
                Console.WriteLine($"FileExists: Found file in dictionary: {path}");
            }
            else
            {
                // Try to find it by navigating the structure
                string directoryPath = Path.GetDirectoryName(path) ?? "\\";
                string fileName = Path.GetFileName(path);

                // Find the parent folder
                if (_folders.TryGetValue(directoryPath, out var folder))
                {
                    // Look for the file in this folder
                    var file = folder.Files.FirstOrDefault(f =>
                        string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));

                    exists = file != null;
                    if (exists)
                    {
                        Console.WriteLine($"FileExists: Found file by navigation: {path}");

                        // Add to dictionary for future lookups
                        if (file is MockVirtualFile mockFile)
                        {
                            _files[path] = mockFile;
                        }
                    }
                }
            }

            return exists;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in FileExists: {ex.Message}");
            return false;
        }
    }

    public bool FolderExists(string path)
    {
        try
        {
            // Normalize the path for comparison
            path = NormalizePath(path);

            // Check in our lookup dictionary
            bool exists = _folders.ContainsKey(path);

            // For debugging
            if (exists)
            {
                Console.WriteLine($"FolderExists: Found folder in dictionary: {path}");
            }
            else if (path != "\\")
            {
                // Try to find it by navigating the structure
                string parentPath = Path.GetDirectoryName(path) ?? "\\";
                string folderName = Path.GetFileName(path);

                // Find the parent folder
                if (_folders.TryGetValue(parentPath, out var parentFolder))
                {
                    // Look for the subfolder in this folder
                    var subfolder = parentFolder.Subfolders.FirstOrDefault(f =>
                        string.Equals(f.Name, folderName, StringComparison.OrdinalIgnoreCase));

                    exists = subfolder != null;
                    if (exists)
                    {
                        Console.WriteLine($"FolderExists: Found folder by navigation: {path}");

                        // Add to dictionary for future lookups
                        if (subfolder is MockVirtualFolder mockFolder)
                        {
                            _folders[path] = mockFolder;
                        }
                    }
                }
            }

            return exists;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in FolderExists: {ex.Message}");
            return false;
        }
    }

    public IVirtualNode? FindNode(string path)
    {
        throw new NotImplementedException();
    }

    private string NormalizePath(string path)
    {
        // Ensure path starts with a backslash
        if (!path.StartsWith("\\"))
            path = "\\" + path;

        // Replace forward slashes with backslashes
        path = path.Replace('/', '\\');

        // Trim trailing backslash unless it's the root
        if (path.Length > 1 && path.EndsWith("\\"))
            path = path.TrimEnd('\\');

        return path;
    }
}

/// <summary>
/// Mock implementation of IVirtualFolder
/// </summary>
public class MockVirtualFolder : IVirtualFolder
{
    public string Name { get; set; }
    public IVirtualFolder? Parent { get; set; }
    public List<IVirtualFolder> Subfolders { get; } = new();
    public List<IVirtualFile> Files { get; } = new();
    private bool IsRoot { get; }

    public MockVirtualFolder(string name, IVirtualFolder? parent, bool isRoot = false)
    {
        Name = name;
        Parent = parent;
        IsRoot = isRoot;
    }

    public string GetFullPath()
    {
        if (IsRoot)
            return "\\";
            
        if (Parent == null || Parent.GetFullPath() == "\\")
            return $"\\{Name}";
            
        return $"{Parent.GetFullPath()}\\{Name}";
    }
}

/// <summary>
/// Mock implementation of IVirtualFile
/// </summary>
public class MockVirtualFile : IVirtualFile
{
    public string Name { get; set; }
    public IVirtualFolder? Parent { get; set; }
    public RemoteFile RemoteFile { get; }

    private byte[]? _mockContent;

    public MockVirtualFile(string name, IVirtualFolder? parent, RemoteFile remoteFile)
    {
        Name = name;
        Parent = parent;
        RemoteFile = remoteFile;
    }

    public string GetFullPath()
    {
        if (Parent == null)
            return $"\\{Name}";
            
        return $"{Parent.GetFullPath()}\\{Name}";
    }

    public Task<byte[]> ReadFileContentAsync(long offset, int length)
    {
        // Generate mock content on first access
        if (_mockContent == null)
        {
            // Generate deterministic content based on file name
            byte[] seed = Encoding.UTF8.GetBytes(Name);
            var random = new Random(BitConverter.ToInt32(seed, 0));
            
            // For small files, generate the whole file
            if (RemoteFile.Size < 1024 * 1024 * 10) // < 10MB
            {
                _mockContent = new byte[RemoteFile.Size];
                random.NextBytes(_mockContent);
            }
            else
            {
                // For large files, just generate the first 1MB
                // (in a real implementation, you would stream from the source)
                _mockContent = new byte[1024 * 1024];
                random.NextBytes(_mockContent);
            }
        }

        // Calculate how much to read
        if (offset >= _mockContent.Length)
            return Task.FromResult(Array.Empty<byte>());
            
        int bytesToRead = Math.Min(length, (int)(_mockContent.Length - offset));
        
        // Return the requested chunk
        var result = new byte[bytesToRead];
        Array.Copy(_mockContent, offset, result, 0, bytesToRead);
        
        return Task.FromResult(result);
    }
}
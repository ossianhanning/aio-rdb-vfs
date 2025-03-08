using Microsoft.Extensions.Logging;
using MediaCluster.Common;

namespace MediaCluster.MergedFileSystem.Test
{
    /// <summary>
    /// Helper class to diagnose issues with the virtual file system
    /// </summary>
    public class VirtualFileDiagnostics
    {
        private readonly ILogger _logger;
        private readonly IVirtualFileSystem _virtualFileSystem;

        public VirtualFileDiagnostics(ILogger logger, IVirtualFileSystem virtualFileSystem)
        {
            _logger = logger;
            _virtualFileSystem = virtualFileSystem;
        }

        /// <summary>
        /// Performs a diagnostic scan of the virtual file system and logs all files and folders
        /// </summary>
        public void RunDiagnostics()
        {
            _logger.LogInformation("=== VIRTUAL FILE SYSTEM DIAGNOSTICS ===");
            _logger.LogInformation("Starting diagnostic scan...");

            // Test if root is accessible
            _logger.LogInformation("Testing access to root folder");
            var root = _virtualFileSystem.Root;
            if (root == null)
            {
                _logger.LogError("ERROR: Root folder is null!");
                return;
            }

            _logger.LogInformation("Root folder is accessible");
            _logger.LogInformation($"Root contains {root.Subfolders.Count} subfolders and {root.Files.Count} files");

            // Log all files in root
            foreach (var file in root.Files)
            {
                _logger.LogInformation($"Root file: {file.Name}, Path: {file.GetFullPath()}, Size: {file.RemoteFile.Size}");
                TestFileAccess(file.GetFullPath());
            }

            // Log all subfolders
            var allFolders = new Dictionary<string, IVirtualFolder>(StringComparer.OrdinalIgnoreCase);
            var allFiles = new Dictionary<string, IVirtualFile>(StringComparer.OrdinalIgnoreCase);
            
            // Add root to folder list
            allFolders.Add("\\", root);
            
            // Recursively scan and log all folders and files
            ScanFolder("\\", root, allFolders, allFiles);
            
            // Test path normalization and lookup for some sample paths
            _logger.LogInformation("Testing path normalization and file lookup...");
            string[] testPaths = new[]
            {
                "\\readme.txt",
                "/readme.txt",
                "readme.txt",
                "\\downloads\\Sample Movie (2023)\\Sample.Movie.2023.1080p.WEBDL.x264.mkv",
                "\\Documents\\readme.txt",
                "\\downloads",
                "downloads"
            };
            
            foreach (var path in testPaths)
            {
                TestPathLookup(path);
            }
            
            // Log summary
            _logger.LogInformation("=== DIAGNOSTICS SUMMARY ===");
            _logger.LogInformation($"Total folders found: {allFolders.Count}");
            _logger.LogInformation($"Total files found: {allFiles.Count}");
            
            // Check for specific patterns
            var readmeFiles = allFiles.Keys.Where(k => k.EndsWith("readme.txt", StringComparison.OrdinalIgnoreCase)).ToList();
            _logger.LogInformation($"Found {readmeFiles.Count} readme.txt files:");
            foreach (var readme in readmeFiles)
            {
                _logger.LogInformation($"  {readme}");
            }
            
            _logger.LogInformation("=== END OF DIAGNOSTICS ===");
        }
        
        private void ScanFolder(string path, IVirtualFolder folder, 
            Dictionary<string, IVirtualFolder> allFolders, 
            Dictionary<string, IVirtualFile> allFiles)
        {
            // Log subfolders
            foreach (var subfolder in folder.Subfolders)
            {
                string subfolderPath = Path.Combine(path, subfolder.Name).Replace('/', '\\');
                _logger.LogInformation($"Found folder: {subfolderPath}");
                
                // Register this folder
                allFolders[subfolderPath] = subfolder;
                
                // Test if the folder exists via the API
                bool exists = _virtualFileSystem.FolderExists(subfolderPath);
                _logger.LogInformation($"  FolderExists(\"{subfolderPath}\") = {exists}");
                
                // Recursively scan subfolders
                ScanFolder(subfolderPath, subfolder, allFolders, allFiles);
            }
            
            // Log files
            foreach (var file in folder.Files)
            {
                string filePath = Path.Combine(path, file.Name).Replace('/', '\\');
                _logger.LogInformation($"Found file: {filePath}, Size: {file.RemoteFile.Size}");
                
                // Register this file
                allFiles[filePath] = file;
                
                // Test if the file exists via the API
                bool exists = _virtualFileSystem.FileExists(filePath);
                _logger.LogInformation($"  FileExists(\"{filePath}\") = {exists}");
                
                // Test file access
                TestFileAccess(filePath);
            }
        }
        
        private void TestPathLookup(string path)
        {
            _logger.LogInformation($"Testing path: \"{path}\"");
            
            // Test file exists
            bool fileExists = _virtualFileSystem.FileExists(path);
            _logger.LogInformation($"  FileExists(\"{path}\") = {fileExists}");
            
            // Test folder exists
            bool folderExists = _virtualFileSystem.FolderExists(path);
            _logger.LogInformation($"  FolderExists(\"{path}\") = {folderExists}");
            
            if (fileExists)
            {
                // Try reading from the file
                TestFileAccess(path);
            }
        }
        
        private void TestFileAccess(string path)
        {
            try
            {
                _logger.LogInformation($"Testing file access: {path}");
                var data = _virtualFileSystem.ReadFileContentAsync(path, 0, 100).Result;
                _logger.LogInformation($"  Successfully read {data.Length} bytes");
            }
            catch (Exception ex)
            {
                _logger.LogError($"  ERROR reading file: {ex.Message}");
            }
        }
    }
}
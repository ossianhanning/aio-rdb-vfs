using MediaCluster.Common;
using MediaCluster.Common.Models.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MediaCluster.RealDebrid;

namespace MediaCluster.MergedFileSystem.Test;

class Program
{
    // Default paths from your configuration
    private const string LOCAL_PATH = "D:\\dev\\rdvfs\\local";
    private const string MERGED_PATH = "D:\\dev\\rdvfs\\merged";
    private const string CACHE_PATH = "D:\\dev\\rdvfs\\cache";

    static async Task Main(string[] args)
    {
        Console.WriteLine("MediaCluster Merged File System Test");
        Console.WriteLine("====================================");

        // Create mock local files and folders
        CreateLocalFilesAndFolders();

        // Create and configure the host
        using var host = CreateHostBuilder(args).Build();

        // Get the TorrentInformationStore and set it to ready
        var torrentStore = host.Services.GetRequiredService<ITorrentInformationStore>();
        Console.WriteLine("Setting torrent store ready...");
        torrentStore.ReadySource.SetResult();
        
        // Get logger from DI
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<Program>();
        
        // Run diagnostics before mounting
        Console.WriteLine("\nRunning diagnostics on virtual file system...");
        var virtualFileSystem = host.Services.GetRequiredService<IVirtualFileSystem>();
        (virtualFileSystem as MockVirtualFileSystem)!.RepairRegistration();
        var diagnostics = new VirtualFileDiagnostics(logger, virtualFileSystem);
        diagnostics.RunDiagnostics();
        
        // Start the host
        await host.StartAsync();
        
        Console.WriteLine($"\nFile system successfully mounted at {MERGED_PATH}");
        Console.WriteLine("\nThe following mock structure has been created:");
        Console.WriteLine($"- Local files in: {LOCAL_PATH}");
        Console.WriteLine($"- Virtual files shown in: {MERGED_PATH}");
        Console.WriteLine($"- Combined view at: {MERGED_PATH}");
        Console.WriteLine("\nVirtual files include:");
        Console.WriteLine("- Movies in the downloads folder");
        Console.WriteLine("- TV shows with seasons and episodes");
        Console.WriteLine("- Various document files");
        Console.WriteLine("\nNote: Virtual files can be read but not modified.");
        Console.WriteLine("      Local files can be read, written, moved, etc.");
        
        Console.WriteLine("\nPress any key to exit and unmount the file system...");
        Console.ReadKey(true);
        
        // Stop the host
        await host.StopAsync();
        
        Console.WriteLine("File system unmounted. Exiting...");
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostContext, config) =>
            {
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string>("FileSystem:FileSystemLocalPath", LOCAL_PATH),
                    new KeyValuePair<string, string>("FileSystem:FileSystemMergedPath", MERGED_PATH),
                    new KeyValuePair<string, string>("FileSystem:CachePath", CACHE_PATH),
                    new KeyValuePair<string, string>("FileSystem:MaxCacheSizeMb", "10240"),
                    new KeyValuePair<string, string>("FileSystem:CachedChunksTtlMinutes", "60")
                });
            })
            .ConfigureServices((hostContext, services) =>
            {
                // Register configuration
                services.Configure<AppConfig>(hostContext.Configuration);
                
                // Register the virtual file system
                services.AddSingleton<IVirtualFileSystem, MockVirtualFileSystem>();
                
                services.AddSingleton<ITorrentInformationStore, MockTorrentInformationStore>();
                
                // Register the merged file system service
                services.AddMergedFileSystemService();
            })
            .ConfigureLogging((hostContext, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Debug);
            });

    static void CreateLocalFilesAndFolders()
    {
        Console.WriteLine("Creating local files and folders...");
        
        // Ensure the directories exist
        EnsureDirectoryExists(LOCAL_PATH);
        EnsureDirectoryExists(CACHE_PATH);
        
        // Make sure the merged path does NOT exist (it will be mounted)
        if (Directory.Exists(MERGED_PATH))
        {
            Console.WriteLine($"Removing existing merged path: {MERGED_PATH}");
            Directory.Delete(MERGED_PATH, true);
        }
        
        // Create some local folders
        string localDocumentsPath = Path.Combine(LOCAL_PATH, "Documents");
        string localPicturesPath = Path.Combine(LOCAL_PATH, "Pictures");
        string localMusicPath = Path.Combine(LOCAL_PATH, "Music");
        
        EnsureDirectoryExists(localDocumentsPath);
        EnsureDirectoryExists(localPicturesPath);
        EnsureDirectoryExists(localMusicPath);
        
        // Create sample text file in Documents
        string readmePath = Path.Combine(localDocumentsPath, "readme.txt");
        if (!File.Exists(readmePath))
        {
            File.WriteAllText(readmePath, @"# MediaCluster Test

This is a sample text file in the local Documents folder.

The merged file system shows both local files (like this one)
and virtual files from the RealDebrid service.

Local files:
- Can be read/written/modified
- Are stored on disk
- Take precedence over virtual files

Virtual files:
- Can only be read, not modified
- Are streamed from the remote service
- Only metadata is stored locally

You can navigate both types of files using normal file explorers and applications.");
        }
        
        // Create sample text file in root
        string notesPath = Path.Combine(LOCAL_PATH, "notes.txt");
        if (!File.Exists(notesPath))
        {
            File.WriteAllText(notesPath, @"This is a sample notes file in the root folder.

It demonstrates that files can be placed at any level in the hierarchy.

This file is stored locally on disk.");
        }
        
        // Create a root readme.txt as well for testing direct access
        string rootReadmePath = Path.Combine(LOCAL_PATH, "readme.txt");
        if (!File.Exists(rootReadmePath))
        {
            File.WriteAllText(rootReadmePath, @"This is a README file in the root folder.
It should be directly accessible from the merged file system.");
        }
        
        // Create a sample image file (just a text file with image extension)
        string imagePath = Path.Combine(localPicturesPath, "sample.jpg");
        if (!File.Exists(imagePath))
        {
            File.WriteAllText(imagePath, "This is not a real image, just a placeholder for testing.");
        }
        
        // Create a second image
        string image2Path = Path.Combine(localPicturesPath, "vacation.jpg");
        if (!File.Exists(image2Path))
        {
            File.WriteAllText(image2Path, "Another placeholder image for testing.");
        }
        
        // Create a music file (text again)
        string musicPath = Path.Combine(localMusicPath, "song.mp3");
        if (!File.Exists(musicPath))
        {
            File.WriteAllText(musicPath, "This is not a real MP3, just a placeholder for testing.");
        }
        
        // Create a subfolder in Music
        string playlistPath = Path.Combine(localMusicPath, "Playlists");
        EnsureDirectoryExists(playlistPath);
        
        // Create a playlist file
        string playlistFilePath = Path.Combine(playlistPath, "favorites.m3u");
        if (!File.Exists(playlistFilePath))
        {
            File.WriteAllText(playlistFilePath, @"#EXTM3U
#EXTINF:180,Sample Song 1
song1.mp3
#EXTINF:240,Sample Song 2
song2.mp3");
        }
        
        // Create a downloads folder (which will be merged with virtual downloads)
        string localDownloadsPath = Path.Combine(LOCAL_PATH, "downloads");
        EnsureDirectoryExists(localDownloadsPath);
        
        // Create a local file in downloads
        string localDownloadFilePath = Path.Combine(localDownloadsPath, "local-download.txt");
        if (!File.Exists(localDownloadFilePath))
        {
            File.WriteAllText(localDownloadFilePath, "This is a locally downloaded file (actually just a test file).");
        }
        
        Console.WriteLine("Local files and folders created successfully.\n");
    }

    static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Console.WriteLine($"Creating directory: {path}");
            Directory.CreateDirectory(path);
        }
    }
}
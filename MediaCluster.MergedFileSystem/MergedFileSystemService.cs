using Fsp;
using MediaCluster.Common;
using MediaCluster.Common.Models.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediaCluster.RealDebrid;

namespace MediaCluster.MergedFileSystem;

public class MergedFileSystemService(
    ILogger<MergedFileSystemService> logger,
    IOptions<AppConfig> appConfig,
    IVirtualFileSystem virtualFileSystem,
    ITorrentInformationStore torrentInformationStore)
    : IDisposable
{
    private readonly FileSystemConfig _config = appConfig.Value.FileSystem;
    private MergedFileSystem? _mergedFs;
    private FileSystemHost? _host;
    private CancellationTokenSource? _cts;
    private Task? _mountTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting merged file system service");

        // Validate paths
        ValidatePaths();

        // Ensure the local path exists
        if (!Directory.Exists(_config.FileSystemLocalPath))
        {
            logger.LogInformation("Creating local directory: {LocalPath}", _config.FileSystemLocalPath);
            Directory.CreateDirectory(_config.FileSystemLocalPath);
        }

        // Make sure merged path doesn't already exist
        if (Directory.Exists(_config.FileSystemMergedPath))
        {
            logger.LogError("Merged path already exists: {MergedPath}. This should be a mount point.", _config.FileSystemMergedPath);
            throw new InvalidOperationException($"Merged path already exists: {_config.FileSystemMergedPath}. This should be a mount point.");
        }

        // Wait for the virtual file system to be ready
        logger.LogInformation("Waiting for torrent information store to be ready");
        await torrentInformationStore.ReadySource.Task;

        // Create merged file system
        _mergedFs = new MergedFileSystem(
            logger,
            _config,
            virtualFileSystem);

        // Create file system host
        _host = new FileSystemHost(_mergedFs);

        // Set up cancellation token
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Mount file system
        _mountTask = Task.Run(() =>
        {
            try
            {
                logger.LogInformation("Mounting merged file system at {MountPoint}", _config.FileSystemMergedPath);

                if (0 > _host.Mount(_config.FileSystemMergedPath, null, true, 0))
                {
                    throw new IOException($"Cannot mount file system at {_config.FileSystemMergedPath}");
                }

                // Get the actual mount point after mounting
                var mountPoint = _host.MountPoint();
                
                logger.LogInformation("Merged file system successfully mounted at {MountPoint}", mountPoint);

                // Keep running until cancellation is requested
                _cts.Token.WaitHandle.WaitOne();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error mounting merged file system");
                throw;
            }
        }, _cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping merged file system service");

        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }

        if (_host != null)
        {
            logger.LogInformation("Unmounting merged file system");
            _host.Unmount();
            _host = null;
        }

        if (_mountTask != null)
        {
            try
            {
                await _mountTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error stopping merged file system");
            }
            finally
            {
                _mountTask = null;
            }
        }

        logger.LogInformation("Merged file system service stopped");
    }

    private void ValidatePaths()
    {
        // Check if FileSystemMergedPath is the same or below FileSystemLocalPath
        var localPath = Path.GetFullPath(_config.FileSystemLocalPath).TrimEnd(Path.DirectorySeparatorChar);
        var mergedPath = Path.GetFullPath(_config.FileSystemMergedPath).TrimEnd(Path.DirectorySeparatorChar);

        if (mergedPath.Equals(localPath, StringComparison.OrdinalIgnoreCase) ||
            mergedPath.StartsWith(localPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Merged path cannot be the same as or below local path");
            throw new InvalidOperationException("Merged path cannot be the same as or below local path");
        }

        // Check if CachePath exists
        if (!Directory.Exists(_config.CachePath))
        {
            logger.LogInformation("Creating cache directory: {CachePath}", _config.CachePath);
            Directory.CreateDirectory(_config.CachePath);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _host?.Unmount();
        _host = null;
        GC.SuppressFinalize(this);
    }
}
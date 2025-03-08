using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediaCluster.MergedFileSystem;

/// <summary>
/// Hosted service that manages the lifecycle of the MergedFileSystemService
/// </summary>
public class MergedFileSystemHostedService : IHostedService
{
    private readonly ILogger<MergedFileSystemHostedService> _logger;
    private readonly MergedFileSystemService _mergedFileSystemService;
    
    /// <summary>
    /// Creates a new instance of MergedFileSystemHostedService
    /// </summary>
    public MergedFileSystemHostedService(
        ILogger<MergedFileSystemHostedService> logger,
        MergedFileSystemService mergedFileSystemService)
    {
        _logger = logger;
        _mergedFileSystemService = mergedFileSystemService;
    }
    
    /// <summary>
    /// Starts the MergedFileSystemService
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting MergedFileSystemHostedService");
        
        try
        {
            await _mergedFileSystemService.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting MergedFileSystemService");
            throw;
        }
    }
    
    /// <summary>
    /// Stops the MergedFileSystemService
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping MergedFileSystemHostedService");
        
        try
        {
            await _mergedFileSystemService.StopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping MergedFileSystemService");
        }
    }
}
using Microsoft.Extensions.DependencyInjection;

namespace MediaCluster.MergedFileSystem;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMergedFileSystemService(this IServiceCollection services)
    {
        // Register the MergedFileSystemService as a singleton
        services.AddSingleton<MergedFileSystemService>();
        
        // Add a hosted service that will start/stop the MergedFileSystemService
        services.AddHostedService<MergedFileSystemHostedService>();
        
        return services;
    }
}
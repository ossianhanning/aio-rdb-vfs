using MediaCluster.CacheSystem;
using MediaCluster.Common;

namespace MediaCluster.RealDebrid;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRealDebridClient(this IServiceCollection services)
    {
        // Add cache system
        services.AddCacheSystem();
        
        services
            .AddSingleton(RealDebridClient.Initialize)
            .AddSingleton<RealDebridRepository>()
            .AddSingleton<TorrentInformationStore>()
            .AddSingleton<ITorrentInformationStore>(provider => provider.GetRequiredService<TorrentInformationStore>())
            .AddSingleton<VirtualFileSystem>()
            .AddSingleton<IVirtualFileSystem>(provider => provider.GetRequiredService<VirtualFileSystem>());
        
        services.AddHostedService<TorrentMonitorService>();
        services.AddHostedService<TorrentCleanupService>();
        
        return services;
    }
}
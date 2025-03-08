using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace MediaCluster.CacheSystem;

/// <summary>
/// Extension methods for adding cache services to the DI container
/// </summary>
public static class CacheServiceCollectionExtensions
{
    /// <summary>
    /// Add the simplified cache system to the service collection
    /// </summary>
    public static IServiceCollection AddCacheSystem(this IServiceCollection services)
    {
        // Configure optimal connection handling
        ConfigureServicePointManager();
        
        // Add HTTP client factory with optimized settings
        services.AddHttpClient("CacheClient", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.ConnectionClose = false; // Keep connections alive
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 20,
            EnableMultipleHttp2Connections = true,
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
        });
        
        // Register the simplified cache provider
        services.AddSingleton<SimplifiedCacheProvider>();
        services.AddSingleton<ICacheProvider>(provider => provider.GetRequiredService<SimplifiedCacheProvider>());
        
        return services;
    }
    
    /// <summary>
    /// Configure ServicePointManager for optimal connections
    /// </summary>
    private static void ConfigureServicePointManager()
    {
        // Increase connection limit globally
        ServicePointManager.DefaultConnectionLimit = 200;
        
        // Allow large numbers of connections to the same endpoint
        ServicePointManager.MaxServicePointIdleTime = 30000; // 30 seconds
        
        // Keep connections alive longer
        ServicePointManager.MaxServicePoints = 0; // Unlimited service points
        
        // Don't use 100-continue
        ServicePointManager.Expect100Continue = false;
        
        // Use strong TLS
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
        
        // Set DNS timeout
        ServicePointManager.DnsRefreshTimeout = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
    }
}
using MediaCluster.Common.Models.Configuration;
using MediaCluster.MediaAnalyzer;
using MediaCluster.MergedFileSystem;
using MediaCluster.QBittorrentApi;
using MediaCluster.RealDebrid;

namespace MediaCluster.Core
{
    /// <summary>
    /// Extension methods for registering MediaCluster services
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Add the core MediaCluster services to the service collection
        /// </summary>
        /// <param name="services">Service collection</param>
        /// <param name="configuration">Configuration</param>
        /// <returns>Service collection for chaining</returns>
        public static IServiceCollection AddMediaClusterCore(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Bind configuration
            services.Configure<AppConfig>(configuration);
            
            // Add health checks
            services.AddHealthChecks()
                .AddCheck<MediaClusterHealthCheck>("mediacluster_health");
            
            return services;
        }
        
        /// <summary>
        /// Add all MediaCluster services to the service collection
        /// </summary>
        /// <param name="services">Service collection</param>
        /// <param name="configuration">Configuration</param>
        /// <param name="options">Additional options</param>
        /// <returns>Service collection for chaining</returns>
        public static IServiceCollection AddMediaCluster(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<MediaClusterOptions> options = null)
        {
            // Configure options
            var mediaClusterOptions = new MediaClusterOptions();
            options?.Invoke(mediaClusterOptions);
            
            // Add core services
            services.AddMediaClusterCore(configuration);
            
            // Add component services
            services.AddRealDebridClient();
            services.AddMediaAnalyzerService();
            services.AddQBittorrentApiService();
            services.AddMergedFileSystemService();
            
            return services;
        }
    }
    
    /// <summary>
    /// Options for configuring MediaCluster
    /// </summary>
    public class MediaClusterOptions
    {
    }
}

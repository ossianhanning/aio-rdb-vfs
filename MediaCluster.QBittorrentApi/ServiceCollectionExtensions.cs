namespace MediaCluster.QBittorrentApi;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add the service to the service collection
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddQBittorrentApiService(this IServiceCollection services)
    {
        services.AddSingleton<IQBittorrentApiService, QBittorrentApiService>();
        return services;
    }
}
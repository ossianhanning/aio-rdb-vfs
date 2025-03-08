using MediaCluster.Common;
using Microsoft.Extensions.DependencyInjection;

namespace MediaCluster.MediaAnalyzer;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add the media analyzer service to the service collection
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddMediaAnalyzerService(this IServiceCollection services)
    {
        services.AddSingleton<IMediaAnalyzerService, MediaAnalyzerService>();
        return services;
    }
}
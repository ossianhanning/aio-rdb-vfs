using Microsoft.Extensions.Diagnostics.HealthChecks;
using MediaCluster.RealDebrid;

namespace MediaCluster.Core
{
    public class MediaClusterHealthCheck(
        TorrentInformationStore torrentStore)
        : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                torrentStore.GetAllTorrents();
                
                return HealthCheckResult.Healthy("MediaCluster is operational");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("MediaCluster health check failed", ex);
            }
        }
    }
}

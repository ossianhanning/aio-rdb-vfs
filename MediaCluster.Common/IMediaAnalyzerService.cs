using MediaCluster.MediaAnalyzer.Models;

namespace MediaCluster.Common
{
    public interface IMediaAnalyzerService
    {
        Task<MediaInfo> AnalyzeMediaAsync(byte[] fileContent, string fileName, CancellationToken cancellationToken = default);
        bool IsMediaFile(string fileName);
    }
}

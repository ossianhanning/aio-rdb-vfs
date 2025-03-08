using System.Diagnostics;
using System.Text;

namespace MediaCluster.RealDebrid
{
    /// <summary>
    /// HTTP message handler that logs detailed request/response information
    /// </summary>
    public class HttpLoggingHandler : DelegatingHandler
    {
        private readonly ILogger _logger;
        private readonly bool _includeHeaders;
        private readonly bool _includeBody;
        private readonly int _maxBodyLength;

        /// <summary>
        /// Creates a new HTTP logging handler
        /// </summary>
        /// <param name="innerHandler">The inner handler to delegate to</param>
        /// <param name="logger">Logger to use</param>
        /// <param name="includeHeaders">Whether to include headers in logs (may contain sensitive info)</param>
        /// <param name="includeBody">Whether to include request/response bodies in logs</param>
        /// <param name="maxBodyLength">Maximum body length to log</param>
        public HttpLoggingHandler(
            HttpMessageHandler innerHandler,
            ILogger logger,
            bool includeHeaders = false,
            bool includeBody = false,
            int maxBodyLength = 1024)
            : base(innerHandler)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _includeHeaders = includeHeaders;
            _includeBody = includeBody;
            _maxBodyLength = maxBodyLength;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid().ToString("N").Substring(0, 8);
            var stopwatch = Stopwatch.StartNew();

            _logger.LogDebug("[{RequestId}] HTTP Request: {Method} {Url}",
                id, request.Method, request.RequestUri);

            if (_includeHeaders && request.Headers.Any())
            {
                var headers = new StringBuilder();
                foreach (var header in request.Headers)
                {
                    // Don't log Authorization header values to avoid leaking credentials
                    if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        headers.AppendLine($"{header.Key}: [REDACTED]");
                    }
                    else
                    {
                        headers.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
                    }
                }
                _logger.LogTrace("[{RequestId}] HTTP Request Headers:\n{Headers}", id, headers);
            }

            if (_includeBody && request.Content != null)
            {
                var content = await request.Content.ReadAsStringAsync();
                if (content.Length > _maxBodyLength)
                {
                    content = content.Substring(0, _maxBodyLength) + "...";
                }

                _logger.LogTrace("[{RequestId}] HTTP Request Body:\n{Content}", id, content);
            }

            HttpResponseMessage response = null;
            string responseContent = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken);

                stopwatch.Stop();
                _logger.LogDebug("[{RequestId}] HTTP Response: {StatusCode} {ReasonPhrase} (took {ElapsedMs}ms)",
                    id, (int)response.StatusCode, response.ReasonPhrase, stopwatch.ElapsedMilliseconds);

                if (_includeHeaders && response.Headers.Any())
                {
                    var headers = new StringBuilder();
                    foreach (var header in response.Headers)
                    {
                        headers.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
                    }
                    _logger.LogTrace("[{RequestId}] HTTP Response Headers:\n{Headers}", id, headers);
                }

                if (_includeBody && response.Content != null)
                {
                    responseContent = await response.Content.ReadAsStringAsync();
                    if (responseContent.Length > _maxBodyLength)
                    {
                        responseContent = responseContent.Substring(0, _maxBodyLength) + "...";
                    }

                    _logger.LogTrace("[{RequestId}] HTTP Response Body:\n{Content}", id, responseContent);
                }

                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "[{RequestId}] HTTP Request failed after {ElapsedMs}ms: {ErrorMessage}",
                    id, stopwatch.ElapsedMilliseconds, ex.Message);
                throw;
            }
        }
    }
}
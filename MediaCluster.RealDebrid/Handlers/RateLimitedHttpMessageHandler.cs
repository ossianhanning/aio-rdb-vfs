namespace MediaCluster.RealDebrid
{
    /// <summary>
    /// HTTP message handler that applies rate limiting to all requests
    /// </summary>
    public class RateLimitedHttpMessageHandler : DelegatingHandler
    {
        private readonly RateLimiter _rateLimiter;

        /// <summary>
        /// Creates a new rate-limited HTTP handler with a custom rate limiter
        /// </summary>
        public RateLimitedHttpMessageHandler(HttpMessageHandler innerHandler, RateLimiter rateLimiter = null)
            : base(innerHandler)
        {
            _rateLimiter = rateLimiter ?? RateLimiter.Default;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Apply rate limiting before sending the request
            await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

            // Proceed with the actual request
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
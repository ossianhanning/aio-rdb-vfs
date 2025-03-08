using System.Collections.Concurrent;

namespace MediaCluster.RealDebrid
{
    /// <summary>
    /// Rate limiter that enforces a maximum number of operations per time window
    /// </summary>
    public class RateLimiter
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentQueue<DateTime> _requestTimestamps;
        private readonly TimeSpan _timeWindow;
        private readonly int _maxRequestsPerWindow;

        // Default static instance for application-wide limiting
        private static readonly Lazy<RateLimiter> _defaultInstance =
            new Lazy<RateLimiter>(() => new RateLimiter(200, TimeSpan.FromMinutes(1)));

        public static RateLimiter Default => _defaultInstance.Value;

        /// <summary>
        /// Creates a rate limiter that enforces a maximum number of operations per time window
        /// </summary>
        /// <param name="maxRequestsPerWindow">Maximum number of requests allowed in the time window</param>
        /// <param name="timeWindow">Time window for rate limiting</param>
        public RateLimiter(int maxRequestsPerWindow, TimeSpan timeWindow)
        {
            _maxRequestsPerWindow = maxRequestsPerWindow;
            _timeWindow = timeWindow;
            _requestTimestamps = new ConcurrentQueue<DateTime>();
            _semaphore = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// Waits until a request can be executed based on the rate limit
        /// </summary>
        public async Task WaitAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Remove timestamps that are outside the current time window
                DateTime cutoff = DateTime.UtcNow - _timeWindow;
                while (_requestTimestamps.TryPeek(out DateTime oldestRequest) && oldestRequest < cutoff)
                {
                    _requestTimestamps.TryDequeue(out _);
                }

                // If we've reached the maximum number of requests in the window, wait until oldest request expires
                if (_requestTimestamps.Count >= _maxRequestsPerWindow)
                {
                    if (_requestTimestamps.TryPeek(out DateTime oldestTimestamp))
                    {
                        DateTime nextAvailableTime = oldestTimestamp + _timeWindow;
                        TimeSpan waitTime = nextAvailableTime - DateTime.UtcNow;

                        if (waitTime > TimeSpan.Zero)
                        {
                            // Add small jitter to avoid thundering herd problem
                            double jitterMs = new Random().NextDouble() * 100;
                            waitTime = waitTime.Add(TimeSpan.FromMilliseconds(jitterMs));

                            await Task.Delay(waitTime, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                // Add current request timestamp
                _requestTimestamps.Enqueue(DateTime.UtcNow);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Executes an action within the rate limits
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
        {
            await WaitAsync(cancellationToken).ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }

        /// <summary>
        /// Executes an action within the rate limits
        /// </summary>
        public async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            await WaitAsync(cancellationToken).ConfigureAwait(false);
            await action().ConfigureAwait(false);
        }
    }
}
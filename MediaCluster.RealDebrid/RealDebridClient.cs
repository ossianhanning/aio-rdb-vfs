using System.Net.Http.Headers;
using System.Text.Json;
using MediaCluster.Common.Models.Configuration;
using MediaCluster.RealDebrid.Models.External;
using Microsoft.Extensions.Options;

namespace MediaCluster.RealDebrid;

public class RealDebridClient : IDisposable
{
    private static RealDebridClient? _instance;

    private readonly HttpClient _httpClient;
    private readonly ILogger<RealDebridClient> _logger;
    private const string BaseUrl = "https://api.real-debrid.com/rest/1.0/";

    private RealDebridClient(string accessToken, ILogger<RealDebridClient> logger, HttpClient httpClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        _logger.LogDebug("Initializing RealDebridClient with token: {TokenPreview}",
            !string.IsNullOrEmpty(accessToken)
                ? $"{accessToken[..Math.Min(5, accessToken.Length)]}..."
                : "null");

        _logger.LogInformation("RealDebridClient initialized with base URL: {BaseUrl}", BaseUrl);
    }
    
    public static RealDebridClient Initialize(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<AppConfig>>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<RealDebridClient>();
        
        string accessToken = options.Value.RealDebrid.ApiKey;
        int requestsPerMinute = options.Value.RealDebrid.RequestsPerMinute;
        
        if (string.IsNullOrEmpty(accessToken))
            throw new ArgumentException("Access token cannot be null or empty", nameof(accessToken));

        var httpClient = CreateHttpClient(accessToken, requestsPerMinute, loggerFactory);
        
        var client = new RealDebridClient(accessToken, logger, httpClient);

        return client;
    }

    // Internal helper method to create the HttpClient with proper handlers
    private static HttpClient CreateHttpClient(string accessToken, int requestsPerMinute, ILoggerFactory? loggerFactory)
    {
        // Create handler chain
        HttpMessageHandler innerHandler = new HttpClientHandler();
        
        // Add HTTP logging if we have a logger factory
        if (loggerFactory != null)
        {
            var httpLogger = loggerFactory.CreateLogger("RealDebrid.HttpClient");
            innerHandler = new HttpLoggingHandler(
                innerHandler, 
                httpLogger,
                // Only log headers and bodies in Trace level
                includeHeaders: true,
                includeBody: true);
        }
        
        // Add rate limiting if requested
        if (requestsPerMinute > 0)
        {
            innerHandler = new RateLimitedHttpMessageHandler(
                innerHandler,
                new RateLimiter(requestsPerMinute, TimeSpan.FromMinutes(1)));
        }
        
        // Create the HTTP client
        var httpClient = new HttpClient(innerHandler)
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestHeaders = { 
                Authorization = 
                    new AuthenticationHeaderValue("Bearer", accessToken)
            }
        };
        
        return httpClient;
    }

    /// <summary>
    /// Get current user info
    /// </summary>
     internal async Task<UserInfoDto> GetUserInfoAsync()
    {
        return await _httpClient.GetFromJsonAsync<UserInfoDto>("user");
    }

    /// <summary>
    /// Check if a file is downloadable
    /// </summary>
    /// <param name="link">The original hoster link</param>
    /// <param name="password">Password to unlock the file access hoster side</param>
    internal async Task<LinkCheckResponseDto> CheckLinkAsync(string link, string password = null)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "link", link },
            { "password", password }
        }.Where(kv => kv.Value != null));

        var response = await _httpClient.PostAsync("unrestrict/check", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LinkCheckResponseDto>();
    }

    /// <summary>
    /// Unrestrict a hoster link and get a new unrestricted link
    /// </summary>
    /// <param name="link">The original hoster link</param>
    /// <param name="password">Password to unlock the file access hoster side</param>
    /// <param name="remote">Use Remote traffic, dedicated servers and account sharing protections lifted</param>
    internal async Task<UnrestrictLinkResponseDto> UnrestrictLinkAsync(string link, string password = null,
        bool remote = false)
    {
        var formData = new Dictionary<string, string>
        {
            { "link", link }
        };

        if (password != null)
            formData.Add("password", password);

        if (remote)
            formData.Add("remote", "1");

        var content = new FormUrlEncodedContent(formData);

        var response = await _httpClient.PostAsync("unrestrict/link", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UnrestrictLinkResponseDto>();
    }

    /// <summary>
    /// Get user torrents list
    /// </summary>
    /// <param name="offset">Starting offset</param>
    /// <param name="page">Pagination system</param>
    /// <param name="limit">Entries returned per page / request (must be within 0 and 5000, default: 100)</param>
    /// <param name="filter">Filter: "active" to list active torrents only</param>
    internal async Task<List<TorrentItemDto>> GetTorrentsAsync(int? offset = 0, int? page = 1, int? limit = 100,
        string filter = "")
    {
        var query = new List<string>();

        if (offset.HasValue)
            query.Add($"offset={offset}");

        if (page.HasValue)
            query.Add($"page={page}");

        if (limit.HasValue)
            query.Add($"limit={limit}");

        query.Add($"filter={filter}");

        var queryString = query.Count > 0 ? $"?{string.Join("&", query)}" : "";

        var response = await _httpClient.GetAsync($"torrents{queryString}");
        var responseString = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<List<TorrentItemDto>>(responseString);
        return result;
    }
    
    /// <summary>
    /// Get info on a specific torrent
    /// </summary>
    /// <param name="id">Torrent ID</param>
    internal async Task<TorrentInfoDto> GetTorrentInfoAsync(string id)
    {
        return await _httpClient.GetFromJsonAsync<TorrentInfoDto>($"torrents/info/{id}");
    }

    /// <summary>
    /// Add a torrent file to download
    /// </summary>
    /// <param name="torrentFileContent">Content of the torrent file</param>
    /// <param name="host">Hoster domain</param>
    /// <param name="category"></param>
    /// <param name="tags"></param>
    internal async Task<TorrentAddResponseDto> AddTorrentAsync(byte[] torrentFileContent,
        string host = "real-debrid.com", string? category = null, List<string>? tags = null)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(torrentFileContent), "file", "file.torrent");

        if (!string.IsNullOrEmpty(host))
            content.Add(new StringContent(host), "host");

        var response = await _httpClient.PutAsync("torrents/addTorrent", content);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TorrentAddResponseDto>();

        if (result == null)
        {
            throw new InvalidOperationException("Failed to add torrent");
        }

        return result;
    }

    /// <summary>
    /// Add a magnet link to download
    /// </summary>
    /// <param name="magnetLink">Magnet link</param>
    /// <param name="host">Hoster domain</param>
    internal async Task<TorrentAddResponseDto> AddMagnetAsync(string magnetLink, string host = "real-debrid.com")
    {
        var formData = new Dictionary<string, string>
        {
            { "magnet", magnetLink }
        };

        if (!string.IsNullOrEmpty(host))
            formData.Add("host", host);

        var content = new FormUrlEncodedContent(formData);

        var response = await _httpClient.PostAsync("torrents/addMagnet", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TorrentAddResponseDto>();
    }

    /// <summary>
    /// Select files of a torrent to start it
    /// </summary>
    /// <param name="id">Torrent ID</param>
    /// <param name="files">Selected files IDs (comma separated) or "all"</param>
    internal async Task SelectTorrentFilesAsync(string id, string files)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "files", files }
        });

        await _httpClient.PostAsync($"torrents/selectFiles/{id}", content);
    }

    /// <summary>
    /// Delete a torrent from torrents list
    /// </summary>
    /// <param name="id">Torrent ID to delete</param>
    internal async Task DeleteTorrentAsync(string id)
    {
        await _httpClient.DeleteAsync($"torrents/delete/{id}");
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}

// Extension method for initializing the client
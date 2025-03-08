using MediaCluster.QBittorrentApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace MediaCluster.QBittorrentApi
{
    /// <summary>
    /// Controller that implements QBittorrent's API endpoints that Sonarr/Radarr use
    /// </summary>
    [ApiController]
    [Route("api/v2")]
    public class QBitTorrentController(
        IQBittorrentApiService qBittorrentApiService,
        ILogger<QBitTorrentController> logger)
        : ControllerBase
    {
        /// <summary>
        /// Get WebAPI version
        /// </summary>
        [HttpGet("app/webapiVersion")]
        public ActionResult<string> GetApiVersion()
        {
            return qBittorrentApiService.GetApiVersion();
        }
        
        /// <summary>
        /// Login to WebAPI
        /// </summary>
        /// <remarks>
        /// QBittorrent requires authentication, but we'll accept any credentials for compatibility
        /// </remarks>
        [HttpPost("auth/login")]
        public IActionResult Login([FromForm] string username, [FromForm] string password)
        {
            logger.LogInformation("Login attempt for user: {Username}", username);
            
            // Set a dummy cookie
            Response.Cookies.Append("SID", Guid.NewGuid().ToString(), new CookieOptions
            {
                HttpOnly = true,
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });
            
            return Ok("Ok.");
        }
        
        /// <summary>
        /// Logout from WebAPI
        /// </summary>
        [HttpPost("auth/logout")]
        public IActionResult Logout()
        {
            // Clear the cookie
            Response.Cookies.Delete("SID");
            
            return Ok();
        }
        
        /// <summary>
        /// Get torrent list
        /// </summary>
        [HttpGet("torrents/info")]
        public async Task<ActionResult<IEnumerable<TorrentInfoDto>>> GetTorrents(
            [FromQuery] string filter = "",
            [FromQuery] string category = "",
            [FromQuery] string sort = "",
            [FromQuery] bool reverse = false,
            [FromQuery] int limit = 10000,
            [FromQuery] int offset = 0,
            [FromQuery] string hashes = "",
            CancellationToken cancellationToken = default)
        {
            logger.LogDebug("GetTorrents: filter={Filter}, category={Category}, sort={Sort}, reverse={Reverse}, limit={Limit}, offset={Offset}, hashes={Hashes}",
                filter, category, sort, reverse, limit, offset, hashes);
                
            try
            {
                var torrents = await qBittorrentApiService.GetTorrentsAsync(
                    filter, category, sort, reverse, limit, offset, hashes, cancellationToken);
                    
                return Ok(torrents);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in GetTorrents: {Message}", ex.Message);
                return StatusCode(500, "Internal Server Error");
            }
        }
        
        /// <summary>
        /// Get torrent generic properties
        /// </summary>
        [HttpGet("torrents/properties")]
        public async Task<ActionResult<TorrentPropertiesDto>> GetTorrentProperties(
            [FromQuery] string hash,
            CancellationToken cancellationToken = default)
        {
            logger.LogDebug("GetTorrentProperties: hash={Hash}", hash);
            
            try
            {
                var properties = await qBittorrentApiService.GetTorrentPropertiesAsync(hash, cancellationToken);
                return Ok(properties);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in GetTorrentProperties: {Message}", ex.Message);
                return StatusCode(500, "Internal Server Error");
            }
        }
        
        /// <summary>
        /// Get torrent contents
        /// </summary>
        [HttpGet("torrents/files")]
        public async Task<ActionResult<IEnumerable<FileInfoDto>>> GetTorrentFiles(
            [FromQuery] string hash,
            CancellationToken cancellationToken = default)
        {
            logger.LogDebug("GetTorrentFiles: hash={Hash}", hash);
            
            try
            {
                var files = await qBittorrentApiService.GetTorrentFilesAsync(hash, cancellationToken);
                return Ok(files);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in GetTorrentFiles: {Message}", ex.Message);
                return StatusCode(500, "Internal Server Error");
            }
        }
        
        /// <summary>
        /// Add new torrent
        /// </summary>
        [HttpPost("torrents/add")]
        [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = long.MaxValue)]
        [RequestSizeLimit(long.MaxValue)]
        public async Task<IActionResult> AddTorrent(
            CancellationToken cancellationToken = default)
        {
            logger.LogInformation("AddTorrent request received");
            
            try
            {
                var form = await Request.ReadFormAsync(cancellationToken);
                
                // Get form data
                string urls = form["urls"].ToString();
                string savePath = form["savepath"].ToString();
                string cookie = form["cookie"].ToString();
                string category = form["category"].ToString();
                string tags = form["tags"].ToString();
                bool skipChecking = form["skip_checking"] == "true";
                bool paused = form["paused"] == "true";
                bool autoTMM = form["autoTMM"] == "true";
                bool sequentialDownload = form["sequentialDownload"] == "true";
                bool firstLastPiecePrio = form["firstLastPiecePrio"] == "true";
                
                // Get uploaded files
                var torrentStreams = new List<Stream>();
                foreach (var file in form.Files)
                {
                    if (file.Length > 0)
                    {
                        torrentStreams.Add(file.OpenReadStream());
                    }
                }
                
                // Add the torrent
                bool success = await qBittorrentApiService.AddTorrentAsync(
                    torrentStreams,
                    urls,
                    savePath,
                    cookie,
                    category,
                    tags,
                    skipChecking,
                    paused,
                    false,
                    false,
                    sequentialDownload,
                    firstLastPiecePrio,
                    null,
                    null,
                    null,
                    cancellationToken);
                    
                // Clean up streams
                foreach (var stream in torrentStreams)
                {
                    await stream.DisposeAsync();
                }
                
                if (success)
                {
                    return Ok();
                }
                else
                {
                    return BadRequest("Failed to add torrent");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in AddTorrent: {Message}", ex.Message);
                return StatusCode(500, "Internal Server Error");
            }
        }
        
        /// <summary>
        /// Delete torrents
        /// </summary>
        [HttpPost("torrents/delete")]
        public async Task<IActionResult> DeleteTorrents(
            [FromForm] string hashes,
            [FromForm] bool deleteFiles = false,
            CancellationToken cancellationToken = default)
        {
            logger.LogInformation("DeleteTorrents: hashes={Hashes}, deleteFiles={DeleteFiles}", hashes, deleteFiles);
            
            try
            {
                bool success = await qBittorrentApiService.DeleteTorrentsAsync(hashes, deleteFiles, cancellationToken);
                
                if (success)
                {
                    return Ok();
                }
                else
                {
                    return BadRequest("Failed to delete torrents");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in DeleteTorrents: {Message}", ex.Message);
                return StatusCode(500, "Internal Server Error");
            }
        }
        
        /// <summary>
        /// Get application preferences
        /// </summary>
        [HttpGet("app/preferences")]
        public async Task<ActionResult<PreferencesDto>> GetPreferences(
            CancellationToken cancellationToken = default)
        {
            logger.LogDebug("GetPreferences");
            
            try
            {
                var preferences = await qBittorrentApiService.GetPreferencesAsync(cancellationToken);
                return Ok(preferences);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in GetPreferences: {Message}", ex.Message);
                return StatusCode(500, "Internal Server Error");
            }
        }
        
        /// <summary>
        /// Get torrent trackers
        /// </summary>
        [HttpGet("torrents/trackers")]
        public async Task<ActionResult<IEnumerable<TrackerDto>>> GetTrackers(
            [FromQuery] string hash,
            CancellationToken cancellationToken = default)
        {
            logger.LogDebug("GetTrackers: hash={Hash}", hash);
            
            try
            {
                var trackers = await qBittorrentApiService.GetTrackersAsync(hash, cancellationToken);
                return Ok(trackers);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in GetTrackers: {Message}", ex.Message);
                return StatusCode(500, "Internal Server Error");
            }
        }
        
        /// <summary>
        /// Get all categories
        /// </summary>
        [HttpGet("torrents/categories")]
        public async Task<ActionResult<Dictionary<string, CategoryDto>>> GetCategories(
            CancellationToken cancellationToken = default)
        {
            logger.LogDebug("GetCategories");
            
            try
            {
                var categories = await qBittorrentApiService.GetCategoriesAsync(cancellationToken);
                return Ok(categories);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in GetCategories: {Message}", ex.Message);
                return StatusCode(500, "Internal Server Error");
            }
        }
        
        /// <summary>
        /// Add new category
        /// </summary>
        [HttpPost("torrents/createCategory")]
        public async Task<IActionResult> CreateCategory(
            [FromForm] string category,
            [FromForm] string savePath = "",
            CancellationToken cancellationToken = default)
        {
            logger.LogInformation("CreateCategory: category={Category}, savePath={SavePath}", category, savePath);
            
            try
            {
                bool success = await qBittorrentApiService.CreateCategoryAsync(category, savePath, cancellationToken);
                
                if (success)
                {
                    return Ok();
                }
                else
                {
                    return BadRequest("Category already exists");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in CreateCategory: {Message}", ex.Message);
                return StatusCode(500, "Internal Server Error");
            }
        }
        
        /// <summary>
        /// Get application version
        /// </summary>
        [HttpGet("app/version")]
        public ActionResult<string> GetAppVersion()
        {
            return "v4.3.9"; // Match the API version
        }
        
        /// <summary>
        /// Get build info
        /// </summary>
        [HttpGet("app/buildInfo")]
        public ActionResult<object> GetBuildInfo()
        {
            return new
            {
                qt = "5.15.2",
                libtorrent = "1.2.14.0",
                boost = "1.76.0",
                openssl = "1.1.1k",
                zlib = "1.2.11",
                bitness = 64
            };
        }
    }
}
# MediaCluster

MediaCluster is a integrated solution that bridges RealDebrid with Sonarr/Radarr by providing a QBittorrent-compatible API while also exposing downloaded content through a virtual file system.

Note that this is a work-in-progress. Only really important missing feature I need to add is symlink support (for Radarr and Sonarr.)

This solution specifically targets Windows and Real-Debrid currently, but expanding it to other providers and platforms is very possible.

This is not finished and may never be, at least until WinFSP adds support for hard links. (Alternatively, I will fork radarr/sonarr.)

## Features

- **QBittorrent API Emulation**: Provides a QBittorrent-compatible API that Sonarr and Radarr can connect to.
- **RealDebrid Integration**: Handles torrent downloads through RealDebrid's cloud service.
- **Virtual File System**: Exposes RealDebrid content as a local file system using WinFSP.
- **Media Analysis**: Analyzes media files to extract metadata using FFProbe.
- **Efficient File Access**: Implements on-demand file access with caching to minimize bandwidth usage.

## Architecture

1. **Core**: Shared models, interfaces, and event system
2. **RealDebrid**: Client for interacting with the RealDebrid API
3. **QBittorrentApi**: QBittorrent API emulator for Sonarr/Radarr integration
4. **VirtualFileSystem**: Virtual file system implementation for accessing RealDebrid content
5. **MediaAnalyzer**: Media file analysis using FFProbe

## Getting Started

### Prerequisites

- .NET 9.0 SDK or later
- WinFSP (for virtual file system functionality)
- FFmpeg/FFprobe (for media analysis)
- RealDebrid premium account

### Connecting Sonarr/Radarr

1. In Sonarr/Radarr, go to Settings > Download Clients
2. Add a new download client of type "qBittorrent"
3. Set the host to your MediaCluster instance (e.g., `localhost`)
4. Set the port to the configured port (default: 8080)
5. Username and password can be any values (authentication is bypassed)
6. Test the connection and save if successful

### Configuration

A configuration file with placeholder values will be generated on first startup.

## Implementation Details


### Virtual File System

The virtual file system provides a seamless interface to RealDebrid content:

- Files are downloaded on-demand, only the requested chunks
- Frequently accessed chunks are cached locally
- File operations are mapped to RealDebrid API calls

### QBittorrent API Emulation

MediaCluster implements the essential QBittorrent API endpoints required by Sonarr/Radarr:

- `/api/v2/app/webapiVersion`: Returns the API version
- `/api/v2/auth/login`: Authentication endpoint (accepts any credentials)
- `/api/v2/torrents/info`: List torrents
- `/api/v2/torrents/properties`: Get torrent properties
- `/api/v2/torrents/files`: Get files in a torrent
- `/api/v2/torrents/add`: Add new torrents
- `/api/v2/torrents/delete`: Delete torrents

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- [RealDebrid](https://real-debrid.com/) for their cloud downloading service
- [QBittorrent](https://www.qbittorrent.org/) for their API design
- [WinFSP](https://github.com/billziss-gh/winfsp) for the file system in userspace functionality
- [FFmpeg](https://ffmpeg.org/) for media analysis capabilities

AniLiberty STRM Docker Playback Rail Fix

This update tightens the Docker playback path for AniLiberty STRM Plugin and documents the supported container workflow: repository manifest install, container-visible output path, scheduled reconstruction, Jellyfin library scan, and proxied HLS playback.

### Docker Playback Proxy Rail
- Fixed a Docker/headless Jellyfin case where `ReverseVirtualPath` could resolve the playback proxy route as `file:///AniLibertyPlayback/hls`.
- Generated `.strm` files now keep the Playback Proxy Rail on HTTP/HTTPS instead of writing unusable `file://` proxy URLs.
- Added proxy endpoint tests that preserve valid absolute HTTP routes and normalize non-HTTP absolute routes back into Jellyfin route paths.

### Docker First Launch Flight Check
- Added a README path contract for Docker: plugin output path and Jellyfin media library path must use the same container-visible path.
- Documented a working volume pattern such as `/srv/aniliberty-strm:/media/aniliberty-strm`.
- Added a first-run sequence for full-catalog generation, favorites generation, scheduled tasks, and empty-output diagnostics.

### Tested Environment
- Verified manifest-based installation in Docker using `jellyfin/jellyfin:10.11.0`.
- Smoke-tested on Windows 10 Pro with Docker Desktop 4.51.0, Docker Engine 28.5.2, and Docker Compose 2.40.3.
- Confirmed Jellyfin scans generated output as a TV library, recognizes generated series and episodes, returns item-level playback info, rewrites HLS playlists through the proxy, and streams HLS segments through the plugin route.

### Not Included
- No Jellyfin 10.10 compatibility build.
- No change to AniLiberty API endpoint hosts.
- No change to the legacy repository URL used for the plugin manifest.

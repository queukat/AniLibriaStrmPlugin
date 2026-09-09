# Changelog

All notable changes to this plugin are documented in this file.

## v2.0.0.14 - 2026-09-09

AniLiberty STRM 2.0.0.14 — Jellyfin 10.11

### Changes

- Reduced unnecessary artwork downloads during library updates.
- Reduced background processing for intro and outro skipping.
- Fixed a problem that could remove generated library files when AniLiberty returned incomplete data.

### Upgrade Notes

Restart Jellyfin after updating. No settings changes or library regeneration are required.

## v2.0.0.13 - 2026-08-18
AniLiberty STRM Performance Update

This update makes library generation and Skip Intro/Outro processing faster and lighter on Jellyfin, especially for larger libraries.

### Performance Improvements
- Faster full-catalog and Favorites library generation.
- Reduced CPU and memory usage during generation and library updates.
- Faster preparation of native Skip Intro/Outro data after Jellyfin starts or scans a library.
- Improved performance during repeated scheduled runs and when multiple library locations are configured.
- Reduced memory usage while processing artwork and plugin-managed library data.

### Upgrade Notes
- Restart Jellyfin after installing the update.
- No settings changes or library regeneration are required.
- GitHub Release: https://github.com/queukat/AniLibriaStrmPlugin/releases/tag/v2.0.0.13
- Compare: https://github.com/queukat/AniLibriaStrmPlugin/compare/v2.0.0.12...v2.0.0.13

## v2.0.0.12 - 2026-07-19
AniLiberty STRM Native Media Segments & Docker Playback Hardening

This release improves Jellyfin-native skip controls, managed-library refresh behavior, authentication visibility, and Docker playback safety. It also expands automated coverage and adds formatting/analyzer checks to both stable and nightly release pipelines.

### Licensing and Branding
- The repository is now licensed for non-commercial use under the **PolyForm Noncommercial License 1.0.0**.
- Added explicit commercial licensing, trademark, and required-notice documents.
- Commercial use requires separate permission from the project owner; the AniLiberty STRM name and branding are not granted for unrestricted reuse.

### Docker Direct Play Guard
- Fixed automatic playback proxy URL selection inside Docker and other containers.
- The plugin no longer writes container-only bridge addresses such as `172.17.x.x` into generated `.strm` files.
- Proxy base URL priority is now: explicit **Jellyfin Playback Proxy Base URL**, `JELLYFIN_PublishedServerUrl`, then native-host auto-detection outside containers.
- When no client-reachable HTTP(S) base URL exists in a container, generation safely falls back to direct AniLiberty HLS URLs and records a clear task warning.
- Invalid manual or published proxy URLs are ignored instead of producing broken playback links.

### Native Skip Intro / Outro
- Replaced generated EDL and chapter XML skip files with a Jellyfin `IMediaSegmentProvider` implementation.
- Opening and ending timings are stored in `.aniliberty-strm-plugin/media-segments.json` and exposed as native Jellyfin media segments.
- Added background warm-up and path-to-episode reconciliation so existing library items receive updated skip timings without rewriting user-authored metadata.

### Managed Mirror and Metadata Refresh
- Expanded managed manifest tracking for STRM, NFO, artwork, popularity metadata, and media-segment state.
- Generated metadata is refreshed when the upstream AniLiberty signal changes while unmarked user-authored files remain protected.
- Cleanup remains bounded to plugin-managed files, with `Keep`, `DryRun`, and opt-in `Delete` behavior.
- Improved specials, franchise-season, fractional-ordinal, hydration, and image-handling paths.

### Popularity and Authentication Visibility
- Added an optional AniLiberty popularity badge to Jellyfin Web item pages.
- Added a public, read-only metadata endpoint backed only by plugin-generated popularity sidecars.
- Added Jellyfin activity notifications when a stored AniLiberty token starts returning HTTP 401/403, with cooldown protection against notification spam.
- Improved login/password, OTP, favorites, and watch-timecode API handling and validation.

### Playback and Sync Reliability
- Kept HLS playlist and segment delivery streaming through Jellyfin without buffering complete media objects in memory.
- Improved STRM URL normalization, playlist route handling, redirect validation, and playback diagnostics.
- Improved per-session watch-progress coordination, cancellation, path resolution, and forward-only progress updates.

### Quality Gates and Verification
- Added `dotnet format --verify-no-changes` to stable and nightly workflows, covering formatting and built-in Roslyn analyzer diagnostics without an extra linter dependency.
- Expanded automated coverage across controllers, API clients, scheduled tasks, managed cleanup, native media segments, popularity injection, authentication notifications, and Docker proxy URL selection.
- Full release-gated result: **207/207 non-integration tests passed**, clean Debug and Release builds, and zero compiler warnings. Three opt-in live AniLiberty API checks remain outside the release gate.
- Reproduced the Docker failure on Jellyfin 10.11.11: an internal bridge URL caused `DirectPlayError` and ffmpeg fallback; a client-reachable proxy URL played the same H.264/AAC stream through Direct Play with no transcoding.

### Upgrade Notes
- Restart Jellyfin after installing the update.
- Run **Generate AniLiberty STRM library** again so existing `.strm` files and managed state receive the new routing and metadata behavior.
- Docker users who want Jellyfin-proxied playback should set **Jellyfin Playback Proxy Base URL** or `JELLYFIN_PublishedServerUrl` to an address reachable by playback devices and the Jellyfin container.

### Not Included
- No Jellyfin 10.10 compatibility build.
- No full-segment server-side cache or mid-segment resume; brief client-side HLS buffering remains the current protection against connection jitter.
- No change to AniLiberty API endpoint hosts or the legacy GitHub Pages repository URL.
- No season-gap fix: the reported missing-season case was not established as a plugin defect in this work.
- GitHub Release: https://github.com/queukat/AniLibriaStrmPlugin/releases/tag/v2.0.0.12
- Compare: https://github.com/queukat/AniLibriaStrmPlugin/compare/v2.0.0.11...v2.0.0.12

## v2.0.0.11 - 2026-05-10
AniLiberty STRM Support Trace & Docker Preflight

This update adds a safer diagnostics flow for Docker and headless Jellyfin setups: compact logs stay readable, while the Support Trace rail captures the full failure context needed for issue triage.

### Diagnostic Command Center
- Added a **Huge trace** switch for explicit support sessions.
- `Show logs` now remains the compact operational view for normal task results.
- `Show support trace` exposes the high-volume diagnostic stream only after Huge trace is enabled and the task is rerun.
- `Copy support bundle` packages sanitized configuration, compact logs, and captured support trace without exposing AniLiberty tokens.

### Full Exception Visibility
- Support trace now records full exception details, including stack traces and inner exceptions.
- Compact logs keep short, readable error summaries so normal UI logging does not turn into a wall of text.
- Verified with a read-only Docker output path: the trace captures the filesystem failure, plugin preflight stack, task failure stack, and remediation hint.

### Docker Output Flight Check
- Scheduled generation now validates that the configured output root is writable before fetching AniLiberty catalog data.
- Permission and path failures now stop early with a clear message about Docker volume mapping, host directory ownership, and PUID/PGID permissions.
- Favorites and full-catalog generation both run this preflight before network/catalog work begins.

### Noise Suppression
- Detailed catalog page progress, per-title generation chatter, and stale-file dry-run lists are routed into support trace instead of compact logs.
- Compact logging defaults remain tuned for routine operation.
- Debug logging in compact logs is now described separately from support trace so the UI makes the diagnostic path clearer.

### Verification
- Built and tested with the non-integration suite.
- Smoke-tested in Docker on Jellyfin 10.11 with manifest installation.
- Confirmed support trace captures full write-permission failures while compact logs remain short.

### Not Included
- No Jellyfin 10.10 compatibility build.
- No change to AniLiberty API endpoint hosts.
- No change to the legacy repository URL used for the plugin manifest.
- GitHub Release: https://github.com/queukat/AniLibriaStrmPlugin/releases/tag/v2.0.0.11
- Compare: https://github.com/queukat/AniLibriaStrmPlugin/compare/v2.0.0.10...v2.0.0.11

## v2.0.0.10 - 2026-05-10
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
- GitHub Release: https://github.com/queukat/AniLibriaStrmPlugin/releases/tag/v2.0.0.10
- Compare: https://github.com/queukat/AniLibriaStrmPlugin/compare/v2.0.0.9...v2.0.0.10

## v2.0.0.9 - 2026-05-09
AniLiberty STRM Hardening System Release

This update hardens AniLiberty STRM Plugin as a full Jellyfin reconstruction system: stable Jellyfin ABI targeting, explicit AniLiberty identity, safer generated-library governance, streaming playback proxying, and a stronger operational surface for administrators.

### Jellyfin Compatibility & Quality Gates
- Pinned all `Jellyfin.*` NuGet references to `10.11.0` to stop builds from silently drifting to newer Jellyfin packages.
- Added committed `packages.lock.json` files and locked restore in CI.
- Added CI checks that verify every resolved `Jellyfin.*` package remains on `10.11.0` while `targetAbi` stays `10.11.0.0`.
- Release automation now keeps `build.yaml`, package version, assembly version, file version, and informational version in sync.

### AniLiberty Identity & API Courtesy
- Rebranded the public plugin identity to **AniLiberty STRM Plugin** while keeping the legacy `AniLibriaStrmPlugin` repository URL for install compatibility and branch history.
- Added a shared `PluginIdentity` with display name, product token, repository URL, version discovery, and canonical User-Agent.
- All AniLiberty API, media proxy, static media, and HLS clients now identify as:
  `AniLibertyStrmPlugin/<version> (Jellyfin; +https://github.com/queukat/AniLibriaStrmPlugin)`.

### Mirror Governance Layer
- Added `.aniliberty-strm-plugin/manifest.json` inside each output root to track files managed by the plugin.
- The manifest records generated STRM, ANIID, EDL, chapter XML, NFO, artwork/thumbs, release/episode IDs, hashes, sources, and timestamps.
- First manifest run initializes the baseline and never prunes existing files.
- Generated NFO/artwork refreshes only when the file is known as plugin-managed through the manifest or generated marker.
- Existing unmarked `.nfo` files and artwork are preserved as user-owned content.

### Safe Cleanup Modes
- Added `StaleCleanupMode`: `Off`, `DryRun`, and `Delete`.
- Default is `DryRun`, which reports stale managed files without deleting anything.
- `Delete` removes only manifest-managed stale files and then removes empty directories created by that cleanup path.
- Untracked user files are left alone.

### Playback Proxy Rail
- Non-playlist HLS segments and keys now stream directly to `Response.Body`.
- The proxy no longer reads segment/key payloads into memory with `ReadAsByteArrayAsync`.
- Upstream host allow-listing and redirect validation remain in place.
- Playlist rewriting continues to route segments and keys through the Jellyfin proxy endpoint.

### Identity & Access Command Center
- OTP codes are no longer persisted in plugin configuration after the OTP flow.
- Login/password and OTP sign-in clear stored OTP state.
- The token box is masked by default, with explicit reveal/copy actions.
- Auth logs no longer include the login e-mail address.

### Operational Reliability
- Scheduled tasks now rethrow failures after logging so Jellyfin can mark task execution accurately.
- Cancellation flows are preserved instead of being swallowed as generic failures.
- AniLiberty progress pull no longer overwrites newer local Jellyfin playback positions with older remote timecodes.
- Clearing UI logs now persists the cleared state.

### Presentation & Documentation
- README now presents the plugin as a coherent product system: Signal Acquisition Layer, Context-Aware Reconstruction Core, Playback Proxy Rail, Mirror Governance Layer, Operational Command Center, and Distribution Quality Gates.
- Added three README visual assets showing the reconstruction core, system capability layers, and operational command center.

### Test Coverage
- Added tests for Jellyfin package pins and lock-file resolution.
- Added User-Agent and named/static HTTP client coverage.
- Added managed mirror tests for baseline creation, generated metadata refresh, unmarked NFO preservation, dry-run cleanup, delete cleanup, and empty-directory cleanup.
- Added proxy tests for streaming segments, playlist rewriting, unsupported upstream rejection, redirect validation, and upstream failure status.
- Added auth/config tests for OTP payload boundaries, safe defaults, and AniLiberty identity.

### Not Included
- No Jellyfin 10.10 compatibility build in this release.
- No migration of AniLiberty API endpoint hosts.
- No destructive cleanup unless `StaleCleanupMode` is explicitly changed to `Delete`.

- GitHub Release: https://github.com/queukat/AniLibriaStrmPlugin/releases/tag/v2.0.0.9
- Compare: https://github.com/queukat/AniLibriaStrmPlugin/compare/v2.0.0.8...v2.0.0.9

## v2.0.0.8 - 2026-03-14
- release: ship playback proxy and v2 stability updates (250992e)
- GitHub Release: https://github.com/queukat/AniLibriaStrmPlugin/releases/tag/v2.0.0.8
- Compare: https://github.com/queukat/AniLibriaStrmPlugin/compare/v2.0.0.7...v2.0.0.8

## v2.0.0.7 - 2026-02-26
- release: fix comments   - chore: normalize project comments   - removed empty comments across source files (1f4236e)
- release (0427582)
- Merge remote-tracking branch 'origin/aniLiberty-v2' into aniLiberty-v2 (496cf9f)
- release: fix checksum (31eb291)
- chore(release): v2.0.0.6 (48b0d37)
- release: fix pages (671a7d1)
- Merge remote-tracking branch 'origin/aniLiberty-v2' into aniLiberty-v2 (5327aac)
- fix(ci): make manifest jq update type-safe (6fa1961)
- chore(release): v2.0.0.5 (3514fa1)
- release: add AniLiberty watch progress sync (push + pull) with safe Jellyfin user mapping   - Added AniLiberty playback progress push sync via /accounts/users/me/views/timecodes.   - Added manual pull task to import AniLiberty watch progress into Jellyfin.   - Added .aniid sidecar + NFO uniqueid mapping for reliable episode matching.   - Added sync settings in plugin UI, including auto user selection for single-user Jellyfin servers. (d116df5)
- release: automate stable/nightly releases and add playback diagnostics - Replace legacy build/publish workflows with a unified release pipeline:   - Nightly: build on every push to main and update a moving prerelease tag "nightly"   - Stable: when the head commit starts with "release:", bump 4-part version (X.Y.Z.W), tag vX.Y.Z.W and create a GitHub Release (b194dee)
- Merge pull request #3 from queukat/codex/fill-changelog-from-release-notes (0954daf)
- fix(ci): inject release notes into build.yaml changelog (91cca23)
- fix (f1b049d)
- fix yaml (623350b)
- fix ver (af5b8d9)
- flow (3b17fd4)
- logs (a42123e)
- ci: fix git-cliff config for changelog generation (1db57af)
- fix: improve franchise TV season detection and movie folders fix: throw when AniLiberty token is missing for favorites task refactor: harden task logging helper for tests and null plugin test: add manual AniLiberty STRM generation harness build: align test deps with Jellyfin.Controller 10.11.4 (61a697d)
- feat: add AniLiberty OTP auth helper fix: handle franchise TV seasons correctly docs: rewrite README for v2 build: bump targetAbi to 10.11.0.0 ci: add git-cliff changelog step (604e13d)
- fix one punch man (c96b1d8)
- polly (76d3500)
- fixed season info (1f8131d)
- clean up (77e572e)
- GitHub Release: https://github.com/queukat/AniLibriaStrmPlugin/releases/tag/v2.0.0.7

## v2.0.0.6 - 2026-02-26
- release: fix pages (671a7d1)
- Merge remote-tracking branch 'origin/aniLiberty-v2' into aniLiberty-v2 (5327aac)
- fix(ci): make manifest jq update type-safe (6fa1961)
- chore(release): v2.0.0.5 (3514fa1)
- release: add AniLiberty watch progress sync (push + pull) with safe Jellyfin user mapping   - Added AniLiberty playback progress push sync via /accounts/users/me/views/timecodes.   - Added manual pull task to import AniLiberty watch progress into Jellyfin.   - Added .aniid sidecar + NFO uniqueid mapping for reliable episode matching.   - Added sync settings in plugin UI, including auto user selection for single-user Jellyfin servers. (d116df5)
- release: automate stable/nightly releases and add playback diagnostics - Replace legacy build/publish workflows with a unified release pipeline:   - Nightly: build on every push to main and update a moving prerelease tag "nightly"   - Stable: when the head commit starts with "release:", bump 4-part version (X.Y.Z.W), tag vX.Y.Z.W and create a GitHub Release (b194dee)
- Merge pull request #3 from queukat/codex/fill-changelog-from-release-notes (0954daf)
- fix(ci): inject release notes into build.yaml changelog (91cca23)
- fix (f1b049d)
- fix yaml (623350b)
- fix ver (af5b8d9)
- flow (3b17fd4)
- logs (a42123e)
- ci: fix git-cliff config for changelog generation (1db57af)
- fix: improve franchise TV season detection and movie folders fix: throw when AniLiberty token is missing for favorites task refactor: harden task logging helper for tests and null plugin test: add manual AniLiberty STRM generation harness build: align test deps with Jellyfin.Controller 10.11.4 (61a697d)
- feat: add AniLiberty OTP auth helper fix: handle franchise TV seasons correctly docs: rewrite README for v2 build: bump targetAbi to 10.11.0.0 ci: add git-cliff changelog step (604e13d)
- fix one punch man (c96b1d8)
- polly (76d3500)
- fixed season info (1f8131d)
- clean up (77e572e)
- conf (5b6d8c8)
- desk (1e8c968)
- naming (8ec8af2)
- slash (47ac28f)
- init repo (2bf3258)
- GitHub Release: https://github.com/queukat/AniLibriaStrmPlugin/releases/tag/v2.0.0.6

## v2.0.0.5 - 2026-02-26
- release: add AniLiberty watch progress sync (push + pull) with safe Jellyfin user mapping   - Added AniLiberty playback progress push sync via /accounts/users/me/views/timecodes.   - Added manual pull task to import AniLiberty watch progress into Jellyfin.   - Added .aniid sidecar + NFO uniqueid mapping for reliable episode matching.   - Added sync settings in plugin UI, including auto user selection for single-user Jellyfin servers. (d116df5)
- release: automate stable/nightly releases and add playback diagnostics - Replace legacy build/publish workflows with a unified release pipeline:   - Nightly: build on every push to main and update a moving prerelease tag "nightly"   - Stable: when the head commit starts with "release:", bump 4-part version (X.Y.Z.W), tag vX.Y.Z.W and create a GitHub Release (b194dee)
- Merge pull request #3 from queukat/codex/fill-changelog-from-release-notes (0954daf)
- fix(ci): inject release notes into build.yaml changelog (91cca23)
- fix (f1b049d)
- fix yaml (623350b)
- fix ver (af5b8d9)
- flow (3b17fd4)
- logs (a42123e)
- ci: fix git-cliff config for changelog generation (1db57af)
- fix: improve franchise TV season detection and movie folders fix: throw when AniLiberty token is missing for favorites task refactor: harden task logging helper for tests and null plugin test: add manual AniLiberty STRM generation harness build: align test deps with Jellyfin.Controller 10.11.4 (61a697d)
- feat: add AniLiberty OTP auth helper fix: handle franchise TV seasons correctly docs: rewrite README for v2 build: bump targetAbi to 10.11.0.0 ci: add git-cliff changelog step (604e13d)
- fix one punch man (c96b1d8)
- polly (76d3500)
- fixed season info (1f8131d)
- clean up (77e572e)
- conf (5b6d8c8)
- desk (1e8c968)
- naming (8ec8af2)
- slash (47ac28f)
- init repo (2bf3258)
- path 2 (a36cf57)
- paths (1192925)
- icon (bd610cd)
- target (777bd5d)
- GitHub Release: https://github.com/queukat/AniLibriaStrmPlugin/releases/tag/v2.0.0.5

## v2.0.0.4 - 2026-02-22
- Current latest stable release baseline.
- GitHub Release: https://github.com/queukat/AniLibriaStrmPlugin/releases/tag/v2.0.0.4

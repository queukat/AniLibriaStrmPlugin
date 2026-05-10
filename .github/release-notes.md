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

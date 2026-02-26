# Changelog

All notable changes to this plugin are documented in this file.

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

# AniLiberty STRM Plugin for Jellyfin

Browse and watch AniLiberty anime in Jellyfin, with posters, episode descriptions, and your usual Jellyfin player.

The plugin adds the AniLiberty catalog or your favorites to a Jellyfin library. It creates small `.strm` files that point to online video, together with local metadata and artwork. **It does not download episodes for offline viewing.** Playback requires access to AniLiberty and its video servers.

## What you get

- A Jellyfin library built from the full AniLiberty catalog or just your favorites.
- Series, seasons, episodes, and movies with descriptions and artwork.
- A choice of 1080p, 720p, or 480p streams, where available.
- Intro and outro timings when AniLiberty provides them and your Jellyfin client supports skipping.
- Scheduled library updates and optional watch-progress sync with your AniLiberty account.

## Choose the right version

The branches target different Jellyfin versions. They share the AniLiberty features, but their plugin binaries are not interchangeable.

| Jellyfin server | Source branch | Installation |
| --- | --- | --- |
| **12** | [`jellyfin-12`](https://github.com/queukat/AniLibriaStrmPlugin/tree/jellyfin-12) — default branch | Build the `2.0.0.15` candidate below; no public Jellyfin 12 release yet |
| **10.11** | [`aniLiberty-v2`](https://github.com/queukat/AniLibriaStrmPlugin/tree/aniLiberty-v2) | Public release `2.0.0.13`, or build the updated `2.0.0.14` candidate below |
| **10.10** | [`main`](https://github.com/queukat/AniLibriaStrmPlugin/tree/main) — older AniLibria plugin | Refer to that branch's documentation |

The candidate numbers identify source builds; they are **not published releases**. Switching the default GitHub branch does not change the Jellyfin plugin catalog. Check [Releases](https://github.com/queukat/AniLibriaStrmPlugin/releases) and the package's Jellyfin requirement before installing.

## Install

### Jellyfin 10.11: plugin catalog

1. Open **Dashboard → Plugins → Repositories → +**.
2. Add this repository URL:

   ```text
   https://queukat.github.io/AniLibriaStrmPlugin/plugins/manifest.json
   ```

3. Open **Plugins → Catalog**, select **AniLiberty STRM Plugin**, and install the compatible version.
4. Restart Jellyfin.

For Jellyfin 12, use the source-build instructions below until a compatible public package is available.

### Install a ZIP manually

1. Obtain a package built for your Jellyfin version.
2. Stop Jellyfin and back up its existing AniLiberty plugin folder and configuration.
3. Extract the ZIP contents into a versioned folder under Jellyfin's `plugins` directory, such as `AniLiberty STRM Plugin_2.0.0.15` for the Jellyfin 12 candidate.
4. Keep the DLLs and `meta.json` directly inside that folder, not inside an extra nested ZIP directory. Move any previous AniLiberty binary folder outside `plugins` so only one copy remains installed. Keep the separate plugin configuration.
5. Start Jellyfin and check that **AniLiberty STRM Plugin** appears under **Dashboard → Plugins**.

For the official Docker image, the plugin directory is `/config/plugins`. Do not extract a plugin package into your media library or `plugins/configurations`.

## Set up your library

Start with either favorites or the full catalog. You can enable both later, using **separate, non-overlapping output folders**.

1. Open the AniLiberty plugin settings under **Dashboard → Plugins**.
2. Choose what to generate:

   | Library | Settings | Scheduled task |
   | --- | --- | --- |
   | Your favorites | Sign in to AniLiberty, set **Favorites STRM Path**, enable **Generate favorites library** | **Generate AniLiberty STRM (Favorites Only)** |
   | Full catalog | Set **All Titles STRM Path**, enable **Generate full catalog library** | **Generate AniLiberty STRM library** |

3. Disable the generation option you are not using. Choose a dedicated folder writable by the Jellyfin server and your preferred resolution.
4. Save settings, then run the matching task under **Dashboard → Scheduled Tasks → AniLiberty**.
5. Add the output folder as a Jellyfin media library and scan it. Titles, episode entries, and artwork should appear; opening an episode starts the online stream.

Both tasks require their own generation option to be enabled and their path to be set. Favorites also requires a valid AniLiberty token. Full-catalog generation does not require signing in.

The full-catalog task has a daily default schedule. Favorites has no default trigger; add a schedule in Jellyfin if you want automatic updates.

### Docker and remote servers

Use the path **as seen by the Jellyfin server**, not by your browser or host computer. For example, if a Docker volume maps `/srv/aniliberty` on the host to `/media/aniliberty` in the container, use these container paths:

```text
/media/aniliberty/all
/media/aniliberty/favorites
```

The container user must be able to write there. Use the same paths when adding Jellyfin libraries. Type server paths directly: a browser folder picker cannot reliably discover a remote server's full filesystem path.

### Playback address

**Proxy playback through Jellyfin server** is enabled by default. It sends the stream through Jellyfin; AniLiberty remains the video source.

Leave **Jellyfin Playback Proxy Base URL** empty if automatic address selection works. Otherwise, set it to the Jellyfin base address reachable by both your playback devices and the server, for example `https://jellyfin.example.com`. Include any configured Jellyfin base path, but do not append an AniLiberty playback endpoint.

Do not use `localhost` for a TV, phone, or another computer: it points to that device, not your Jellyfin server. In Docker, set the explicit URL or `JELLYFIN_PublishedServerUrl`; without a usable address the plugin falls back to direct AniLiberty stream links.

After changing the address, rerun each enabled generation task to update existing `.strm` links.

## Updates and cleanup

Generation refreshes plugin-managed files while preserving untracked, user-supplied metadata and artwork. Unchanged images use HTTP revalidation when the image server supports it; otherwise refreshing them still requires a download.

**Stale generated files** controls what happens to managed files no longer present in a completed update:

- **Dry-run log** — the default; shows what would be removed without deleting it.
- **Keep** — retains stale files.
- **Delete managed stale files** — removes previously tracked generated files and empty generated directories.

Incomplete API results do not authorize stale-file cleanup. The first update establishes the list of managed files without deleting existing files. Keep a backup before enabling deletion, and never share or nest the favorites and full-catalog output folders.

## Account and watch progress

Use **AniLiberty Authentication** in the plugin settings to sign in with login/password or the offered OTP flow. The saved token enables favorites and optional progress synchronization.

- **Jellyfin → AniLiberty:** enable **Sync playback progress to AniLiberty** to send progress for generated episodes to the connected AniLiberty account. This is off by default.
- **AniLiberty → Jellyfin:** run **Sync AniLiberty watch progress to Jellyfin**. Select **Jellyfin UserId for pull sync** on a multi-user server; a server with one user can select that user automatically.

The connected AniLiberty account is shared by the plugin, not configured separately for each Jellyfin user. Consider this before enabling outbound progress sync on a shared server.

Keep the plugin configuration private: it can contain account tokens, device identifiers, and logs. Do not attach it to a public issue. Before sharing logs or screenshots, remove tokens, authentication details, playback URLs, and personal server paths.

## Troubleshooting

- **No files generated:** confirm the correct task is enabled, settings were saved, and its output folder is writable. Favorites requires a valid sign-in.
- **Files exist but the library is empty:** check that Jellyfin scans the same server-visible folder, then run a library scan.
- **Playback works on one device only:** check the playback base URL and access to the video source from the affected device/server.
- **No skip button:** timings must exist in the AniLiberty source, and the client must support Jellyfin media segments.

Check the task logs on the plugin settings page for API or permission errors. Enable detailed logging only while investigating. Report problems using the [issue templates](https://github.com/queukat/AniLibriaStrmPlugin/issues/new/choose), including your Jellyfin version, plugin version, and installation method.

## Build from source

Install **PowerShell 7** and the SDK for your target: **.NET 9 SDK** for Jellyfin 10.11, or **.NET 10 SDK** for Jellyfin 12. Jellyfin itself supplies the runtime needed to load the plugin.

For **Jellyfin 12**:

```powershell
git clone --branch jellyfin-12 https://github.com/queukat/AniLibriaStrmPlugin.git
cd AniLibriaStrmPlugin
pwsh ./scripts/Build-Plugin.ps1 -Version 2.0.0.15
```

For **Jellyfin 10.11**, use this instead:

```powershell
git clone --branch aniLiberty-v2 https://github.com/queukat/AniLibriaStrmPlugin.git
cd AniLibriaStrmPlugin
pwsh ./scripts/Build-Plugin.ps1 -Version 2.0.0.14
```

Run either sequence in a directory without an existing `AniLibriaStrmPlugin` checkout. The build script uses the selected branch's Jellyfin compatibility and produces a ZIP, build receipt, and checksums under `build/packages`. Use `-OutputDirectory` to choose another destination. Follow the manual ZIP installation steps above.

## Development and contributions

Use the branch matching your target Jellyfin version. Run the offline tests before opening a pull request:

```powershell
dotnet restore --locked-mode
dotnet test -c Release --no-restore --filter "Category!=Integration&FullyQualifiedName!~ManualStrmGenerationTests"
```

Integration and manual-generation tests can contact external services or write media output; they are excluded from this command. Branch builds do not publish a stable release or update the Jellyfin catalog. Public releases have a separate version and release-note approval step.

Issues, feature requests, and pull requests are welcome. Include the target Jellyfin version and keep credentials, local configuration, and generated media out of submissions.

## License

<!-- commercial-license-policy -->
This project is licensed for non-commercial use under the [PolyForm Noncommercial License 1.0.0](https://polyformproject.org/licenses/noncommercial/1.0.0.txt).
Commercial use, resale, paid distribution, marketplace publication, SaaS hosting, or bundling into a paid product requires separate written permission from the author.
Project names, logos, package identifiers, store listings, screenshots, and other branding assets are not licensed for use in forks or redistributed builds.

# AniLiberty STRM Plugin for Jellyfin

![icon](Resources/icon.png)

Generate **`.strm` / `.nfo` / `.edl`** (and optionally native *Skip Intro* markers) for every title that the Russian
fansub site **AniLiberty / AniLibria** hosts — directly from your Jellyfin server (via the AniLiberty API v1).

---

## ✨ Features

- **Two scheduled tasks**
  - **All titles** – mirrors the whole AniLiberty catalogue into a flat STRM library.
  - **Favourites only** – mirrors only what you added to favourites on the site (requires AniLiberty account / token).
- **Per‑episode assets**
  - `SxxExx.strm` with HLS URL.
  - `SxxExx-thumb.jpg/png` (episode preview, if available).
  - `SxxExx.edl` (intro / credits skip).
  - `SxxExx.nfo` (episode metadata: title (RU/EN when available), runtime, year).
- **Intro‑skip**
  - Always generates classic **EDL** files.
  - On Jellyfin ≥ 10.11 additionally writes *Intro* / *Credits* chapter markers → native *Skip Intro* button.
- **API v1 aware metadata**
  - Uses `release.year` (API v1) for movies and show grouping (season object does **not** contain a year in v1).
  - Better type handling: **MOVIE** is detected by `type.value`; **SPECIAL / OVA / OAD** are routed into **Season 00**.
- **Robust HTTP layer**
  - DI-managed `HttpClient` for the AniLiberty API (consistent headers, `ResponseHeadersRead`).
  - Auto‑retry with exponential back‑off (Polly) for transient errors **and HTTP 429 (Too Many Requests)**.
  - Reused HTTP clients for media/image downloads to reduce overhead.
- **Image handling**
  - Supports API v1 image schema (`preview/thumbnail/optimized`) and prefers non‑optimized URLs when possible.
  - Normalizes `.webp` URLs to `.jpg` and safely picks extensions even when URLs contain query parameters.
- **Built‑in auth helper**
  - Login via **e‑mail + password** or **OTP** against AniLiberty API v1.
  - Plugin stores a **JWT** (`AniLibertyToken`) and device id; you don’t have to paste cookies manually.
- **UI logs that don’t explode**
  - Full logging into the plugin’s configuration page (Last Task Logs).
  - Optional **Enable debug logs** switch (very noisy): when OFF, DEBUG/TRACE aren’t stored in UI logs and per‑title progress is throttled.

---

## 🚀 Requirements

|                 | Minimum / tested                                      |
|-----------------|-------------------------------------------------------|
| Jellyfin server | **10.11.0** (target ABI `10.11.0.0`)                  |
| .NET runtime    | `net9.0` (bundled with Jellyfin 10.11+ server builds) |
| OS              | Anything Jellyfin runs on (Windows / Linux / macOS)   |

Older Jellyfin 10.10 builds are **not** supported: the plugin is compiled against 10.11
(Jellyfin.Controller / Jellyfin.Model 10.11.*).

---

## 🔧 Installation

### Option A — via GitHub Pages repository (recommended)

1. Open **Dashboard → Plugins → Repositories → +** in Jellyfin.
2. Add repository URL:

   ```text
   https://queukat.github.io/AniLibriaStrmPlugin/plugins/manifest.json
   ```

3. Go to **Dashboard → Plugins → Catalog**, refresh the page.
4. Find **“AniLiberty STRM Plugin”**, click **Install**, restart Jellyfin.

The repository and manifest are built automatically by GitHub Actions from this repo.

### Option B — manual ZIP

1. Download the latest `AniLibertyStrmPlugin_*.zip` from this repository’s **Releases** page.
2. Stop Jellyfin.
3. Unpack the ZIP into Jellyfin’s `plugins` directory, preserving the folder structure, e.g.:

   ```text
   <jellyfin>/plugins/aniliberty-strm-plugin/
       AniLibertyStrmPlugin.dll
       Polly.dll
       Polly.Core.dll
       Polly.Extensions.Http.dll
       Microsoft.Extensions.Http.Polly.dll
       icon.png
       meta.json
       ...
   ```

4. Start Jellyfin.

---

## 🛠 Configuration

Open **Dashboard → Plugins → AniLiberty STRM**.

### Paths and behaviour

| Field                        | Meaning                                                                 |
|------------------------------|-------------------------------------------------------------------------|
| **All Titles STRM Path**     | Where to write the global catalogue. Leave empty to disable.           |
| **Favourites STRM Path**     | Separate folder for your AniLiberty favourites.                        |
| **Preferred Resolution**     | 1080 / 720 / 480 – which HLS URL to prefer in generated `.strm`.       |
| **Update favourites folder** | If unchecked, the “Favourites only” scheduled task will be skipped.    |
| **Update full catalogue**    | If unchecked, the “All titles” scheduled task will be skipped.         |
| Pagination settings          | API paging; change only if you hit rate limits or need to throttle.    |
| Logging options              | UI min log level, **Enable debug logs**, **Enable playback diagnostics logs**, and how many lines to keep. |

### Logging notes

- **UiMinLogLevel** controls what gets stored in *Last Task Logs*.
- **Enable debug logs (very noisy)**:
  - When **OFF**: DEBUG/TRACE messages are not stored in UI logs; per‑title progress logging is throttled.
  - When **ON**: UI logs become much more verbose (use it for troubleshooting).
- **Enable playback diagnostics logs**:
  - Adds per-episode diagnostics to *Last Task Logs*:
    selected HLS URL, normalized URL, existing/new `.strm` value, and a server-side HLS probe summary.

### AniLiberty authentication

At the bottom of the config page there is an **“AniLiberty Authorization”** section.

You have two flows:

1. **Login + password**
   - Enter your AniLiberty **e‑mail** and **password**.
   - Press **Log In**.
   - On success, the plugin receives a JWT token and saves it into config
     (`AniLibertyToken`). The token is shown in the *Token* box.

2. **OTP flow**
   - Press **Start** – the plugin requests a one‑time code for your device id (the last received code is shown on the page).
   - Enter the received code into the OTP field.
   - Press **Sign In** to exchange the code for a JWT and store it.

The token and device id are stored in the plugin configuration and reused by:

- **Favorites task** – to fetch `/accounts/users/me/favorites/releases`.
- Any future authenticated API calls.

You can copy or clear the token from the same page.

---

## 📅 Scheduled tasks

Two tasks appear under **Dashboard → Scheduled Tasks → AniLiberty**:

1. **Generate AniLiberty STRM library**
   - Fetches *all* titles from AniLiberty (`/anime/catalog/releases`).
   - Respects `AllTitlesPageSize` and `AllTitlesMaxPages`.
   - Generates `.strm`, `.nfo`, `.edl`, thumbnails and optional chapters
     under **All Titles STRM Path**.
   - By default runs **once per day** (interval trigger).

2. **Generate AniLiberty STRM (Favorites Only)**
   - Requires a valid `AniLibertyToken`.
   - Fetches favourites and generates the same set of files under
     **Favourites STRM Path**.
   - Has no default trigger; you can enable and schedule it as you like.

After saving settings you can run tasks manually or wait for the scheduler.

---

## 🏗 Build from source

```bash
# clone
git clone https://github.com/queukat/AniLibriaStrmPlugin.git
cd AniLibriaStrmPlugin

# build & test
dotnet build -c Release
dotnet test  -c Release

# package with JPRM (build.yaml is already included)
jprm build .
# or use the provided GitHub Actions workflows (see .github/workflows)
```

The repository already contains `build.yaml` and CI workflows that:

- Compile the plugin for **net9.0 / Jellyfin 10.11**.
- Package it together with dependencies (`Polly`, `Microsoft.Extensions.Http.Polly`, `icon.png`).
- Publish the ZIP and `manifest.json` to GitHub Pages.

---

## 🔁 Release Automation

The repository uses two workflows:

- `.github/workflows/nightly.yml`
  - Trigger: every push to `main`.
  - Builds the plugin and updates a moving prerelease with tag `nightly`.
  - Replaces assets in that single prerelease (no new tag per commit).

- `.github/workflows/release.yml`
  - Trigger: push to `main` where the **head commit message starts with `release:`**.
  - Bumps patch part of 4-part version (`X.Y.Z.W`) in `build.yaml`.
  - Creates tag `vX.Y.Z.W`.
  - Creates GitHub Release with generated notes + commit summary since previous stable tag.
  - Updates `gh-pages/plugins/manifest.json` and publishes release asset + checksum.
  - Updates and publishes `CHANGELOG.md`.

### Version source of truth

- Version is stored in `build.yaml` (`version: "X.Y.Z.W"`).
- Stable release bumps the **4th part** (`W`) by default.
- Manifest/catalog version is produced from the same release version and kept in sync automatically.

### Manifest and changelog visibility in Jellyfin

- Plugin catalog manifest is published to:
  - `https://queukat.github.io/AniLibriaStrmPlugin/plugins/manifest.json`
- For stable releases CI updates manifest fields for the new version:
  - `version`
  - `sourceUrl`
  - `checksum`
  - `changelog` (summary + links)
- Additional links are added in manifest version entry:
  - `releaseUrl`
  - `changelogUrl`
- Public changelog is published to:
  - `https://queukat.github.io/AniLibriaStrmPlugin/CHANGELOG.md`

### How to force stable release

1. Push your changes to `main`.
2. Ensure the last pushed commit message starts with `release:`.
3. CI does the rest: bump version, tag, release, manifest, changelog.

Example commit message:

```text
release: fix Android TV playback diagnostics and manifest sync
```

---

## 🧯 CI Troubleshooting

- `release.yml` did not run:
  - Verify branch is `main`.
  - Verify the latest pushed commit message starts with `release:`.
- Tag push or commit push failed from Actions:
  - Check branch protection rules allow `GITHUB_TOKEN` to push tags/commits.
- Manifest not updated:
  - Check `gh-pages` branch exists and workflow has `contents: write`.
- Jellyfin catalog shows old data:
  - GitHub Pages cache/refresh delay can take a few minutes.
  - Re-open Plugins Catalog in Jellyfin and refresh repositories.

---

## 🤝 Contributing

Issues and PRs are welcome.  
Feel free to file bugs, request features or send patches.

---

## 📜 License

MIT © 2025 **queukat**

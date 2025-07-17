# AniLiberty STRM Plugin for Jellyfin

![icon](icon.png)

Generate **`.strm` / `.nfo` / `.edl`** (and optionally native *Skip Intro* markers) for every title that the Russian fansub site **[AniLiberty](https://www.anilibria.tv)** hosts — directly from your Jellyfin server.

---
## ✨ Features

* **Two Scheduled Tasks**
  * **All titles** – mirrors the whole AniLiberty catalogue into a flat STRM library.
  * **Favourites only** – mirrors only what you added to favourites on the site (needs SessionID).
* **Per‑episode assets**  (`preview.jpg`, `.edl`, `.nfo`).
* **Intro‑skip**
  * Always generates classic **EDL** files.
  * On Jellyfin ≥ 10.11 additionally writes *Intro* chapter markers → native *Skip Intro* button.
  * *(Currently commented out until Jellyfin 10.11 is officially released.)*
* Auto‑retry HTTP with exponential back‑off; parallel downloads; full logging to the plugin page.
* Built‑in auth helper: log in once → plugin stores your `PHPSESSID`.

---
## 🚀 Requirements

|                  | Minimum |
|------------------|---------|
| Jellyfin server  | **10.10.0** (10.11.0 for native Intro markers) |
| .NET runtime     | `net8.0` (already bundled with Jellyfin 10.10+) |
| OS               | Windows / Linux / macOS – anything Jellyfin runs on |

---
## 🔧 Installation

### Option A – via custom repository (recommended)
1. Drop `AniLibertyStrm_1.0.0.zip` (this repo’s *Releases* page) somewhere online.
2. Create **`repo.json`** in the same place:
   ```json
   {
     "Plugins": [
       {
         "Name": "AniLiberty STRM Plugin",
         "Guid": "cce0798d-c8b7-4265-b08c-dc9e7bd3fc0f",
         "Version": "1.0.0",
         "Overview": "Creates .strm / .nfo / .edl for AniLiberty",
         "Packages": [
           {
             "TargetAbi": "10.10.0.0",
             "AssemblyVersion": "1.0.0.0",
             "ZipUrl": "https://<your-host>/AniLibertyStrm_1.0.0.zip",
             "Checksum": "<SHA256>"
           }
         ]
       }
     ]
   }
   ```
3. Jellyfin → **Dashboard → Plugins → Repositories → +**. Paste the raw URL of `repo.json`.
4. Reload the *Catalog* tab, search “AniLiberty STRM”, click **Install**.

### Option B – manual DLL
1. Stop Jellyfin.
2. Copy `AniLibertyStrmPlugin.dll` to `<jellyfin>/plugins/`.
3. Start Jellyfin.

---
## 🛠 Configuration

*Open Dashboard → Plugins → AniLiberty STRM.*

| Field | Meaning |
|-------|---------|
| **All Titles STRM Path** | Where to write the global catalogue. Leave empty to disable. |
| **Favourites STRM Path** | Separate folder for your favourites. |
| **Preferred Resolution** | 1080 / 720 / 480 – link that will be written into `.strm`. |
| **AniLiberty SessionID**  | `PHPSESSID` cookie. Press **Log In** on the page to obtain automatically. |
| Pagination settings      | API limits: change only if you hit rate‐limits. |

After saving run tasks once manually or wait for scheduler (All Titles – daily).

---
## 🏗 Build from source

```bash
# clone
git clone https://github.com/queukat/AniLibertyStrmPlugin.git
cd AniLibertyStrmPlugin

# build & test
 dotnet build -c Release

# package
 dotnet publish -c Release -o package
 cd package
 zip -r ../AniLibertyStrm_1.0.0.zip .
```

---
## 🤝 Contributing

Issues and PRs are welcome!  Feel free to file bugs, request features or send patches.

---
## 📜 License

MIT © 2025 **queukat**


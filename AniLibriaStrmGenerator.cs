// ========= File: AniLibriaStrmGenerator.cs =========

using AniLibriaStrmPlugin.Models;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using System.Security;
using System.Text;
using MediaBrowser.Model.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Chapters;

namespace AniLibriaStrmPlugin
{
    public interface IAniLibriaStrmGenerator
    {
        Task GenerateTitlesAsync(IEnumerable<TitleResponse> titles, string basePath, string resolution,
            IProgress<double>? progress, CancellationToken token);
    }

    public sealed class AniLibriaStrmGenerator : IAniLibriaStrmGenerator
    {
        private readonly ILogger<AniLibriaStrmGenerator> _log;
        private readonly ILibraryManager _library;
        private readonly IChapterManager _chapters;

        public AniLibriaStrmGenerator(
            ILogger<AniLibriaStrmGenerator> log,
            ILibraryManager library,
            IChapterManager chapters)
        {
            _log = log;
            _library = library;
            _chapters = chapters;
        }

        public async Task GenerateTitlesAsync(IEnumerable<TitleResponse> titles, string basePath, string resolution,
            IProgress<double>? progress, CancellationToken token)
        {
            Directory.CreateDirectory(basePath);

            var list = titles.ToList();
            var total = list.Count;
            var current = 0;

            foreach (var t in list)
            {
                token.ThrowIfCancellationRequested();
                current++;

                var name = t.Names?.En ?? t.Code;
                _log.LogInformation("({Cur}/{Tot}) {Name}", current, total, name);
                await GenerateStrmForTitle(t, basePath, resolution, token);
                progress?.Report(current / (double)total * 100);
            }
        }

        // ---------------------------------------------------------------------
        private async Task GenerateStrmForTitle(
            TitleResponse title,
            string basePath,
            string resolution,
            CancellationToken token)
        {
            // ───────── Skip titles without streams ─────────
            if (title.Player?.List is null || title.Player.List.Count == 0)
            {
                _log.LogInformation("Skip {Id} –  /HLS", title.Id);
                return;
            }

            // ───────── Names ─────────
            var engName = title.Names?.En?.Trim();
            var rusName = title.Names?.Ru?.Trim();
            var safeName = MakeSafe(engName ?? title.Code ?? $"Title_{title.Id}");
            var seasonNum = title.Franchises?
                .SelectMany(f => f.Releases ?? new())
                .FirstOrDefault(r => r.Id == title.Id)?.Ordinal ?? 1;

            // ───────── Directories ─────────
            var showDir = Path.Combine(basePath, safeName);
            var seasonDir = Path.Combine(showDir, $"Season {seasonNum}");
            Directory.CreateDirectory(seasonDir);

            // ───────── Poster ─────────
            await DownloadIfAbsentAsync(
                "https://www.anilibria.tv" + (title.Posters?.Original?.Url ?? string.Empty),
                Path.Combine(showDir, "folder.jpg"),
                token);

            // ───────── tvshow.nfo (RU title, EN original, plot) ─────────
            var tvshowNfo = Path.Combine(showDir, "tvshow.nfo");
            if (!File.Exists(tvshowNfo))
            {
                var plot = MakeSafeXml(title.Description?.Trim() ?? string.Empty);
                var xml = $@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<tvshow>
  {(rusName is null ? string.Empty : $"<title>{MakeSafeXml(rusName)}</title>")}
  {(engName is null ? string.Empty : $"<originaltitle>{MakeSafeXml(engName)}</originaltitle>")}
  {(plot.Length == 0 ? string.Empty : $"<plot>{plot}</plot><outline>{plot}</outline>")}
</tvshow>";
                await File.WriteAllTextAsync(tvshowNfo, xml, Encoding.UTF8, token);
            }

            // ───────── Episodes ─────────
            var lastEp = title.Player.Episodes?.Last ?? 1;
            for (int ep = 1; ep <= lastEp; ep++)
            {
                token.ThrowIfCancellationRequested();

                var epKey = ep.ToString();
                var epInfo = title.Player.List.GetValueOrDefault(epKey);

                var url = epInfo?.Hls != null
                    ? ChooseHls(epInfo.Hls, resolution, title.Player.Host)
                    : null;
                if (url is null) continue;

                var strmFile = $"{safeName} - S{seasonNum:00}E{ep:00}.strm";
                var strmPath = Path.Combine(seasonDir, strmFile);
                if (!File.Exists(strmPath))
                    await File.WriteAllTextAsync(strmPath, url, token);

                // 
                // preview
                await DownloadIfAbsentAsync(
                    "https://www.anilibria.tv" + (epInfo?.Preview ?? string.Empty),
                    Path.Combine(seasonDir, $"{safeName} - S{seasonNum:00}E{ep:00}-preview.jpg"),
                    token);

                // edl + intro-chapter
                if (epInfo?.Skips is { Opening: { Count: >= 2 } op })
                {
                    var startSec = op[0];
                    var endSec   = op[1];
                
                    // 1) EDL ─ Kodi- side-car
                    var edlPath = Path.ChangeExtension(strmPath, ".edl");
                    if (!File.Exists(edlPath))
                        await File.WriteAllLinesAsync(edlPath,
                            new[] { $"{startSec} {endSec} 0" },   //  
                            token);
                
                    // 2)   «Intro» (Jellyfin ≥ 10.11)
                    try
                    {
                        // FindByPath → BaseItem → Video
                        if (_library.FindByPath(strmPath, isFolder: false) is Video item)
                        {
                            var chapters = new[]
                            {
                                new ChapterInfo
                                {
                                    Name               = "Intro",
                                    StartPositionTicks = TimeSpan.FromSeconds(startSec).Ticks
                                    // EndPositionTicks   –    / 
                                }
                            };
                
                            // !!  : ё Guid,    
                            var existing = _chapters.GetChapters(item.Id);
                
                            if (existing.Count == 0 ||
                                existing[0].StartPositionTicks != chapters[0].StartPositionTicks)
                            {
                                _chapters.SaveChapters(item, chapters);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Unable to set Intro chapter for {File}", strmPath);
                    }
                }



                // episode-nfo  (RU + EN titles)
                var nfoPath = Path.ChangeExtension(strmPath, ".nfo"); // ⇒ …S01E01… .nfo
                if (!File.Exists(nfoPath))
                {
                    var epRus = epInfo?.Name?.Trim() ?? $"Episode {ep}";
                    var xml = $@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<episodedetails>
  <title>{MakeSafeXml(epRus)}</title>
  {(engName is null ? string.Empty : $"<originaltitle>{MakeSafeXml(epRus)}</originaltitle>")}
  <season>{seasonNum}</season>
  <episode>{ep}</episode>
  <showtitle>{MakeSafeXml(engName ?? safeName)}</showtitle>
</episodedetails>";
                    await File.WriteAllTextAsync(nfoPath, xml, Encoding.UTF8, token);
                }
            }
        }


        // ---------------------------------------------------------------------
        private static async Task DownloadIfAbsentAsync(string url, string path, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(url) || File.Exists(path)) return;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var bytes = await http.GetByteArrayAsync(url, ct);
                await File.WriteAllBytesAsync(path, bytes, ct);
            }
            catch
            {
                /* not critical */
            }
        }

        // helpers --------------------------------------------------------------
        private static string? ChooseHls(HlsBlock? hls, string pref, string? host)
        {
            if (hls == null || string.IsNullOrEmpty(host)) return null;
            var link = pref switch
            {
                "1080" => hls.Fhd ?? hls.Hd ?? hls.Sd,
                "720" => hls.Hd ?? hls.Fhd ?? hls.Sd,
                "480" => hls.Sd ?? hls.Hd ?? hls.Fhd,
                _ => hls.Hd ?? hls.Sd ?? hls.Fhd
            };
            return link is null ? null : $"https://{host}{link}";
        }

        private static string MakeSafe(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        private static string MakeSafeXml(string? text) => SecurityElement.Escape(text) ?? "";
    }
}
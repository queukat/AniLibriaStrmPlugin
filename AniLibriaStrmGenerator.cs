using AniLibriaStrmPlugin.Models;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
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
            _log      = log;
            _library  = library;
            _chapters = chapters;
        }

        // ────────────────────── 1. очистка названия ──────────────────────
        private static readonly Regex[] _suffixRules =
        {
            //  ❶  … Season 2 / 2nd Season / 3rd Season …
            new(@"\s*(Season)\s*\d+\b.*$", RegexOptions.IgnoreCase),

            //  ❷  … 2nd Season / 2‑nd Season (с числом перед Season)
            new(@"\s*\d+(?:st|nd|rd|th)?\s*Season\b.*$", RegexOptions.IgnoreCase),

            //  ❸  Part X / Cour Y
            new(@"\s*(?:Part|Cour)\s*\d+\b.*$", RegexOptions.IgnoreCase),

            //  ❹  “… II / III / IV”  (римские, ≤ 8)
            new(@"\s*[-._ ]+(?:I{2,3}|IV|VI{0,2}|VII?)$", RegexOptions.IgnoreCase),

            //  ❺  одиночная 2 / 3 / 4 в конце
            new(@"\s+([2-4])$", RegexOptions.IgnoreCase),

            //  ❻  OAD / OVA / Special / Movie
            new(@"\s+(?:OAD|OVA|OAV|Special|Movie)$", RegexOptions.IgnoreCase)
        };


        private static string CleanShowName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return name;

            var cleaned = _suffixRules.Aggregate(name, (current, rx) => rx.Replace(current, ""));
            cleaned = Regex.Replace(cleaned, @"[\s\.\-_()]+$", "");
            return cleaned.Trim();
        }

        // ───────────────────────── 2. публичный API ───────────────────────
        public async Task GenerateTitlesAsync(IEnumerable<TitleResponse> titles, string basePath, string resolution,
            IProgress<double>? progress, CancellationToken token)
        {
            Directory.CreateDirectory(basePath);

            var list   = titles.ToList();
            var total  = list.Count;
            var current = 0;

            foreach (var t in list)
            {
                token.ThrowIfCancellationRequested();
                current++;

                _log.LogInformation("({Cur}/{Tot}) {Name}", current, total, t.Names?.En ?? t.Code);
                await GenerateStrmForTitle(t, basePath, resolution, token);
                progress?.Report(current / (double)total * 100);
            }
        }

        // ─────────────────────── 3. один релиз → файлы ─────────────────────
        private async Task GenerateStrmForTitle(
            TitleResponse title,
            string basePath,
            string resolution,
            CancellationToken token)
        {
            if (title.Player?.List is null || title.Player.List.Count == 0)
            {
                _log.LogInformation("Skip {Id} – no streams/HLS", title.Id);
                return;
            }

            // ---- имена -----------------------------------------------------
            var engName  = title.Names?.En?.Trim();
            var jpName   = title.Names?.Alternative?.Trim();      // японский (если есть)
            var ruName   = title.Names?.Ru?.Trim();
            var rawName  = engName ?? title.Code ?? $"Title_{title.Id}";
            var safeName = MakeSafe(CleanShowName(rawName));

            var seasonNum = title.Franchises?
                .SelectMany(f => f.Releases ?? new())
                .FirstOrDefault(r => r.Id == title.Id)?.Ordinal ?? 1;

            // ---- директории ------------------------------------------------
            var showDir   = Path.Combine(basePath, safeName);
            var seasonDir = Path.Combine(showDir, $"Season {seasonNum}");
            Directory.CreateDirectory(seasonDir);

            // ---- постеры ---------------------------------------------------
            var posterUrl = "https://www.anilibria.tv" + (title.Posters?.Original?.Url ?? string.Empty);

            // • постер сериала (folder.jpg)
            await DownloadIfAbsentAsync(posterUrl, Path.Combine(showDir, "folder.jpg"), token);

            // • постер сезона (seasonXX-poster.jpg)
            var seasonPoster = $"season{seasonNum:00}-poster.jpg";
            await DownloadIfAbsentAsync(posterUrl, Path.Combine(showDir, seasonPoster), token);

            // ---- tvshow.nfo -----------------------------------------------
            var tvshowNfo = Path.Combine(showDir, "tvshow.nfo");
            if (!File.Exists(tvshowNfo))
            {
                var plot = MakeSafeXml(title.Description?.Trim() ?? string.Empty);
                var xml  = $@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<tvshow>
  {(engName is not null ? $"<title>{MakeSafeXml(engName)}</title>" : string.Empty)}
  {(jpName  is not null ? $"<originaltitle>{MakeSafeXml(jpName)}</originaltitle>" : string.Empty)}
  {(ruName  is not null ? $"<sorttitle>{MakeSafeXml(ruName)}</sorttitle>" : string.Empty)}
  {(plot.Length > 0 ? $"<plot>{plot}</plot><outline>{plot}</outline>" : string.Empty)}
  <lockdata>false</lockdata>
</tvshow>";
                await File.WriteAllTextAsync(tvshowNfo, xml, Encoding.UTF8, token);
            }

            // ---- season.nfo -----------------------------------------------
            var seasonNfo = Path.Combine(seasonDir, "season.nfo");
            if (!File.Exists(seasonNfo))
            {
                var seasonXml = $@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<season>
  <title>Season {seasonNum}</title>
  <seasonnumber>{seasonNum}</seasonnumber>
  <lockdata>false</lockdata>
</season>";
                await File.WriteAllTextAsync(seasonNfo, seasonXml, Encoding.UTF8, token);
            }

            // ---- эпизоды ---------------------------------------------------
            var lastEp = title.Player.Episodes?.Last ?? 1;

            for (var ep = 1; ep <= lastEp; ep++)
            {
                token.ThrowIfCancellationRequested();

                if (!title.Player.List.TryGetValue(ep.ToString(), out var epInfo))
                    continue;

                var url = epInfo?.Hls != null
                    ? ChooseHls(epInfo.Hls, resolution, title.Player.Host)
                    : null;
                if (url is null)
                    continue;

                var strmFile = $"S{seasonNum:00}E{ep:00}.strm";
                var strmPath = Path.Combine(seasonDir, strmFile);
                if (!File.Exists(strmPath))
                    await File.WriteAllTextAsync(strmPath, url, token);

                // миниатюра серии
                if (!string.IsNullOrWhiteSpace(epInfo.Preview))
                {
                    var ext       = Path.GetExtension(epInfo.Preview);
                    if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                    var thumbPath = Path.Combine(seasonDir, $"S{seasonNum:00}E{ep:00}-thumb{ext}");
                    await DownloadIfAbsentAsync("https://www.anilibria.tv" + epInfo.Preview, thumbPath, token);
                }

                // EDL + главы для Skip-Intro
                if (epInfo?.Skips is { Opening: { Count: >= 2 } op })
                {
                    var startSec = op[0];
                    var endSec   = op[1];

                    // .edl
                    var edlPath = Path.ChangeExtension(strmPath, ".edl");
                    if (!File.Exists(edlPath))
                        await File.WriteAllLinesAsync(edlPath,
                            new[] { $"{startSec} {endSec} 0" }, token);

                    // главы
                    try
                    {
                        if (_library.FindByPath(strmPath, isFolder: false) is Video item)
                        {
                            var chapters = new[]
                            {
                                new ChapterInfo
                                {
                                    Name = "Intro",
                                    StartPositionTicks = TimeSpan.FromSeconds(startSec).Ticks
                                },
                                new ChapterInfo
                                {
                                    Name = "Post-Intro",
                                    StartPositionTicks = TimeSpan.FromSeconds(endSec).Ticks
                                }
                            };

                            var existing = _chapters.GetChapters(item.Id);
                            if (existing.Count < 2 ||
                                existing[0].StartPositionTicks != chapters[0].StartPositionTicks ||
                                existing[1].StartPositionTicks != chapters[1].StartPositionTicks)
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

                // episode.nfo (только базовое)
                var nfoPath = Path.ChangeExtension(strmPath, ".nfo");
                if (!File.Exists(nfoPath))
                {
                    var epRu = epInfo.Name?.Trim() ?? $"Episode {ep}";
                    var xml  = $@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<episodedetails>
  <title>{MakeSafeXml(epRu)}</title>
  <season>{seasonNum}</season>
  <episode>{ep}</episode>
  <showtitle>{MakeSafeXml(engName ?? safeName)}</showtitle>
</episodedetails>";
                    await File.WriteAllTextAsync(nfoPath, xml, Encoding.UTF8, token);
                }
            }
        }

        // ──────────────────────── helpers ─────────────────────────────
        private static async Task DownloadIfAbsentAsync(string url, string path, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(url) || File.Exists(path))
                return;

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

        private static string? ChooseHls(HlsBlock? hls, string pref, string? host)
        {
            if (hls == null || string.IsNullOrEmpty(host))
                return null;

            var link = pref switch
            {
                "1080" => hls.Fhd ?? hls.Hd ?? hls.Sd,
                "720"  => hls.Hd  ?? hls.Fhd ?? hls.Sd,
                "480"  => hls.Sd  ?? hls.Hd  ?? hls.Fhd,
                _      => hls.Hd  ?? hls.Sd  ?? hls.Fhd
            };
            return link is null ? null : $"https://{host}{link}";
        }

        private static string MakeSafe(string s) =>
            Path.GetInvalidFileNameChars()
                .Aggregate(s, (current, c) => current.Replace(c, '_'))
                .Trim();

        private static string MakeSafeXml(string? text) =>
            SecurityElement.Escape(text) ?? string.Empty;
    }
}

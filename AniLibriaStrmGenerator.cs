using AniLibriaStrmPlugin.Models;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
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

        // ────────────────────── 1. очистка названия ──────────────────────
        private static readonly Regex[] _suffixRules =
        {
            new(@"\s*(?:Season)\s*\d+\b.*$", RegexOptions.IgnoreCase),
            new(@"\s*\d+(?:st|nd|rd|th)?\s*Season\b.*$", RegexOptions.IgnoreCase),
            new(@"\s*(?:Part|Cour)\s*\d+\b.*$", RegexOptions.IgnoreCase),
            new(@"\s*\d+(?:st|nd|rd|th)?\s*Cour\b.*$", RegexOptions.IgnoreCase),
            new(@"\s*[-._ ]+(?:I{2,3}|IV|V?I{0,3}|VII?)$", RegexOptions.IgnoreCase),
            new(@"\s+[2-4]$", RegexOptions.IgnoreCase),
            new(@"\s+(?:OAD|OVA|OAV|Special|Movie)$", RegexOptions.IgnoreCase),
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

            var list = titles.ToList();
            var total = list.Count;
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
            var engName = title.Names?.En?.Trim();
            var jpName = title.Names?.Alternative?.Trim(); // японский (если есть)
            var ruName = title.Names?.Ru?.Trim();
            var rawName = engName ?? title.Code ?? $"Title_{title.Id}";
            var safeName = MakeSafe(CleanShowName(rawName)).ToLowerInvariant();

            var seasonNum = title.Franchises?
                .SelectMany(f => f.Releases ?? new())
                .FirstOrDefault(r => r.Id == title.Id)?.Ordinal ?? 1;

            // ---- директории ------------------------------------------------
            var showDir = Path.Combine(basePath, safeName.ToLowerInvariant());
            var seasonDir = Path.Combine(showDir, $"Season {seasonNum}");
            Directory.CreateDirectory(seasonDir);

            // ---- постеры ---------------------------------------------------
            var posterUrl = "https://www.anilibria.tv" + (title.Posters?.Original?.Url ?? string.Empty);

            await DownloadIfAbsentAsync(posterUrl, Path.Combine(showDir, "folder.jpg"), token);
            await DownloadIfAbsentAsync(posterUrl, Path.Combine(showDir, $"season{seasonNum:00}-poster.jpg"), token);

            // ---- tvshow.nfo -----------------------------------------------
            var tvshowNfo = Path.Combine(showDir, "tvshow.nfo");
            if (!File.Exists(tvshowNfo))
            {
                var displayTitle = ruName ?? engName ?? safeName; // то, что увидит пользователь
                var originalTitle = jpName ?? engName ?? displayTitle; // «оригинал» (япон. или англ.)
                var sortTitle = engName ?? ruName ?? displayTitle; // латиница для поиска
                var plot = MakeSafeXml(title.Description?.Trim() ?? string.Empty);

                var xml = $@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
            <tvshow>
              <title>{MakeSafeXml(displayTitle)}</title>
              {(originalTitle != displayTitle ? $"<originaltitle>{MakeSafeXml(originalTitle)}</originaltitle>" : string.Empty)}
              {(sortTitle != displayTitle ? $"<sorttitle>{MakeSafeXml(sortTitle)}</sorttitle>" : string.Empty)}
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
                    var ext = Path.GetExtension(epInfo.Preview);
                    if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                    var thumbPath = Path.Combine(seasonDir, $"S{seasonNum:00}E{ep:00}-thumb{ext}");
                    await DownloadIfAbsentAsync("https://www.anilibria.tv" + epInfo.Preview, thumbPath, token);
                }

                // EDL + условные главы для Skip-Intro
                if (epInfo?.Skips is { Opening: { Count: >= 2 } op })
                {
                    var startSec = op[0];
                    var endSec = op[1];

                    // .edl
                    var edlPath = Path.ChangeExtension(strmPath, ".edl");
                    if (!File.Exists(edlPath))
                        await File.WriteAllLinesAsync(edlPath,
                            new[] { $"{startSec} {endSec} 0" }, token);

                    // главы – только если знаем длительность и она > endSec
                    var runtimeSec = await GetHlsDurationAsync(url, token);
                    if (runtimeSec > endSec + 1) // запас 1 сек
                    {
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

#if JF_10_10

                                _chapters.SaveChapters(item.Id, chapters);
#else
                                var existing = _chapters.GetChapters(item.Id);        // перегрузка есть и в 10.11
                                if (existing.Count < 2 ||
                                    existing[0].StartPositionTicks != chapters[0].StartPositionTicks ||
                                    existing[1].StartPositionTicks != chapters[1].StartPositionTicks)
                                    _chapters.SaveChapters(item, chapters);
#endif
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Unable to set Intro chapter for {File}", strmPath);
                        }
                    }
                    else
                    {
                        _log.LogDebug("Skip chapters for {Ep} – runtime {Run:0.0}s ≤ {End}s",
                            strmFile, runtimeSec, endSec);
                    }
                }

                // episode.nfo (только базовое)
                var nfoPath = Path.ChangeExtension(strmPath, ".nfo");
                if (!File.Exists(nfoPath))
                {
                    var epRu = epInfo.Name?.Trim() ?? $"Episode {ep}";
                    var showTitle = ruName ?? engName ?? safeName; // тот же, что <title> в tvshow.nfo
                    var xml = $@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
                <episodedetails>
                  <title>{MakeSafeXml(epRu)}</title>
                  <season>{seasonNum}</season>
                  <episode>{ep}</episode>
                  <showtitle>{MakeSafeXml(showTitle)}</showtitle>
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
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-AniLibriaStrm/1.0 (+https://github.com)");

                var resp = await http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return;

                // Быстрый чек: JPEG должен начинаться с FF D8, PNG — с 89 50 4E 47
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 4) return;
                var isJpg = bytes[0] == 0xFF && bytes[1] == 0xD8;
                var isPng = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
                if (!isJpg && !isPng)
                {
                    Console.WriteLine("[AniStrm] {0} – not an image, skip", url);
                    return;
                }

                await File.WriteAllBytesAsync(path, bytes, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AniStrm] Download image failed: " + ex.Message);
            }
        }

        private static string? ChooseHls(HlsBlock? hls, string pref, string? host)
        {
            if (hls == null || string.IsNullOrEmpty(host))
                return null;

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
            if (string.IsNullOrEmpty(s)) return string.Empty;
        
            var invalid = Path.GetInvalidFileNameChars();
            var sb      = new StringBuilder(s.Length);
        
            foreach (var ch in s)
                sb.Append(Array.IndexOf(invalid, ch) >= 0 ? ' ' : ch);   // ← пробел вместо '_'
        
            // схлопываем подряд идущие пробелы / точки / дефисы
            var tmp = Regex.Replace(sb.ToString(), @"[ \t\.\-]{2,}", " ").Trim();
            return tmp;
        }


        private static string MakeSafeXml(string? text) =>
            SecurityElement.Escape(text) ?? string.Empty;

        // ────────────────── HLS duration helper ────────────────────
        private static readonly ConcurrentDictionary<string, double> _hlsDurationCache = new();

        private static async Task<double> GetHlsDurationAsync(string url, CancellationToken ct)
        {
            if (_hlsDurationCache.TryGetValue(url, out var cached))
                return cached;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var playlist = await http.GetStringAsync(url, ct);

                double sum = 0;
                foreach (var line in playlist.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!line.StartsWith("#EXTINF:", StringComparison.Ordinal)) continue;
                    var segTxt = line.Substring("#EXTINF:".Length);
                    var comma = segTxt.IndexOf(',');
                    if (comma >= 0) segTxt = segTxt[..comma];
                    if (double.TryParse(segTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var seg))
                        sum += seg;
                }

                _hlsDurationCache[url] = sum;
                return sum;
            }
            catch (Exception ex)
            {
                // ошибку логируем 1 раз, потом считаем 0 чтобы не тормозить
                Console.WriteLine("[AniStrm] GetHlsDuration failed: " + ex.Message);
                _hlsDurationCache[url] = 0;
                return 0;
            }
        }
    }
}
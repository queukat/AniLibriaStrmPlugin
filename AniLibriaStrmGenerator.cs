// ===== UPDATED FILE: AniLibriaStrmGenerator.cs =====
// 2025-07-15 — устранено «спам»-404/429: вместо загрузки .m3u8 для
//              расчёта длины эпизода используем поле Duration,
//              а к HLS-плейлисту обращаемся ТОЛЬКО если Duration==0.
//              Это в разы уменьшает количество запросов и убирает 429.

using AniLibriaStrmPlugin.Models;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Chapters;
using MediaBrowser.Model.Entities;

namespace AniLibriaStrmPlugin
{
    public interface IAniLibriaStrmGenerator
    {
        Task GenerateTitlesAsync(IEnumerable<ReleaseResponse> titles,
            string basePath,
            string resolution,
            IProgress<double>? progress,
            CancellationToken token);
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
            if (string.IsNullOrWhiteSpace(name)) return name;

            var cleaned = _suffixRules.Aggregate(name, (current, rx) => rx.Replace(current, ""));
            cleaned = Regex.Replace(cleaned, @"[\s\.\-_()]+$", "");
            return cleaned.Trim();
        }

        // ────────────────────── 1.1 detect numeric season ─────────────────
        private static readonly Regex _rxSeasonEng = new(@"\bSeason\s*(\d{1,2})\b",
            RegexOptions.IgnoreCase);

        private static readonly Regex _rxTrailingNum = new(@"(?:\s|\D)(\d{1,2})\s*$",
            RegexOptions.IgnoreCase);

        private static int DetectSeasonNumber(ReleaseResponse rel)
        {
            int TryParse(string? title)
            {
                if (string.IsNullOrWhiteSpace(title)) return 0;

                var m = _rxSeasonEng.Match(title);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var n1)) return n1;

                m = _rxTrailingNum.Match(title);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var n2)) return n2;

                return 0;
            }

            var num = TryParse(rel.Name?.English);
            if (num == 0) num = TryParse(rel.Name?.Main);
            if (num == 0) num = 1;
            return num;
        }

        // ───────────────────────── 2. публичный API ───────────────────────
        public async Task GenerateTitlesAsync(IEnumerable<ReleaseResponse> titles,
            string basePath,
            string resolution,
            IProgress<double>? progress,
            CancellationToken token)
        {
            Directory.CreateDirectory(basePath);

            var list = titles.ToList();
            var total = list.Count;
            var current = 0;

            foreach (var rel in list)
            {
                token.ThrowIfCancellationRequested();
                current++;

                var display = rel.Name?.English ?? rel.Name?.Main ?? rel.Alias;
                _log.LogInformation("({Cur}/{Tot}) {Name}", current, total, display);
                await GenerateStrmForTitle(rel, basePath, resolution, token);
                progress?.Report(current / (double)total * 100);
            }
        }

        // ─────────────────────── 3. один релиз → файлы ─────────────────────
        private async Task GenerateStrmForTitle(
            ReleaseResponse rel,
            string basePath,
            string resolution,
            CancellationToken token)
        {
            if (rel.Episodes is null || rel.Episodes.Count == 0)
            {
                _log.LogInformation("Skip {Id} – no episodes", rel.Id);
                return;
            }

            // ---- имена -----------------------------------------------------
            var ruName = rel.Name?.Main?.Trim();
            var engName = rel.Name?.English?.Trim();
            var altName = rel.Name?.Alternative?.Trim();

            var rawName = engName ?? ruName ?? rel.Alias ?? $"Title_{rel.Id}";
            var safeName = MakeSafe(CleanShowName(rawName)).ToLowerInvariant();

            // ---- номер сезона ---------------------------------------------
            var seasonNum = DetectSeasonNumber(rel);

            // ---- директории ------------------------------------------------
            var showDir = Path.Combine(basePath, safeName);
            Directory.CreateDirectory(showDir);

            var seasonFolder = $"Season {seasonNum:00}";
            var seasonDir = Path.Combine(showDir, seasonFolder);
            Directory.CreateDirectory(seasonDir);

            // ---- постер ----------------------------------------------------
            var posterUrl = MakeFullUrl(rel.Poster?.Src ?? rel.Poster?.Preview ?? "");

            await DownloadIfAbsentAsync(posterUrl, Path.Combine(showDir, "folder.jpg"), token);
            await DownloadIfAbsentAsync(posterUrl, Path.Combine(showDir, $"{seasonFolder}-poster.jpg"), token);

            // ---- tvshow.nfo ------------------------------------------------
            var tvshowNfo = Path.Combine(showDir, "tvshow.nfo");
            if (!File.Exists(tvshowNfo))
            {
                var displayTitle = ruName ?? engName ?? safeName;
                var originalTitle = altName ?? engName ?? displayTitle;
                var sortTitle = engName ?? ruName ?? displayTitle;
                var plot = MakeSafeXml(rel.Description?.Trim() ?? string.Empty);

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

            // ---- season.nfo ------------------------------------------------
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

            var autoNumber = 1; // fallback для эпизодов без ordinal

            foreach (var ep in rel.Episodes)
            {
                token.ThrowIfCancellationRequested();

                var epNum = ep.Ordinal ?? autoNumber++;
                var url = ChooseHls(ep, resolution);
                if (string.IsNullOrEmpty(url)) continue;

                var strmFile = $"S{seasonNum:00}E{epNum:00}.strm";
                var strmPath = Path.Combine(seasonDir, strmFile);
                if (!File.Exists(strmPath))
                    await File.WriteAllTextAsync(strmPath, url, token);

                // миниатюра серии
                if (!string.IsNullOrWhiteSpace(ep.Preview?.Src))
                {
                    var previewUrl = MakeFullUrl(ep.Preview.Src);
                    var ext = Path.GetExtension(previewUrl);
                    if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                    var thumbPath = Path.Combine(seasonDir, $"S{seasonNum:00}E{epNum:00}-thumb{ext}");
                    await DownloadIfAbsentAsync(previewUrl, thumbPath, token);
                }

                // ───── Skip-Intro / Credits ─────
                var segments = new List<(int start, int stop, string name)>
                {
                    (ep.Opening?.Start ?? -1, ep.Opening?.Stop ?? -1, "Intro"),
                    (ep.Ending ?.Start ?? -1, ep.Ending ?.Stop ?? -1, "Credits")
                }.Where(s => s.start >= 0 && s.stop > s.start)
                 .ToList();
                
                if (segments.Count > 0)
                {
                    /* ---------- 1. .edl (для авто-пропуска в трансляции) ---------- */
                    var edlPath = Path.ChangeExtension(strmPath, ".edl");
                    if (!File.Exists(edlPath))
                        await File.WriteAllLinesAsync(edlPath,
                            segments.Select(s => $"{s.start} {s.stop} 0"), token);
                
                    /* ---------- 2. chapters.xml (Jellyfin берёт главы из sidecar) ---------- */
                    var chXml = Path.ChangeExtension(strmPath, ".chapters.xml");
                    if (!File.Exists(chXml))
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine(@"<?xml version=""1.0"" encoding=""utf-8""?>");
                        sb.AppendLine("<chapters>");
                        foreach (var (start, _, name) in segments)
                        {
                            sb.AppendLine("  <chapter>");
                            sb.AppendLine($"    <name>{name}</name>");
                            sb.AppendLine($"    <time>{TimeSpan.FromSeconds(start):hh\\:mm\\:ss\\.fff}</time>");
                            sb.AppendLine("  </chapter>");
                        }
                        sb.AppendLine("</chapters>");
                        await File.WriteAllTextAsync(chXml, sb.ToString(), Encoding.UTF8, token);
                    }
                
                    /* ---------- 3. Пытаемся сразу записать главы в БД, если объект уже известен ---------- */
                    var runtimeSec = ep.Duration > 0 ? ep.Duration
                                                     : await GetHlsDurationAsync(url, token);
                
                    if (runtimeSec > segments.Max(s => s.stop) + 1)
                    {
                        try
                        {
                            if (_library.FindByPath(strmPath, isFolder: false) is Video item)
                            {
                                var chapters = segments.Select(s => new ChapterInfo
                                {
                                    Name = s.name,
                                    StartPositionTicks = TimeSpan.FromSeconds(s.start).Ticks
                                }).ToArray();
                
                #if JF_10_10
                                _chapters.SaveChapters(item.Id, chapters);
                #else
                                var existing = _chapters.GetChapters(item.Id);
                                
                                bool same = existing.Count >= chapters.Length &&
                                            existing.Take(chapters.Length)
                                                    .Select((c, i) => c.StartPositionTicks == chapters[i].StartPositionTicks)
                                                    .All(b => b);
                                
                                if (!same)
                                    _chapters.SaveChapters(item, chapters);
                #endif
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Unable to save chapters for {File}", strmPath);
                        }
                    }
                }


                // episode.nfo -------------------------------------------------
                var nfoPath = Path.ChangeExtension(strmPath, ".nfo");
                if (!File.Exists(nfoPath))
                {
                    var epRu = $"Episode {epNum}";
                    var showTitle = ruName ?? engName ?? safeName;
                    var xml = $@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
            <episodedetails>
              <title>{MakeSafeXml(epRu)}</title>
              <season>{seasonNum}</season>
              <episode>{epNum}</episode>
              <showtitle>{MakeSafeXml(showTitle)}</showtitle>
            </episodedetails>";
                    await File.WriteAllTextAsync(nfoPath, xml, Encoding.UTF8, token);
                }
            }
        }

        // ─────────────────────── helpers ─────────────────────────────
        // (DownloadIfAbsentAsync, ChooseHls, MakeFullUrl, MakeSafe, MakeSafeXml,
        //  GetHlsDurationAsync — без изменений)

        private static async Task DownloadIfAbsentAsync(string url, string path, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(url) || File.Exists(path)) return;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-AniLibertyStrm/1.0 (+https://github.com)");


                var resp = await http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return;

                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 4) return;
                var isJpg = bytes[0] == 0xFF && bytes[1] == 0xD8;
                var isPng = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
                if (!isJpg && !isPng) return;

                await File.WriteAllBytesAsync(path, bytes, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AniStrm] Download image failed: " + ex.Message);
            }
        }

        private static string? ChooseHls(EpisodeItem ep, string pref) =>
            pref switch
            {
                "1080" => ep.Hls1080 ?? ep.Hls720 ?? ep.Hls480,
                "720" => ep.Hls720 ?? ep.Hls1080 ?? ep.Hls480,
                _ => ep.Hls480 ?? ep.Hls720 ?? ep.Hls1080
            };

        private static string MakeFullUrl(string url) =>
            string.IsNullOrWhiteSpace(url) ? url :
            Uri.IsWellFormedUriString(url, UriKind.Absolute) ? url :
            "https://api.anilibria.app" + url;

        private static string MakeSafe(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);

            foreach (var ch in s)
                sb.Append(Array.IndexOf(invalid, ch) >= 0 ? ' ' : ch);

            var tmp = Regex.Replace(sb.ToString(), @"[ \t\.\-]{2,}", " ").Trim();
            return tmp;
        }

        private static string MakeSafeXml(string? text) =>
            System.Security.SecurityElement.Escape(text) ?? string.Empty;

        // — HLS duration cache/logic remain unchanged —
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
                    var segTxt = line["#EXTINF:".Length..];
                    var comma = segTxt.IndexOf(',');
                    if (comma >= 0) segTxt = segTxt[..comma];
                    if (double.TryParse(segTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var seg))
                        sum += seg;
                }

                _hlsDurationCache[url] = sum;
                return sum;
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode == 429)
            {
                // Too Many Requests – кешируем 0 и не шумим
                _hlsDurationCache[url] = 0;
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AniStrm] GetHlsDuration failed: " + ex.Message);
                _hlsDurationCache[url] = 0;
                return 0;
            }
        }
    }
}
using System.Collections.Concurrent;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using AniLibertyStrmPlugin.Models;
using AniLibertyStrmPlugin.Utils;
using MediaBrowser.Controller.Chapters;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin;

public interface IAniLibertyStrmGenerator
{
    Task GenerateTitlesAsync(
        IEnumerable<ReleaseResponse> titles,
        string basePath,
        string resolution,
        IProgress<double>? progress,
        CancellationToken token);
}

public sealed class AniLibertyStrmGenerator(
    ILogger<AniLibertyStrmGenerator> log,
    ILibraryManager library,
    IChapterManager chapters,
    IAniLibertyClient client)
    : IAniLibertyStrmGenerator
{
    // ────────────────────── 1.  очистка суффиксов ──────────────────────
    private static readonly Regex[] SuffixRules =
    {
        new(@"\s*(?:Season)\s*\d+\b.*$", RegexOptions.IgnoreCase),
        new(@"\s*\d+(?:st|nd|rd|th)?\s*Season\b.*$", RegexOptions.IgnoreCase),
        new(@"\s*(?:Part|Cour)\s*\d+\b.*$", RegexOptions.IgnoreCase),
        new(@"\s*\d+(?:st|nd|rd|th)?\s*Cour\b.*$", RegexOptions.IgnoreCase),
        new(@"\s*[-._ ]+(?:I{2,3}|IV|V?I{0,3}|VII?)$", RegexOptions.IgnoreCase),
        new(@"\s+[2-4]$", RegexOptions.IgnoreCase),
        new(@"\s+(?:OAD|OVA|OAV|Specials?|Movie)$", RegexOptions.IgnoreCase),

        // NEW: сносим "Ω/omega/омега" в конце
        new(@"\s*(?:Ω|ω|Omega|Омега)\b.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    };

    // ────────────────────── 1.1 detect numeric season ─────────────────
    private static readonly Regex _rxSeasonEng = new(@"\bSeason\s*(\d{1,2})\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex _rxTrailingNum = new(@"(?:\s|\D)(\d{1,2})\s*$",
        RegexOptions.IgnoreCase);

    // ────────────── вспомогательное: квартал для сортировки ───────────
    private static readonly Dictionary<string, int> _seasonOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["winter"] = 1, ["spring"] = 2, ["summer"] = 3, ["autumn"] = 4
    };

    // ─────────────────────── франшиза → номер сезона ───────────────────
    private static readonly ConcurrentDictionary<int, List<FranchiseInfo>?> _franchiseCache = new();

    private static readonly Regex _rxNonSeasonTv = new(
        @"\b(?:
        specials? |
        спецвыпуск(?:и|а)? |
        episode\s*0 |
        episode\s*zero |
        нулевая\s*серия |
        recap
    )\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace);

    private static readonly ConcurrentDictionary<string, double> _hlsDurationCache = new();

    // ────────────────────── 2.  API ───────────────────────
    public async Task GenerateTitlesAsync(
        IEnumerable<ReleaseResponse> titles,
        string basePath,
        string resolution,
        IProgress<double>? progress,
        CancellationToken token)
    {
        if (titles == null) return;

        // Преобразуем к списку, чтобы знать Count и удобнее логировать/прогресс
        var list = titles as IList<ReleaseResponse> ?? titles.ToList();
        if (list.Count == 0) return;

        // Fallback‑нумерация по году внутри групп одинаковых имён
        var fallbackById = new Dictionary<int, int>();
        foreach (var grp in list.GroupBy(GroupKey))
        {
            var ordered = grp
                .OrderBy(r => r.Season?.Year > 0 ? r.Season!.Year : int.MaxValue)
                .ThenBy(r => QuarterIndex(r.Season?.Value))
                .ThenBy(r => r.Name?.English ?? r.Name?.Main ?? r.Alias ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Id)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
                fallbackById[ordered[i].Id] = i + 1;
        }

        var total = list.Count;
        var current = 0;

        foreach (var rel0 in list)
        {
            token.ThrowIfCancellationRequested();
            current++;

            var display = rel0.Name?.English ?? rel0.Name?.Main ?? rel0.Alias;
            log.Info("({0}/{1}) \"{2}\"", current, total, display);

            // Гидратация (если нет эпизодов в карточке каталога)
            var rel = rel0;
            if (rel.Episodes is null || rel.Episodes.Count == 0)
                try
                {
                    var full = await client.FetchReleaseByIdAsync(rel.Id, token);
                    if (full?.Episodes?.Count > 0)
                    {
                        rel = full;
                    }
                    else
                    {
                        log.LogInformation("Skip {Id} – no episodes in detail", rel.Id);
                        progress?.Report(current / (double)total * 100.0);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Skip {Id} – failed to fetch details", rel.Id);
                    progress?.Report(current / (double)total * 100.0);
                    continue;
                }

            var displayRaw = rel.Name?.English ?? rel.Name?.Main ?? rel.Alias ?? "";
            if (LooksLikeMovie(rel, displayRaw))
            {
                await GenerateMovieAsync(rel, basePath, resolution, token);

                async Task GenerateMovieAsync(ReleaseResponse rel, string basePath, string resolution,
                    CancellationToken token)
                {
                    // Берём единственный эпизод как «файл» фильма
                    var ep = rel.Episodes?.FirstOrDefault();
                    if (ep == null)
                    {
                        log.Info("Skip {0} – no movie episode", rel.Id);
                        return;
                    }

                    var ruName = rel.Name?.Main?.Trim();
                    var engName = rel.Name?.English?.Trim();
                    var raw = engName ?? ruName ?? rel.Alias ?? $"Movie_{rel.Id}";

                    // вычистим слово "movie", нормализуем "Code: White" → "Code White", уберём двоеточия
                    var title = Regex.Replace(raw, @"\b(the\s*movie|movie|film)\b", "", RegexOptions.IgnoreCase);
                    title = Regex.Replace(title, @"\bcode\s*[:\-]\s*", "Code ", RegexOptions.IgnoreCase);
                    title = NormalizeTitleForFs(title);

                    var year = rel.Season?.Year > 0 ? rel.Season!.Year : DateTime.UtcNow.Year;
                    var folder = $"{title} ({year})";
                    var movieDir = Path.Combine(basePath, folder);
                    Directory.CreateDirectory(movieDir);

                    var url = ChooseHls(ep, resolution);
                    if (string.IsNullOrEmpty(url)) return;

                    var strmPath = Path.Combine(movieDir, $"{folder}.strm");
                    if (!File.Exists(strmPath))
                        await File.WriteAllTextAsync(strmPath, url, token);

                    // poster
                    var posterUrl = MakeFullUrl(rel.Poster?.Src ?? rel.Poster?.Preview ?? "");
                    await DownloadIfAbsentAsync(posterUrl, Path.Combine(movieDir, "cover.jpg"), token);

                    // простейший movie.nfo
                    var plot = MakeSafeXml(rel.Description ?? "");
                    var orig = engName ?? ruName ?? title;
                    var nfo = $@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
                     <movie>
                       <title>{MakeSafeXml(title)}</title>
                       {(orig != title ? $"<originaltitle>{MakeSafeXml(orig)}</originaltitle>" : "")}
                       <year>{year}</year>
                       {(plot.Length > 0 ? $"<plot>{plot}</plot>" : "")}
                     </movie>";
                    var nfoPath = Path.Combine(movieDir, $"{folder}.nfo");
                    if (!File.Exists(nfoPath))
                        await File.WriteAllTextAsync(nfoPath, nfo, Encoding.UTF8, token);
                }
            }
            else
            {
                await GenerateStrmForTitle(rel, basePath, resolution, fallbackById, list, token);
            }

            progress?.Report(current / (double)total * 100.0);
        }
    }

    private static string CleanShowName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;

        var cleaned = SuffixRules.Aggregate(name, (current, rx) => rx.Replace(current, ""));
        cleaned = Regex.Replace(cleaned, @"[\s\.\-_()]+$", "");
        // На всякий случай: единичные Ω внутри — убираем
        cleaned = Regex.Replace(cleaned, @"[\u03A9\u03C9]", "");
        return cleaned.Trim();
    }

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

    private static int QuarterIndex(string? v)
    {
        return v != null && _seasonOrder.TryGetValue(v, out var k) ? k : 99;
    }

    // Предкалькуляция ключа группы (очищенное, нормализованное имя)
    private static string GroupKey(ReleaseResponse r)
    {
        var ruName = r.Name?.Main?.Trim();
        var engName = r.Name?.English?.Trim();
        var rawName = engName ?? ruName ?? r.Alias ?? $"Title_{r.Id}";
        // specials убираем из ключа
        rawName = Regex.Replace(rawName, @"\b(?:specials?)\b", "", RegexOptions.IgnoreCase).Trim();
        return NormalizeTitleForFs(rawName).ToLowerInvariant();
    }

    private static bool LooksLikeMovie(ReleaseResponse rel, string title)
    {
        var oneEp = (rel.Episodes?.Count ?? 0) <= 1 || rel.EpisodesTotal.GetValueOrDefault(0) <= 1;
        var hasMovieWord = Regex.IsMatch(title, @"\b(movie|film|the\s*movie)\b", RegexOptions.IgnoreCase);
        return oneEp && hasMovieWord;
    }

    // ─────────────────────── 3. генерация STRM → файлы ─────────────────────
    private async Task GenerateStrmForTitle(
        ReleaseResponse rel,
        string basePath,
        string resolution,
        Dictionary<int, int> fallbackById,
        IList<ReleaseResponse> allList,
        CancellationToken token)
    {
        if (rel.Episodes is null || rel.Episodes.Count == 0)
        {
            log.Info("Skip {0} – no episodes", rel.Id);
            return;
        }

        // ---- имена -----------------------------------------------------
        var ruName = rel.Name?.Main?.Trim();
        var engName = rel.Name?.English?.Trim();
        var altName = rel.Name?.Alternative?.Trim();

        var rawName = engName ?? ruName ?? rel.Alias ?? $"Title_{rel.Id}";
        // specials? -> вынести из имени и отправить в Season 00
        var isSpecialsTitle = Regex.IsMatch(rawName, @"\b(?:specials?)\b", RegexOptions.IgnoreCase);
        if (isSpecialsTitle)
            rawName = Regex.Replace(rawName, @"\b(?:specials?)\b", "", RegexOptions.IgnoreCase).Trim();
        var safeName = NormalizeTitleForFs(rawName).ToLowerInvariant();

        // ---- сезон -----------------------------------------------------
        int seasonNum;
        var hasFranchiseSeason = false;

        if (isSpecialsTitle)
        {
            seasonNum = 0;
        }
        else
        {
            // 1) пытаемся получить сезон из франшизы
            seasonNum = await DetectSeasonFromFranchiseAsync(rel.Id, token);
            hasFranchiseSeason = seasonNum > 0;

            // 2) если не удалось — парсим из названия (как было)
            if (seasonNum <= 0)
                seasonNum = DetectSeasonNumber(rel);

            // 3) если всё ещё «первый» и в группе есть дубликаты — используем fallback‑порядок по году
            if (!hasFranchiseSeason && seasonNum <= 1)
            {
                var key = GroupKey(rel);
                var sameGroupCount = allList.Count(x => GroupKey(x) == key);
                if (sameGroupCount > 1 && fallbackById.TryGetValue(rel.Id, out var nByYear))
                    seasonNum = nByYear;
            }
        }

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

        var autoNumber = 1; // fallback порядковый номер

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

            // превью
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
                    (ep.Ending?.Start ?? -1, ep.Ending?.Stop ?? -1, "Credits")
                }.Where(s => s.start >= 0 && s.stop > s.start)
                .ToList();

            if (segments.Count > 0)
            {
                // 1) .edl (skip секции)
                var edlPath = Path.ChangeExtension(strmPath, ".edl");
                if (!File.Exists(edlPath))
                    await File.WriteAllLinesAsync(edlPath,
                        segments.Select(s => $"{s.start} {s.stop} 0"), token);

                // 2) chapters.xml (sidecar главы Jellyfin)
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

                // 3) запись глав в библиотеку, если Item уже проиндексирован
                var runtimeSec = ep.Duration > 0
                    ? ep.Duration
                    : await GetHlsDurationAsync(url, token);

                if (runtimeSec > segments.Max(s => s.stop) + 1)
                    try
                    {
                        if (library.FindByPath(strmPath, false) is Video item)
                        {
                            var chapters1 = segments.Select(s => new ChapterInfo
                            {
                                Name = s.name,
                                StartPositionTicks = TimeSpan.FromSeconds(s.start).Ticks
                            }).ToArray();

#if JF_10_10
                                _chapters.SaveChapters(item.Id, chapters);
#else
                            var existing = chapters.GetChapters(item.Id);
                            var same = existing.Count >= chapters1.Length &&
                                       existing.Take(chapters1.Length)
                                           .Select((c, i) =>
                                               c.StartPositionTicks == chapters1[i].StartPositionTicks)
                                           .All(b => b);

                            if (!same)
                                chapters.SaveChapters(item, chapters1);
#endif
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Warn(ex, "Unable to save chapters for {0}", strmPath);
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

    private async Task<int> DetectSeasonFromFranchiseAsync(int releaseId, CancellationToken ct)
    {
        if (!_franchiseCache.TryGetValue(releaseId, out var frList))
        {
            frList = await client.FetchFranchisesForReleaseAsync(releaseId, ct);
            _franchiseCache[releaseId] = frList;
        }

        return ComputeSeasonFromFranchises(frList, releaseId);
    }

    private static bool IsNonSeasonTv(FranchiseReleaseLink link)
    {
        // интересуют только TV-релизы
        if (!string.Equals(link.Release?.Type?.Value, "TV", StringComparison.OrdinalIgnoreCase))
            return false;

        var en = link.Release?.Name?.English ?? string.Empty;
        var ru = link.Release?.Name?.Main ?? string.Empty;
        var alt = link.Release?.Name?.Alternative ?? string.Empty;

        var text = $"{en} {ru} {alt}";
        return _rxNonSeasonTv.IsMatch(text);
    }

    private static int ComputeSeasonFromFranchises(List<FranchiseInfo>? list, int releaseId)
    {
        if (list is null || list.Count == 0) return 0;

        // Берём франшизу, где больше всего релизов (и которая содержит текущий)
        var candidates = list.Where(f => f.FranchiseReleases?.Any(x => x.ReleaseId == releaseId) == true).ToList();
        if (candidates.Count == 0) candidates = list;

        var best = candidates
            .OrderByDescending(f => f.FranchiseReleases?.Count ?? 0)
            .FirstOrDefault();

        if (best == null || best.FranchiseReleases == null || best.FranchiseReleases.Count == 0)
            return 0;

        var entries = best.FranchiseReleases;

        // 1) сначала оставляем только TV, если текущий релиз тоже TV
        var onlyTv = entries
            .Where(e => string.Equals(e.Release?.Type?.Value, "TV", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (onlyTv.Any(e => e.ReleaseId == releaseId))
            entries = onlyTv;

        // 2) выкидываем спецвыпуски / recaps / нулевые серии из TV,
        //    но только если после фильтрации наш релиз всё ещё внутри.

        var filtered = entries.Where(e => !IsNonSeasonTv(e)).ToList();
        if (filtered.Any(e => e.ReleaseId == releaseId))
            entries = filtered;
        
        var sorted = entries
            .OrderBy(e => e.SortOrder ?? int.MaxValue)
            .ThenBy(e => e.Release?.Year ?? int.MaxValue)
            .ThenBy(e => e.ReleaseId)
            .ToList();

        var idx = sorted.FindIndex(e => e.ReleaseId == releaseId);
        return idx >= 0 ? idx + 1 : 0;
    }

    private static string NormalizeTitleForFs(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        // унифицируем символ x и выкидываем дефисы/тире
        s = s.Replace('×', 'x').Replace('Ｘ', 'x');
        s = s.Replace("-", " ").Replace("–", " ").Replace("—", " ");
        // убираем одиночные омеги
        s = s.Replace("Ω", "").Replace("ω", "");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return MakeSafe(CleanShowName(s));
    }

    // ─────────────────────── helpers ─────────────────────────────

    private async Task DownloadIfAbsentAsync(string url, string path, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(url) || File.Exists(path)) return;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Jellyfin-AniLibertyStrm/1.0 (+https://github.com/queukat/AniLibertyStrmPlugin)");

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
            log.Warn(ex, "Download image failed: {0}", url);
        }
    }

    private static string? ChooseHls(EpisodeItem ep, string pref)
    {
        return pref switch
        {
            "1080" => ep.Hls1080 ?? ep.Hls720 ?? ep.Hls480,
            "720" => ep.Hls720 ?? ep.Hls1080 ?? ep.Hls480,
            _ => ep.Hls480 ?? ep.Hls720 ?? ep.Hls1080
        };
    }

    private static string MakeFullUrl(string url)
    {
        return string.IsNullOrWhiteSpace(url) ? url :
            Uri.IsWellFormedUriString(url, UriKind.Absolute) ? url :
            "https://api.anilibria.app" + url;
    }

    private static string MakeSafe(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);

        foreach (var ch in s)
        {
            if (ch == '-' || ch == '–' || ch == '—')
            {
                sb.Append(' ');
                continue;
            }

            if (ch == '×')
            {
                sb.Append('x');
                continue;
            }

            // NEW: убираем греческие омеги
            if (ch == 'Ω' || ch == 'ω') continue;

            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? ' ' : ch);
        }

        var tmp = Regex.Replace(sb.ToString(), @"[ \t\.\-]{2,}", " ").Trim();
        return tmp;
    }

    private static string MakeSafeXml(string? text)
    {
        return SecurityElement.Escape(text) ?? string.Empty;
    }

    private async Task<double> GetHlsDurationAsync(string url, CancellationToken ct)
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
            _hlsDurationCache[url] = 0;
            return 0;
        }
        catch (Exception ex)
        {
            log.Warn(ex, "GetHlsDuration failed: {0}", url);
            _hlsDurationCache[url] = 0;
            return 0;
        }
    }
}
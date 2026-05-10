using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using AniLibertyStrmPlugin.Configuration;
using AniLibertyStrmPlugin.Models;
using AniLibertyStrmPlugin.Utils;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
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
    IServerApplicationHost serverHost,
    INetworkManager networkManager,
    ILibraryManager library,
    IChapterManager chapters,
    IAniLibertyClient client)
    : IAniLibertyStrmGenerator
{
    // ────────────────────── 1. suffix cleanup ──────────────────────
    private static readonly Regex[] SuffixRules =
    {
        new(@"\s*(?:Season)\s*\d+\b.*$", RegexOptions.IgnoreCase),
        new(@"\s*\d+(?:st|nd|rd|th)?\s*Season\b.*$", RegexOptions.IgnoreCase),
        new(@"\s*(?:Part|Cour)\s*\d+\b.*$", RegexOptions.IgnoreCase),
        new(@"\s*\d+(?:st|nd|rd|th)?\s*Cour\b.*$", RegexOptions.IgnoreCase),
        new(@"\s*[-._ ]+(?:I{2,3}|IV|V?I{0,3}|VII?)$", RegexOptions.IgnoreCase),
        new(@"\s+[2-4]$", RegexOptions.IgnoreCase),
        new(@"\s+(?:OAD|OVA|OAV|Specials?|Movie)$", RegexOptions.IgnoreCase),

        // NEW: strip trailing Omega suffix variants
        new(@"\s*(?:Ω|ω|Omega|Омега)\b.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    };

    // ────────────────────── 1.1 detect numeric season ─────────────────
    private static readonly Regex _rxSeasonEng = new(@"\bSeason\s*(\d{1,2})\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex _rxTrailingNum = new(@"(?:\s|\D)(\d{1,2})\s*$",
        RegexOptions.IgnoreCase);

    // ────────────── helper: quarter index for sorting ───────────
    private static readonly Dictionary<string, int> _seasonOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["winter"] = 1,
        ["spring"] = 2,
        ["summer"] = 3,
        ["autumn"] = 4
    };

    // ─────────────────────── franchise -> season number ───────────────────
    private static readonly ConcurrentDictionary<int, List<FranchiseInfo>?> _franchiseCache = new();

    // Keywords that mark a TV release as NOT a regular season
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
    private static readonly ConcurrentDictionary<string, string> _hlsProbeCache = new();

    // ────────────────────── 2.  media http (reuse) ──────────────────────
    private static readonly HttpClient _mediaHttp = CreateMediaHttp(TimeSpan.FromSeconds(20));
    private static readonly HttpClient _hlsHttp = CreateMediaHttp(TimeSpan.FromSeconds(10));

    private static HttpClient CreateMediaHttp(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(PluginIdentity.UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru,en;q=0.8");
        return http;
    }

    // ────────────────────── 3.  API ───────────────────────
    public async Task GenerateTitlesAsync(
        IEnumerable<ReleaseResponse> titles,
        string basePath,
        string resolution,
        IProgress<double>? progress,
        CancellationToken token)
    {
        if (titles == null) return;
        if (string.IsNullOrWhiteSpace(basePath))
        {
            log.Warn("Skip generation because base path is empty.");
            return;
        }

        var list = titles as IList<ReleaseResponse> ?? titles.ToList();
        if (list.Count == 0) return;

        var debugLogs = Plugin.Instance?.Configuration?.EnableDebugLogs == true;
        var supportTrace = Plugin.Instance?.Configuration?.EnableRawSupportLogs == true;
        var playbackDiag = Plugin.Instance?.Configuration?.EnablePlaybackDiagnostics == true;
        var cleanupMode = Plugin.Instance?.Configuration?.StaleCleanupMode ?? StaleCleanupMode.DryRun;
        var manifest = await ManagedLibraryManifest.LoadAsync(basePath, token);

        // Fallback numbering by year within groups of same titles
        // In v1, year is on release level (field "year"), not season.year
        var fallbackById = new Dictionary<int, int>();
        foreach (var grp in list.GroupBy(GroupKey))
        {
            var ordered = grp
                .OrderBy(r => r.Year > 0 ? r.Year : int.MaxValue)
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

            // By default, do NOT spam per-title logs to avoid bloating the UI log.
            // When Debug logs = ON, log each title; otherwise: first, every 25th, and last.
            if (debugLogs || current == 1 || current == total || current % 25 == 0)
            {
                log.Info("({0}/{1}) \"{2}\"", current, total, display);
            }
            else if (supportTrace)
            {
                log.Debug("({0}/{1}) \"{2}\"", current, total, display);
            }

            // Hydration (when catalog card has no episodes)
            var rel = rel0;
            if (rel.Episodes is null || rel.Episodes.Count == 0)
            {
                try
                {
                    var full = await client.FetchReleaseByIdAsync(rel.Id, token);
                    if (full?.Episodes?.Count > 0)
                    {
                        rel = full;
                    }
                    else
                    {
                        log.Info("Skip {0} – no episodes in detail", rel.Id);
                        progress?.Report(current / (double)total * 100.0);
                        continue;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log.Warn(ex, "Skip {0} – failed to fetch details", rel.Id);
                    progress?.Report(current / (double)total * 100.0);
                    continue;
                }
            }

            var displayRaw = rel.Name?.English ?? rel.Name?.Main ?? rel.Alias ?? "";
            if (LooksLikeMovie(rel, displayRaw))
            {
                await GenerateMovieAsync(rel, basePath, resolution, playbackDiag, manifest, token);
            }
            else
            {
                await GenerateStrmForTitle(rel, basePath, resolution, fallbackById, list, playbackDiag, manifest, token);
            }

            progress?.Report(current / (double)total * 100.0);
        }

        await manifest.ApplyCleanupAsync(cleanupMode, log, token);
        await manifest.SaveAsync(cleanupMode, token);
    }

    private static string CleanShowName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;

        var cleaned = SuffixRules.Aggregate(name, (current, rx) => rx.Replace(current, ""));
        cleaned = Regex.Replace(cleaned, @"[\s\.\-_()]+$", "");
        cleaned = Regex.Replace(cleaned, @"[\u03A9\u03C9]", ""); // remove internal Ω characters
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

    private static string GroupKey(ReleaseResponse r)
    {
        var ruName = r.Name?.Main?.Trim();
        var engName = r.Name?.English?.Trim();
        var rawName = engName ?? ruName ?? r.Alias ?? $"Title_{r.Id}";
        rawName = Regex.Replace(rawName, @"\b(?:specials?)\b", "", RegexOptions.IgnoreCase).Trim();
        return NormalizeTitleForFs(rawName).ToLowerInvariant();
    }

    private static bool LooksLikeMovie(ReleaseResponse rel, string title)
    {
        // v1 has type.value (MOVIE), which is more reliable than matching title words.
        if (string.Equals(rel.Type?.Value, "MOVIE", StringComparison.OrdinalIgnoreCase))
            return true;

        var oneEp = (rel.Episodes?.Count ?? 0) <= 1 || rel.EpisodesTotal.GetValueOrDefault(0) <= 1;
        var hasMovieWord = Regex.IsMatch(title, @"\b(movie|film|the\s*movie)\b", RegexOptions.IgnoreCase);
        return oneEp && hasMovieWord;
    }

    // ─────────────────────── 4. STRM generation -> files ─────────────────────

    private async Task GenerateMovieAsync(
        ReleaseResponse rel,
        string basePath,
        string resolution,
        bool playbackDiag,
        ManagedLibraryManifest manifest,
        CancellationToken token)
    {
        var ep = rel.Episodes?.FirstOrDefault();
        if (ep == null)
        {
            log.Info("Skip {0} – no movie episode", rel.Id);
            return;
        }

        var ruName = rel.Name?.Main?.Trim();
        var engName = rel.Name?.English?.Trim();
        var raw = engName ?? ruName ?? rel.Alias ?? $"Movie_{rel.Id}";

        var title = Regex.Replace(raw, @"\b(the\s*movie|movie|film)\b", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\bcode\s*[:\-]\s*", "Code ", RegexOptions.IgnoreCase);
        title = NormalizeTitleForFs(title);

        var year = rel.Year > 0 ? rel.Year : DateTime.UtcNow.Year;
        var folder = $"{title} ({year})";
        var movieDir = Path.Combine(basePath, folder);
        Directory.CreateDirectory(movieDir);

        var strmPath = Path.Combine(movieDir, $"{folder}.strm");
        var selectedRaw = ChooseHls(ep, resolution) ?? string.Empty;
        var url = MakePlaybackUrl(selectedRaw);
        if (string.IsNullOrWhiteSpace(url))
        {
            if (playbackDiag)
                log.Warn("[PLAYBACK-DIAG] MOVIE relId={0} alias={1}: no HLS URL selected; hls1080=\"{2}\" hls720=\"{3}\" hls480=\"{4}\"",
                    rel.Id, rel.Alias, TrimForLog(ep.Hls1080), TrimForLog(ep.Hls720), TrimForLog(ep.Hls480));
            return;
        }

        if (playbackDiag)
            await LogPlaybackDiagnosticsAsync(
                $"MOVIE relId={rel.Id} alias={rel.Alias}",
                ep,
                resolution,
                selectedRaw,
                url,
                strmPath,
                token);

        await WriteManagedTextIfChangedAsync(
            manifest,
            strmPath,
            url,
            encoding: null,
            kind: "strm",
            releaseId: rel.Id,
            episodeId: ep.Id,
            source: selectedRaw,
            respectUnmanagedExisting: false,
            token);
        await WriteEpisodeIdSidecarAsync(manifest, strmPath, ep.Id, rel.Id, token);

        var posterUrl = NormalizeImageUrlPreferJpg(MakeFullUrl(PickImageUrl(rel.Poster)));
        await DownloadManagedImageAsync(
            manifest,
            posterUrl,
            Path.Combine(movieDir, "cover.jpg"),
            "movie-cover",
            rel.Id,
            ep.Id,
            token);

        var plot = MakeSafeXml(rel.Description ?? "");
        var orig = engName ?? ruName ?? title;
        var nfo = AddGeneratedXmlMarker($@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<movie>
  <title>{MakeSafeXml(title)}</title>
  {(orig != title ? $"  <originaltitle>{MakeSafeXml(orig)}</originaltitle>" : "")}
  <year>{year}</year>
  {(Guid.TryParse(ep.Id, out _) ? $"  <uniqueid type=\"aniliberty_episode_id\" default=\"false\">{MakeSafeXml(ep.Id)}</uniqueid>" : "")}
  {(plot.Length > 0 ? $"  <plot>{plot}</plot>" : "")}
  <lockdata>false</lockdata>
</movie>");

        var nfoPath = Path.Combine(movieDir, $"{folder}.nfo");
        await WriteManagedTextIfChangedAsync(
            manifest,
            nfoPath,
            nfo,
            Encoding.UTF8,
            "movie-nfo",
            rel.Id,
            ep.Id,
            source: null,
            respectUnmanagedExisting: true,
            token);
    }

    private async Task GenerateStrmForTitle(
        ReleaseResponse rel,
        string basePath,
        string resolution,
        Dictionary<int, int> fallbackById,
        IList<ReleaseResponse> allList,
        bool playbackDiag,
        ManagedLibraryManifest manifest,
        CancellationToken token)
    {
        if (rel.Episodes is null || rel.Episodes.Count == 0)
        {
            log.Info("Skip {0} – no episodes", rel.Id);
            return;
        }

        var ruName = rel.Name?.Main?.Trim();
        var engName = rel.Name?.English?.Trim();
        var altName = rel.Name?.Alternative?.Trim();

        var rawName = engName ?? ruName ?? rel.Alias ?? $"Title_{rel.Id}";

        // SPECIAL/OVA/OAD should go to Season 00 even if "special" is not present in title.
        var isSpecialsType =
            string.Equals(rel.Type?.Value, "SPECIAL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rel.Type?.Value, "OVA", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rel.Type?.Value, "OAD", StringComparison.OrdinalIgnoreCase);

        var isSpecialsTitle = isSpecialsType ||
                              Regex.IsMatch(rawName, @"\b(?:specials?)\b", RegexOptions.IgnoreCase);

        if (!isSpecialsType && isSpecialsTitle)
            rawName = Regex.Replace(rawName, @"\b(?:specials?)\b", "", RegexOptions.IgnoreCase).Trim();

        var safeName = NormalizeTitleForFs(rawName).ToLowerInvariant();

        // ---- season -----------------------------------------------------
        int seasonNum;
        var hasFranchiseSeason = false;

        if (isSpecialsTitle)
        {
            seasonNum = 0;
        }
        else
        {
            seasonNum = await DetectSeasonFromFranchiseAsync(rel.Id, token);
            hasFranchiseSeason = seasonNum > 0;

            if (seasonNum <= 0)
                seasonNum = DetectSeasonNumber(rel);

            if (!hasFranchiseSeason && seasonNum <= 1)
            {
                var key = GroupKey(rel);
                var sameGroupCount = allList.Count(x => GroupKey(x) == key);
                if (sameGroupCount > 1 && fallbackById.TryGetValue(rel.Id, out var nByYear))
                    seasonNum = nByYear;
            }
        }

        // ---- directories ------------------------------------------------
        var showDir = Path.Combine(basePath, safeName);
        Directory.CreateDirectory(showDir);

        var seasonFolder = $"Season {seasonNum:00}";
        var seasonDir = Path.Combine(showDir, seasonFolder);
        Directory.CreateDirectory(seasonDir);

        // ---- poster ----------------------------------------------------
        var posterUrl = NormalizeImageUrlPreferJpg(MakeFullUrl(PickImageUrl(rel.Poster)));
        await DownloadManagedImageAsync(manifest, posterUrl, Path.Combine(showDir, "folder.jpg"), "show-poster", rel.Id, null, token);
        await DownloadManagedImageAsync(manifest, posterUrl, Path.Combine(showDir, $"{seasonFolder}-poster.jpg"), "season-poster", rel.Id, null, token);

        // ---- tvshow.nfo ------------------------------------------------
        var tvshowNfo = Path.Combine(showDir, "tvshow.nfo");
        {
            var displayTitle = ruName ?? engName ?? safeName;
            var originalTitle = altName ?? engName ?? displayTitle;
            var sortTitle = engName ?? ruName ?? displayTitle;
            var plot = MakeSafeXml(rel.Description?.Trim() ?? string.Empty);

            // tvshow.nfo should include year, but showDir is shared across all seasons.
            // Use the minimum year across the group (if any).
            var gk = GroupKey(rel);
            var showYear = allList
                .Where(x => GroupKey(x) == gk)
                .Select(x => x.Year)
                .Where(y => y > 0)
                .DefaultIfEmpty(0)
                .Min();

            var xml = AddGeneratedXmlMarker($@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<tvshow>
  <title>{MakeSafeXml(displayTitle)}</title>
  {(originalTitle != displayTitle ? $"  <originaltitle>{MakeSafeXml(originalTitle)}</originaltitle>" : string.Empty)}
  {(sortTitle != displayTitle ? $"  <sorttitle>{MakeSafeXml(sortTitle)}</sorttitle>" : string.Empty)}
  {(showYear > 0 ? $"  <year>{showYear}</year>" : string.Empty)}
  {(plot.Length > 0 ? $"  <plot>{plot}</plot><outline>{plot}</outline>" : string.Empty)}
  <lockdata>false</lockdata>
</tvshow>");
            await WriteManagedTextIfChangedAsync(
                manifest,
                tvshowNfo,
                xml,
                Encoding.UTF8,
                "tvshow-nfo",
                rel.Id,
                episodeId: null,
                source: null,
                respectUnmanagedExisting: true,
                token);
        }

        // ---- season.nfo -----------------------------------------------
        var seasonNfo = Path.Combine(seasonDir, "season.nfo");
        {
            var seasonXml = AddGeneratedXmlMarker($@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<season>
  <title>Season {seasonNum}</title>
  <seasonnumber>{seasonNum}</seasonnumber>
  <lockdata>false</lockdata>
</season>");
            await WriteManagedTextIfChangedAsync(
                manifest,
                seasonNfo,
                seasonXml,
                Encoding.UTF8,
                "season-nfo",
                rel.Id,
                episodeId: null,
                source: null,
                respectUnmanagedExisting: true,
                token);
        }

        var autoNumber = 1; // fallback sequential episode number
        var usedEpisodeNumbers = new HashSet<int>();
        var useSortOrderFileNumbers = ShouldUseSortOrderFileNumbers(rel.Episodes);

        foreach (var ep in rel.Episodes)
        {
            token.ThrowIfCancellationRequested();

            var episodeId = ResolveEpisodeIdentity(ep, ref autoNumber, usedEpisodeNumbers, useSortOrderFileNumbers);
            var epNum = episodeId.FileEpisodeNumber;
            var strmFile = $"S{seasonNum:00}E{epNum:00}.strm";
            var strmPath = Path.Combine(seasonDir, strmFile);
            var selectedRaw = ChooseHls(ep, resolution) ?? string.Empty;
            var url = MakePlaybackUrl(selectedRaw);
            if (string.IsNullOrWhiteSpace(url))
            {
                if (playbackDiag)
                    log.Warn(
                        "[PLAYBACK-DIAG] TV relId={0} alias={1} S{2:00}E{3:00}: no HLS URL selected; hls1080=\"{4}\" hls720=\"{5}\" hls480=\"{6}\"",
                        rel.Id, rel.Alias, seasonNum, epNum,
                        TrimForLog(ep.Hls1080), TrimForLog(ep.Hls720), TrimForLog(ep.Hls480));
                continue;
            }

            if (playbackDiag)
                await LogPlaybackDiagnosticsAsync(
                    $"TV relId={rel.Id} alias={rel.Alias} S{seasonNum:00}E{epNum:00}",
                    ep,
                    resolution,
                    selectedRaw,
                    url,
                    strmPath,
                    token);

            await WriteManagedTextIfChangedAsync(
                manifest,
                strmPath,
                url,
                encoding: null,
                kind: "strm",
                releaseId: rel.Id,
                episodeId: ep.Id,
                source: selectedRaw,
                respectUnmanagedExisting: false,
                token);
            await WriteEpisodeIdSidecarAsync(manifest, strmPath, ep.Id, rel.Id, token);

            // preview image (v1: preview.preview / preview.thumbnail)
            var epPreviewUrlRaw = MakeFullUrl(PickImageUrl(ep.Preview));
            var epPreviewUrl = NormalizeImageUrlPreferJpg(epPreviewUrlRaw);

            if (!string.IsNullOrWhiteSpace(epPreviewUrl))
            {
                // IMPORTANT: URL often contains query (?x=..), and Path.GetExtension() returns ".jpg?..."
                var ext = GetSafeImageExtensionFromUrl(epPreviewUrl);
                var thumbPath = Path.Combine(seasonDir, $"S{seasonNum:00}E{epNum:00}-thumb{ext}");
                await DownloadManagedImageAsync(manifest, epPreviewUrl, thumbPath, "episode-thumb", rel.Id, ep.Id, token);
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
                var edlPath = Path.ChangeExtension(strmPath, ".edl");
                var edlContent = string.Join(
                    Environment.NewLine,
                    segments.Select(s => $"{s.start} {s.stop} 0"));
                await WriteManagedTextIfChangedAsync(
                    manifest,
                    edlPath,
                    edlContent,
                    Encoding.UTF8,
                    "edl",
                    rel.Id,
                    ep.Id,
                    source: null,
                    respectUnmanagedExisting: false,
                    token);

                var chXml = Path.ChangeExtension(strmPath, ".chapters.xml");
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
                await WriteManagedTextIfChangedAsync(
                    manifest,
                    chXml,
                    sb.ToString(),
                    Encoding.UTF8,
                    "chapters",
                    rel.Id,
                    ep.Id,
                    source: null,
                    respectUnmanagedExisting: false,
                    token);

                var runtimeSec = ep.Duration > 0
                    ? ep.Duration
                    : await GetHlsDurationAsync(url, token);

                // Outside Jellyfin (tests), library/chapters are null -> skip chapter updates.
                if (runtimeSec > segments.Max(s => s.stop) + 1 &&
                    library is not null &&
                    chapters is not null)
                {
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
                            chapters.SaveChapters(item.Id, chapters1);
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
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        log.Warn(ex, "Unable to save chapters for {0}", strmPath);
                    }
                }
            }

            // episode.nfo -------------------------------------------------
            // v1 has name/name_english + duration, but no episode plot/description.
            var nfoPath = Path.ChangeExtension(strmPath, ".nfo");
            {
                var showTitle = ruName ?? engName ?? safeName;

                var epTitleRu = !string.IsNullOrWhiteSpace(ep.Name)
                    ? ep.Name.Trim()
                    : $"Episode {episodeId.DisplayEpisodeNumber}";

                var epTitleEn = !string.IsNullOrWhiteSpace(ep.NameEnglish)
                    ? ep.NameEnglish.Trim()
                    : string.Empty;

                var runtimeMin = ep.Duration > 0
                    ? (int)Math.Round(ep.Duration / 60.0, MidpointRounding.AwayFromZero)
                    : 0;

                var xml = AddGeneratedXmlMarker($@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<episodedetails>
  <title>{MakeSafeXml(epTitleRu)}</title>
  {(epTitleEn.Length > 0 && !string.Equals(epTitleEn, epTitleRu, StringComparison.OrdinalIgnoreCase)
      ? $"  <originaltitle>{MakeSafeXml(epTitleEn)}</originaltitle>"
      : string.Empty)}
  {(Guid.TryParse(ep.Id, out _) ? $"  <uniqueid type=\"aniliberty_episode_id\" default=\"false\">{MakeSafeXml(ep.Id)}</uniqueid>" : string.Empty)}
  {(rel.Year > 0 ? $"  <year>{rel.Year}</year>" : string.Empty)}
  {(runtimeMin > 0 ? $"  <runtime>{runtimeMin}</runtime>" : string.Empty)}
  <showtitle>{MakeSafeXml(showTitle)}</showtitle>
  <episode>{epNum}</episode>
  {(episodeId.ShouldWriteDisplayEpisode ? $"  <displayepisode>{MakeSafeXml(episodeId.DisplayEpisodeNumber)}</displayepisode>" : string.Empty)}
  <season>{seasonNum}</season>
  <lockdata>false</lockdata>
</episodedetails>");
                await WriteManagedTextIfChangedAsync(
                    manifest,
                    nfoPath,
                    xml,
                    Encoding.UTF8,
                    "episode-nfo",
                    rel.Id,
                    ep.Id,
                    source: null,
                    respectUnmanagedExisting: true,
                    token);
            }
        }
    }

    // ─────────────────────── franchises -> season number ─────────────────────

    private async Task<int> DetectSeasonFromFranchiseAsync(int releaseId, CancellationToken ct)
    {
        if (!_franchiseCache.TryGetValue(releaseId, out var frList))
        {
            frList = await client.FetchFranchisesForReleaseAsync(releaseId, ct);
            _franchiseCache[releaseId] = frList;
        }

        return ComputeSeasonFromFranchises(frList, releaseId);
    }

    private static bool IsRegularTvSeason(FranchiseReleaseLink link)
    {
        if (!string.Equals(link.Release?.Type?.Value, "TV", StringComparison.OrdinalIgnoreCase))
            return false;

        var en = link.Release?.Name?.English ?? string.Empty;
        var ru = link.Release?.Name?.Main ?? string.Empty;
        var alt = link.Release?.Name?.Alternative ?? string.Empty;
        var text = $"{en} {ru} {alt}";

        return !_rxNonSeasonTv.IsMatch(text);
    }

    private static int ComputeSeasonFromFranchises(List<FranchiseInfo>? list, int releaseId)
    {
        if (list is null || list.Count == 0) return 0;

        var candidates = list.Where(f => f.FranchiseReleases?.Any(x => x.ReleaseId == releaseId) == true).ToList();
        if (candidates.Count == 0)
            candidates = list;

        var best = candidates
            .OrderByDescending(f => f.FranchiseReleases?.Count ?? 0)
            .FirstOrDefault();

        if (best?.FranchiseReleases == null || best.FranchiseReleases.Count == 0)
            return 0;

        var seasons = best.FranchiseReleases
            .Where(e => e.Release != null)
            .Where(IsRegularTvSeason)
            .OrderBy(e => e.SortOrder ?? int.MaxValue)
            .ThenBy(e => e.Release?.Year ?? int.MaxValue)
            .ThenBy(e => e.ReleaseId)
            .ToList();

        if (seasons.Count == 0)
            return 0;

        var idx = seasons.FindIndex(e => e.ReleaseId == releaseId);
        return idx >= 0 ? idx + 1 : 0;
    }

    // ─────────────────────── helpers ─────────────────────────────

    private static async Task WriteEpisodeIdSidecarAsync(
        ManagedLibraryManifest manifest,
        string strmPath,
        string? episodeId,
        int releaseId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(strmPath) || string.IsNullOrWhiteSpace(episodeId))
            return;

        var normalized = episodeId.Trim();
        if (!Guid.TryParse(normalized, out _))
            return;

        var sidecarPath = AniLibertyEpisodeIdResolver.GetSidecarPath(strmPath);
        if (File.Exists(sidecarPath))
        {
            var oldVal = (await File.ReadAllTextAsync(sidecarPath, ct)).Trim();
            if (string.Equals(oldVal, normalized, StringComparison.OrdinalIgnoreCase))
            {
                manifest.TrackText(sidecarPath, "aniid", normalized, Encoding.UTF8, releaseId, normalized, source: null);
                return;
            }
        }

        await File.WriteAllTextAsync(sidecarPath, normalized, Encoding.UTF8, ct);
        manifest.TrackText(sidecarPath, "aniid", normalized, Encoding.UTF8, releaseId, normalized, source: null);
    }

    private static EpisodeIdentity ResolveEpisodeIdentity(
        EpisodeItem ep,
        ref int autoNumber,
        HashSet<int> usedEpisodeNumbers,
        bool useSortOrderFileNumbers)
    {
        var fileEpisodeNumber = ResolveEpisodeFileNumber(ep, ref autoNumber, usedEpisodeNumbers, useSortOrderFileNumbers);
        var displayEpisodeNumber = FormatEpisodeDisplayNumber(ep.Ordinal, fileEpisodeNumber);
        var shouldWriteDisplayEpisode = !string.Equals(
            displayEpisodeNumber,
            fileEpisodeNumber.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

        return new EpisodeIdentity(fileEpisodeNumber, displayEpisodeNumber, shouldWriteDisplayEpisode);
    }

    private static int ResolveEpisodeFileNumber(
        EpisodeItem ep,
        ref int autoNumber,
        HashSet<int> usedEpisodeNumbers,
        bool useSortOrderFileNumbers)
    {
        while (usedEpisodeNumbers.Contains(autoNumber))
            autoNumber++;

        foreach (var candidate in GetEpisodeFileNumberCandidates(ep, autoNumber, useSortOrderFileNumbers))
        {
            if (candidate <= 0 || !usedEpisodeNumbers.Add(candidate))
                continue;

            autoNumber = Math.Max(autoNumber, candidate + 1);
            return candidate;
        }

        var fallback = autoNumber;
        usedEpisodeNumbers.Add(fallback);
        autoNumber++;
        return fallback;
    }

    private static IEnumerable<int> GetEpisodeFileNumberCandidates(
        EpisodeItem ep,
        int autoNumber,
        bool useSortOrderFileNumbers)
    {
        if (useSortOrderFileNumbers && ep.SortOrder is > 0)
            yield return ep.SortOrder.Value;

        if (!useSortOrderFileNumbers && TryGetWholeEpisodeNumber(ep.Ordinal, out var wholeEpisodeNumber))
            yield return wholeEpisodeNumber;

        if (!useSortOrderFileNumbers && ep.SortOrder is > 0)
            yield return ep.SortOrder.Value;

        if (!useSortOrderFileNumbers && ep.Ordinal is > 0)
        {
            var ceilEpisodeNumber = (int)Math.Ceiling(ep.Ordinal.Value);
            if (ceilEpisodeNumber > 0)
                yield return ceilEpisodeNumber;
        }

        yield return autoNumber;
    }

    private static bool ShouldUseSortOrderFileNumbers(IReadOnlyCollection<EpisodeItem> episodes)
    {
        return episodes.Count > 0 &&
               episodes.Any(ep => HasFractionalOrdinal(ep.Ordinal)) &&
               episodes.All(ep => ep.SortOrder is > 0);
    }

    private static bool HasFractionalOrdinal(double? ordinal)
    {
        if (!ordinal.HasValue || ordinal.Value <= 0)
            return false;

        return Math.Abs(ordinal.Value - Math.Round(ordinal.Value)) > 0.0001d;
    }

    private static bool TryGetWholeEpisodeNumber(double? ordinal, out int wholeEpisodeNumber)
    {
        wholeEpisodeNumber = 0;
        if (!ordinal.HasValue || ordinal.Value <= 0)
            return false;

        var rounded = Math.Round(ordinal.Value);
        if (Math.Abs(ordinal.Value - rounded) > 0.0001d)
            return false;

        if (rounded < 1 || rounded > int.MaxValue)
            return false;

        wholeEpisodeNumber = (int)rounded;
        return true;
    }

    private static string FormatEpisodeDisplayNumber(double? ordinal, int fallbackEpisodeNumber)
    {
        if (!ordinal.HasValue || ordinal.Value <= 0)
            return fallbackEpisodeNumber.ToString(CultureInfo.InvariantCulture);

        if (TryGetWholeEpisodeNumber(ordinal, out var wholeEpisodeNumber))
            return wholeEpisodeNumber.ToString(CultureInfo.InvariantCulture);

        return ordinal.Value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static async Task WriteTextIfChangedAsync(
        string path,
        string content,
        Encoding? encoding,
        CancellationToken ct)
    {
        if (File.Exists(path))
        {
            var existing = await File.ReadAllTextAsync(path, ct);
            if (TextMatches(existing, content))
                return;
        }

        if (encoding is null)
            await File.WriteAllTextAsync(path, content, ct);
        else
            await File.WriteAllTextAsync(path, content, encoding, ct);
    }

    private static bool TextMatches(string existing, string content)
    {
        return NormalizeTextForCompare(existing) == NormalizeTextForCompare(content);
    }

    private static async Task WriteManagedTextIfChangedAsync(
        ManagedLibraryManifest manifest,
        string path,
        string content,
        Encoding? encoding,
        string kind,
        int releaseId,
        string? episodeId,
        string? source,
        bool respectUnmanagedExisting,
        CancellationToken ct)
    {
        if (respectUnmanagedExisting && File.Exists(path) && !manifest.IsManaged(path))
        {
            var existing = await File.ReadAllTextAsync(path, ct);
            if (!ManagedLibraryManifest.HasGeneratedXmlMarker(existing))
                return;
        }

        await WriteTextIfChangedAsync(path, content, encoding, ct);
        manifest.TrackText(path, kind, content, encoding, releaseId, episodeId, source);
    }

    private static string AddGeneratedXmlMarker(string xml)
    {
        const string marker = "<!-- generated-by AniLibertyStrmPlugin -->";
        if (xml.Contains(marker, StringComparison.Ordinal))
            return xml;

        var declarationEnd = xml.IndexOf("?>", StringComparison.Ordinal);
        if (declarationEnd < 0)
            return marker + Environment.NewLine + xml;

        var insertAt = declarationEnd + 2;
        return xml.Insert(insertAt, Environment.NewLine + marker);
    }

    private static string NormalizeTextForCompare(string text)
    {
        return text.Replace("\r\n", "\n").TrimEnd('\n', '\r');
    }

    private readonly record struct EpisodeIdentity(
        int FileEpisodeNumber,
        string DisplayEpisodeNumber,
        bool ShouldWriteDisplayEpisode);

    private static string TrimForLog(string? text, int max = 220)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private async Task LogPlaybackDiagnosticsAsync(
        string context,
        EpisodeItem ep,
        string resolution,
        string selectedRaw,
        string normalizedUrl,
        string strmPath,
        CancellationToken ct)
    {
        log.Info(
            "[PLAYBACK-DIAG] {0}; pref={1}; epId={2}; ordinal={3}; hls1080=\"{4}\"; hls720=\"{5}\"; hls480=\"{6}\"; selectedRaw=\"{7}\"; normalized=\"{8}\"",
            context,
            resolution,
            TrimForLog(ep.Id, 64),
            ep.Ordinal?.ToString() ?? "-",
            TrimForLog(ep.Hls1080),
            TrimForLog(ep.Hls720),
            TrimForLog(ep.Hls480),
            TrimForLog(selectedRaw),
            TrimForLog(normalizedUrl, 320));

        if (File.Exists(strmPath))
        {
            try
            {
                var existing = (await File.ReadAllTextAsync(strmPath, ct)).Trim();
                var same = string.Equals(existing, normalizedUrl, StringComparison.Ordinal);
                log.Info(
                    "[PLAYBACK-DIAG] STRM exists: path=\"{0}\"; sameAsSelected={1}; existing=\"{2}\"",
                    strmPath,
                    same ? "yes" : "no",
                    TrimForLog(existing, 320));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Warn(ex, "[PLAYBACK-DIAG] Failed to read existing STRM: {0}", strmPath);
            }
        }
        else
        {
            log.Info("[PLAYBACK-DIAG] STRM create: path=\"{0}\"; value=\"{1}\"",
                strmPath, TrimForLog(normalizedUrl, 320));
        }

        var probe = await ProbeHlsPlaylistAsync(normalizedUrl, ct);
        log.Info("[PLAYBACK-DIAG] HLS probe: {0}; {1}", TrimForLog(normalizedUrl, 320), probe);
    }

    private async Task<string> ProbeHlsPlaylistAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return "empty-url";
        if (_hlsProbeCache.TryGetValue(url, out var cached)) return cached;

        string summary;
        try
        {
            using var resp = await _hlsHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            var code = (int)resp.StatusCode;
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "-";

            var body = await resp.Content.ReadAsStringAsync(ct);
            var hasExtM3u = body.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase);
            var hasExtInf = body.Contains("#EXTINF", StringComparison.OrdinalIgnoreCase);
            var hasVariant = body.Contains("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase);

            var firstMediaUri = body
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .FirstOrDefault(x => !x.StartsWith('#'));

            summary =
                $"http={code}; type={contentType}; bytes={body.Length}; extm3u={hasExtM3u}; variant={hasVariant}; segments={hasExtInf}; firstUri=\"{TrimForLog(firstMediaUri, 140)}\"";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            summary = $"probe-failed: {ex.GetType().Name}: {TrimForLog(ex.Message, 180)}";
        }

        _hlsProbeCache[url] = summary;
        return summary;
    }

    private static string PickImageUrl(ImageBlock? img)
    {
        if (img is null) return string.Empty;

        static string First(params string?[] vals)
            => vals.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

        // Prefer non-optimized URLs first (usually jpg); optimized is often webp
        return First(
            img.Src,
            img.Preview,
            img.Thumbnail,
            img.Optimized?.Preview,
            img.Optimized?.Thumbnail,
            img.Optimized?.Src
        );
    }

    private static string NormalizeImageUrlPreferJpg(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;

        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var path = uri.AbsolutePath;
                if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                {
                    var newPath = path[..^5] + ".jpg";
                    var builder = new UriBuilder(uri) { Path = newPath };
                    return builder.Uri.ToString();
                }
            }
        }
        catch
        {
            // ignore
        }

        // fallback (in case a non-URI string is returned)
        return url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            ? url[..^5] + ".jpg"
            : url;
    }

    private static string GetSafeImageExtensionFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return ".jpg";

        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var ext = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrWhiteSpace(ext)) return ".jpg";

                ext = ext.ToLowerInvariant();
                return ext is ".jpg" or ".jpeg" or ".png" ? ext : ".jpg";
            }
        }
        catch
        {
            // ignore
        }

        var ext2 = Path.GetExtension(url);
        if (string.IsNullOrWhiteSpace(ext2)) return ".jpg";

        ext2 = ext2.ToLowerInvariant();
        return ext2 is ".jpg" or ".jpeg" or ".png" ? ext2 : ".jpg";
    }

    private async Task DownloadManagedImageAsync(
        ManagedLibraryManifest manifest,
        string url,
        string path,
        string kind,
        int releaseId,
        string? episodeId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        var exists = File.Exists(path);
        if (exists && !manifest.IsManaged(path))
            return;

        try
        {
            using var resp = await _mediaHttp.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                if (exists)
                    manifest.TrackExisting(path, kind, releaseId, episodeId, url);
                return;
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length < 4)
            {
                if (exists)
                    manifest.TrackExisting(path, kind, releaseId, episodeId, url);
                return;
            }

            var isJpg = bytes[0] == 0xFF && bytes[1] == 0xD8;
            var isPng = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
            if (!isJpg && !isPng)
            {
                if (exists)
                    manifest.TrackExisting(path, kind, releaseId, episodeId, url);
                return;
            }

            if (!exists || !BytesMatch(path, bytes))
                await File.WriteAllBytesAsync(path, bytes, ct);

            manifest.TrackBytes(path, kind, bytes, releaseId, episodeId, url);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (exists)
                manifest.TrackExisting(path, kind, releaseId, episodeId, url);
            log.Warn(ex, "Download image failed: {0}", url);
        }
    }

    private static bool BytesMatch(string path, byte[] bytes)
    {
        try
        {
            var existing = File.ReadAllBytes(path);
            return existing.AsSpan().SequenceEqual(bytes);
        }
        catch
        {
            return false;
        }
    }

    private static string? ChooseHls(EpisodeItem ep, string pref)
    {
        return pref switch
        {
            "1080" => ep.Hls1080 ?? ep.Hls720 ?? ep.Hls480,
            "720"  => ep.Hls720 ?? ep.Hls1080 ?? ep.Hls480,
            _      => ep.Hls480 ?? ep.Hls720 ?? ep.Hls1080
        };
    }

    private string MakePlaybackUrl(string rawUrl)
    {
        var upstreamUrl = MakeFullUrl(rawUrl);
        if (string.IsNullOrWhiteSpace(upstreamUrl))
            return upstreamUrl;

        if (Plugin.Instance?.Configuration?.UseJellyfinPlaybackProxy != true)
            return upstreamUrl;

        var proxyEndpoint = TryGetPlaybackProxyEndpoint();
        return string.IsNullOrWhiteSpace(proxyEndpoint)
            ? upstreamUrl
            : PlaybackProxyHelper.BuildProxyUrl(proxyEndpoint, upstreamUrl);
    }

    private string TryGetPlaybackProxyEndpoint()
    {
        try
        {
            var configuredBaseUrl = Plugin.Instance?.Configuration?.JellyfinPlaybackProxyBaseUrl?.Trim();
            var baseUrl = configuredBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                var preferredIp = networkManager.GetInternalBindAddresses()
                    .Select(x => x.Address)
                    .FirstOrDefault(ip => ip is not null &&
                                          !IPAddress.IsLoopback(ip) &&
                                          ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?? networkManager.GetInternalBindAddresses()
                        .Select(x => x.Address)
                        .FirstOrDefault(ip => ip is not null && !IPAddress.IsLoopback(ip));

                if (preferredIp is not null)
                    baseUrl = serverHost.GetApiUrlForLocalAccess(preferredIp, serverHost.ListenWithHttps);
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
                return string.Empty;

            var route = serverHost.ReverseVirtualPath(PlaybackProxyHelper.ProxyRoute);
            if (string.IsNullOrWhiteSpace(route))
                route = PlaybackProxyHelper.ProxyRoute;

            return PlaybackProxyHelper.BuildProxyEndpoint(baseUrl, route);
        }
        catch (Exception ex)
        {
            log.Warn(ex, "Unable to resolve AniLiberty playback proxy endpoint. Falling back to direct HLS URL.");
            return string.Empty;
        }
    }

    private static string MakeFullUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;

        url = url.Trim();
        if (Uri.IsWellFormedUriString(url, UriKind.Absolute)) return url;

        if (url.StartsWith("//", StringComparison.Ordinal))
            return "https:" + url;

        return url.StartsWith("/", StringComparison.Ordinal)
            ? "https://api.anilibria.app" + url
            : "https://api.anilibria.app/" + url;
    }

    private static string MakeSafe(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);

        foreach (var ch in s)
        {
            if (ch is '-' or '–' or '—')
            {
                sb.Append(' ');
                continue;
            }

            if (ch == '×')
            {
                sb.Append('x');
                continue;
            }

            if (ch == 'Ω' || ch == 'ω') continue;

            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? ' ' : ch);
        }

        var tmp = Regex.Replace(sb.ToString(), @"[ \t\.\-]{2,}", " ").Trim();
        return tmp;
    }

    private static string NormalizeTitleForFs(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        s = s.Replace('×', 'x').Replace('Ｘ', 'x');
        s = s.Replace("-", " ").Replace("–", " ").Replace("—", " ");
        s = s.Replace("Ω", "").Replace("ω", "");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return MakeSafe(CleanShowName(s));
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
            var playlist = await _hlsHttp.GetStringAsync(url, ct);

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
        catch (OperationCanceledException)
        {
            throw;
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

using System.Collections.Concurrent;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AniLibertyStrmPlugin.Configuration;
using AniLibertyStrmPlugin.Models;
using AniLibertyStrmPlugin.Utils;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
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

public sealed partial class AniLibertyStrmGenerator(
    ILogger<AniLibertyStrmGenerator> log,
    IServerApplicationHost serverHost,
    INetworkManager networkManager,
    IAniLibertyClient client)
    : IAniLibertyStrmGenerator
{
    private const string PublishedServerUrlEnvironmentVariable = "JELLYFIN_PublishedServerUrl";
    private const string UppercasePublishedServerUrlEnvironmentVariable = "JELLYFIN_PUBLISHEDSERVERURL";
    private const string DotnetRunningInContainerEnvironmentVariable = "DOTNET_RUNNING_IN_CONTAINER";
    private const int RegexMatchTimeoutMilliseconds = 1000;

    private int _containerProxyFallbackWarningLogged;
    private int _invalidConfiguredProxyUrlWarningLogged;
    private int _invalidPublishedProxyUrlWarningLogged;
    private int _publishedProxyUrlLogged;

    // ────────────────────── 1. suffix cleanup ──────────────────────
    private static readonly Regex[] SuffixRules =
    {
        SeasonSuffixRegex(),
        NumberedSeasonSuffixRegex(),
        PartSuffixRegex(),
        NumberedCourSuffixRegex(),
        RomanSuffixRegex(),
        NumericSuffixRegex(),
        ExtraTypeSuffixRegex(),

        // NEW: strip trailing Omega suffix variants
        OmegaSuffixRegex()
    };

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

    private static readonly ConcurrentDictionary<string, string> _hlsProbeCache = new();

    private static readonly JsonSerializerOptions PopularityJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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

    [GeneratedRegex(@"\s*(?:Season)\s*\d+\b.*$", RegexOptions.IgnoreCase, RegexMatchTimeoutMilliseconds)]
    private static partial Regex SeasonSuffixRegex();

    [GeneratedRegex(@"\s*\d+(?:st|nd|rd|th)?\s*Season\b.*$", RegexOptions.IgnoreCase, RegexMatchTimeoutMilliseconds)]
    private static partial Regex NumberedSeasonSuffixRegex();

    [GeneratedRegex(@"\s*(?:Part|Cour)\s*\d+\b.*$", RegexOptions.IgnoreCase, RegexMatchTimeoutMilliseconds)]
    private static partial Regex PartSuffixRegex();

    [GeneratedRegex(@"\s*\d+(?:st|nd|rd|th)?\s*Cour\b.*$", RegexOptions.IgnoreCase, RegexMatchTimeoutMilliseconds)]
    private static partial Regex NumberedCourSuffixRegex();

    [GeneratedRegex(@"\s*[-._ ]+(?:I{2,3}|IV|V?I{0,3}|VII?)$", RegexOptions.IgnoreCase, RegexMatchTimeoutMilliseconds)]
    private static partial Regex RomanSuffixRegex();

    [GeneratedRegex(@"\s+[2-4]$", RegexOptions.IgnoreCase, RegexMatchTimeoutMilliseconds)]
    private static partial Regex NumericSuffixRegex();

    [GeneratedRegex(@"\s+(?:OAD|OVA|OAV|Specials?|Movie)$", RegexOptions.IgnoreCase, RegexMatchTimeoutMilliseconds)]
    private static partial Regex ExtraTypeSuffixRegex();

    [GeneratedRegex(@"\s*(?:Ω|ω|Omega|Омега)\b.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexMatchTimeoutMilliseconds)]
    private static partial Regex OmegaSuffixRegex();

    [GeneratedRegex(@"\bSeason\s*(\d{1,2})\b", RegexOptions.IgnoreCase, RegexMatchTimeoutMilliseconds)]
    private static partial Regex SeasonNumberRegex();

    [GeneratedRegex(@"(?:\s|\D)(\d{1,2})\s*$", RegexOptions.IgnoreCase, RegexMatchTimeoutMilliseconds)]
    private static partial Regex TrailingNumberRegex();

    [GeneratedRegex(@"\b(?:
            specials? |
            спецвыпуск(?:и|а)? |
            episode\s*0 |
            episode\s*zero |
            нулевая\s*серия |
            recap
        )\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace, RegexMatchTimeoutMilliseconds)]
    private static partial Regex NonSeasonTvRegex();

    [GeneratedRegex(@"[\s\.\-_()]+$", RegexOptions.None, RegexMatchTimeoutMilliseconds)]
    private static partial Regex TrailingTitleJunkRegex();

    [GeneratedRegex(@"[\u03A9\u03C9]", RegexOptions.None, RegexMatchTimeoutMilliseconds)]
    private static partial Regex OmegaCharacterRegex();

    [GeneratedRegex(@"\b(?:specials?)\b", RegexOptions.IgnoreCase, RegexMatchTimeoutMilliseconds)]
    private static partial Regex SpecialsWordRegex();

    [GeneratedRegex(@"\b(movie|film|the\s*movie)\b", RegexOptions.IgnoreCase, RegexMatchTimeoutMilliseconds)]
    private static partial Regex MovieWordRegex();

    [GeneratedRegex(@"\b(the\s*movie|movie|film)\b", RegexOptions.IgnoreCase, RegexMatchTimeoutMilliseconds)]
    private static partial Regex MovieTitleWordRegex();

    [GeneratedRegex(@"\bcode\s*[:\-]\s*", RegexOptions.IgnoreCase, RegexMatchTimeoutMilliseconds)]
    private static partial Regex CodePrefixRegex();

    [GeneratedRegex(@"[ \t\.\-]{2,}", RegexOptions.None, RegexMatchTimeoutMilliseconds)]
    private static partial Regex RepeatedSeparatorRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, RegexMatchTimeoutMilliseconds)]
    private static partial Regex WhitespaceRegex();

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

        var cfg = Plugin.Instance?.Configuration;
        var debugLogs = cfg?.EnableDebugLogs == true;
        var supportTrace = cfg?.EnableRawSupportLogs == true;
        var cleanupMode = cfg?.StaleCleanupMode ?? StaleCleanupMode.DryRun;
        var manifest = await ManagedLibraryManifest.LoadAsync(basePath, token);
        var mediaSegments = new AniLibertyMediaSegmentState(basePath);
        var context = new GenerationContext(
            basePath,
            resolution,
            BuildFallbackSeasonMap(list),
            list,
            cfg?.EnablePlaybackDiagnostics == true,
            manifest,
            mediaSegments);

        await GenerateTitleListAsync(list, context, progress, debugLogs, supportTrace, token);

        await mediaSegments.SaveAsync(token);
        await manifest.ApplyCleanupAsync(cleanupMode, log, token);
        await manifest.SaveAsync(cleanupMode, token);
    }

    internal static Dictionary<int, int> BuildFallbackSeasonMap(IList<ReleaseResponse> list)
    {
        var fallbackById = new Dictionary<int, int>();
        foreach (var group in list.GroupBy(GroupKey))
        {
            var ordered = group
                .OrderBy(r => r.Year > 0 ? r.Year : int.MaxValue)
                .ThenBy(r => QuarterIndex(r.Season?.Value))
                .ThenBy(r => r.Name?.English ?? r.Name?.Main ?? r.Alias ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Id)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
                fallbackById[ordered[i].Id] = i + 1;
        }

        return fallbackById;
    }

    private async Task GenerateTitleListAsync(
        IList<ReleaseResponse> list,
        GenerationContext context,
        IProgress<double>? progress,
        bool debugLogs,
        bool supportTrace,
        CancellationToken token)
    {
        for (var index = 0; index < list.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var current = index + 1;
            var rel = list[index];

            LogTitleProgress(rel, current, list.Count, debugLogs, supportTrace);
            await GenerateReleaseAsync(rel, context, token);
            progress?.Report(current / (double)list.Count * 100.0);
        }
    }

    private void LogTitleProgress(
        ReleaseResponse rel,
        int current,
        int total,
        bool debugLogs,
        bool supportTrace)
    {
        var display = rel.Name?.English ?? rel.Name?.Main ?? rel.Alias;
        if (debugLogs || current == 1 || current == total || current % 25 == 0)
        {
            log.Info("({0}/{1}) \"{2}\"", current, total, display);
        }
        else if (supportTrace)
        {
            log.Debug("({0}/{1}) \"{2}\"", current, total, display);
        }
    }

    private async Task GenerateReleaseAsync(ReleaseResponse rel, GenerationContext context, CancellationToken token)
    {
        var hydrated = await HydrateReleaseWithEpisodesAsync(rel, token);
        if (hydrated is null)
            return;

        var displayRaw = hydrated.Name?.English ?? hydrated.Name?.Main ?? hydrated.Alias ?? "";
        if (LooksLikeMovie(hydrated, displayRaw))
            await GenerateMovieAsync(hydrated, context.BasePath, context.Resolution, context.PlaybackDiagnostics, context.Manifest, token);
        else
            await GenerateStrmForTitle(hydrated, context, token);
    }

    private async Task<ReleaseResponse?> HydrateReleaseWithEpisodesAsync(ReleaseResponse rel, CancellationToken token)
    {
        if (rel.Episodes is { Count: > 0 })
            return rel;

        try
        {
            var full = await client.FetchReleaseByIdAsync(rel.Id, token);
            if (full?.Episodes?.Count > 0)
                return full;

            log.Info("Skip {0} – no episodes in detail", rel.Id);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Warn(ex, "Skip {0} – failed to fetch details", rel.Id);
            return null;
        }
    }

    internal static string CleanShowName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;

        var cleaned = SuffixRules.Aggregate(name, (current, rx) => rx.Replace(current, ""));
        cleaned = TrailingTitleJunkRegex().Replace(cleaned, "");
        cleaned = OmegaCharacterRegex().Replace(cleaned, ""); // remove internal Ω characters
        return cleaned.Trim();
    }

    internal static int DetectSeasonNumber(ReleaseResponse rel)
    {
        int TryParse(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return 0;

            var m = SeasonNumberRegex().Match(title);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n1)) return n1;

            m = TrailingNumberRegex().Match(title);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n2)) return n2;

            return 0;
        }

        var num = TryParse(rel.Name?.English);
        if (num == 0) num = TryParse(rel.Name?.Main);
        if (num == 0) num = 1;
        return num;
    }

    internal static int QuarterIndex(string? v)
    {
        return v != null && _seasonOrder.TryGetValue(v, out var k) ? k : 99;
    }

    internal static string GroupKey(ReleaseResponse r)
    {
        var ruName = r.Name?.Main?.Trim();
        var engName = r.Name?.English?.Trim();
        var rawName = engName ?? ruName ?? r.Alias ?? $"Title_{r.Id}";
        rawName = SpecialsWordRegex().Replace(rawName, "").Trim();
        return NormalizeTitleForFs(rawName).ToLowerInvariant();
    }

    internal static bool LooksLikeMovie(ReleaseResponse rel, string title)
    {
        // v1 has type.value (MOVIE), which is more reliable than matching title words.
        if (string.Equals(rel.Type?.Value, "MOVIE", StringComparison.OrdinalIgnoreCase))
            return true;

        var oneEp = (rel.Episodes?.Count ?? 0) <= 1 || rel.EpisodesTotal.GetValueOrDefault(0) <= 1;
        var hasMovieWord = MovieWordRegex().IsMatch(title);
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

        var title = MovieTitleWordRegex().Replace(raw, "");
        title = CodePrefixRegex().Replace(title, "Code ");
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
            new ManagedTextWrite(strmPath, url, null, "strm", rel.Id, ep.Id, selectedRaw, false),
            manifest,
            token);
        await WriteEpisodeIdSidecarAsync(manifest, strmPath, ep.Id, rel.Id, token);
        await WritePopularitySidecarAsync(rel, movieDir, manifest, token);

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
            new ManagedTextWrite(nfoPath, nfo, Encoding.UTF8, "movie-nfo", rel.Id, ep.Id, null, true),
            manifest,
            token);
    }

    private async Task GenerateStrmForTitle(ReleaseResponse rel, GenerationContext context, CancellationToken token)
    {
        if (rel.Episodes is null || rel.Episodes.Count == 0)
        {
            log.Info("Skip {0} – no episodes", rel.Id);
            return;
        }

        var title = BuildTitleInfo(rel);
        var seasonNum = await ResolveSeasonNumberAsync(rel, title, context, token);
        var paths = CreateSeasonPaths(context.BasePath, title.SafeName, seasonNum);
        var posterUrl = NormalizeImageUrlPreferJpg(MakeFullUrl(PickImageUrl(rel.Poster)));
        await DownloadManagedImageAsync(context.Manifest, posterUrl, Path.Combine(paths.ShowDir, "folder.jpg"), "show-poster", rel.Id, null, token);
        await DownloadManagedImageAsync(context.Manifest, posterUrl, Path.Combine(paths.ShowDir, $"{paths.SeasonFolder}-poster.jpg"), "season-poster", rel.Id, null, token);
        await WritePopularitySidecarAsync(rel, paths.ShowDir, context.Manifest, token);
        await WriteTvShowNfoAsync(rel, title, context, paths.ShowDir, token);
        await WriteSeasonNfoAsync(rel.Id, seasonNum, paths.SeasonDir, context.Manifest, token);
        await GenerateEpisodesAsync(rel, title, paths, seasonNum, context, token);
    }

    private static TitleGenerationInfo BuildTitleInfo(ReleaseResponse rel)
    {
        var ruName = rel.Name?.Main?.Trim();
        var engName = rel.Name?.English?.Trim();
        var altName = rel.Name?.Alternative?.Trim();
        var rawName = engName ?? ruName ?? rel.Alias ?? $"Title_{rel.Id}";
        var isSpecialsType = IsSpecialsType(rel);
        var isSpecialsTitle = isSpecialsType || SpecialsWordRegex().IsMatch(rawName);

        if (!isSpecialsType && isSpecialsTitle)
            rawName = SpecialsWordRegex().Replace(rawName, "").Trim();

        return new TitleGenerationInfo(
            ruName,
            engName,
            altName,
            NormalizeTitleForFs(rawName).ToLowerInvariant(),
            isSpecialsTitle);
    }

    internal static bool IsSpecialsType(ReleaseResponse rel)
    {
        return string.Equals(rel.Type?.Value, "SPECIAL", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rel.Type?.Value, "OVA", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rel.Type?.Value, "OAD", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> ResolveSeasonNumberAsync(
        ReleaseResponse rel,
        TitleGenerationInfo title,
        GenerationContext context,
        CancellationToken token)
    {
        if (title.IsSpecialsTitle)
            return 0;

        var franchiseSeason = await DetectSeasonFromFranchiseAsync(rel.Id, token);
        if (franchiseSeason > 0)
            return franchiseSeason;

        var detectedSeason = DetectSeasonNumber(rel);
        if (detectedSeason > 1)
            return detectedSeason;

        return TryResolveFallbackSeason(rel, context, out var fallbackSeason)
            ? fallbackSeason
            : detectedSeason;
    }

    private static bool TryResolveFallbackSeason(
        ReleaseResponse rel,
        GenerationContext context,
        out int seasonNum)
    {
        seasonNum = 0;
        var key = GroupKey(rel);
        var sameGroupCount = context.AllTitles.Count(x => GroupKey(x) == key);
        return sameGroupCount > 1 && context.FallbackById.TryGetValue(rel.Id, out seasonNum);
    }

    private static SeasonPaths CreateSeasonPaths(string basePath, string safeName, int seasonNum)
    {
        var showDir = Path.Combine(basePath, safeName);
        Directory.CreateDirectory(showDir);

        var seasonFolder = $"Season {seasonNum:00}";
        var seasonDir = Path.Combine(showDir, seasonFolder);
        Directory.CreateDirectory(seasonDir);
        return new SeasonPaths(showDir, seasonFolder, seasonDir);
    }

    private static async Task WriteTvShowNfoAsync(
        ReleaseResponse rel,
        TitleGenerationInfo title,
        GenerationContext context,
        string showDir,
        CancellationToken token)
    {
        var tvshowNfo = Path.Combine(showDir, "tvshow.nfo");
        var xml = BuildTvShowNfo(rel, title, context.AllTitles);
        await WriteManagedTextIfChangedAsync(
            new ManagedTextWrite(tvshowNfo, xml, Encoding.UTF8, "tvshow-nfo", rel.Id, null, null, true),
            context.Manifest,
            token);
    }

    private static string BuildTvShowNfo(
        ReleaseResponse rel,
        TitleGenerationInfo title,
        IList<ReleaseResponse> allTitles)
    {
        var displayTitle = title.RuName ?? title.EngName ?? title.SafeName;
        var originalTitle = title.AltName ?? title.EngName ?? displayTitle;
        var sortTitle = title.EngName ?? title.RuName ?? displayTitle;
        var plot = MakeSafeXml(rel.Description?.Trim() ?? string.Empty);
        var showYear = FindShowYear(rel, allTitles);

        return AddGeneratedXmlMarker($@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<tvshow>
  <title>{MakeSafeXml(displayTitle)}</title>
  {(originalTitle != displayTitle ? $"  <originaltitle>{MakeSafeXml(originalTitle)}</originaltitle>" : string.Empty)}
  {(sortTitle != displayTitle ? $"  <sorttitle>{MakeSafeXml(sortTitle)}</sorttitle>" : string.Empty)}
  {(showYear > 0 ? $"  <year>{showYear}</year>" : string.Empty)}
  {(plot.Length > 0 ? $"  <plot>{plot}</plot><outline>{plot}</outline>" : string.Empty)}
  <lockdata>false</lockdata>
</tvshow>");
    }

    private static int FindShowYear(ReleaseResponse rel, IList<ReleaseResponse> allTitles)
    {
        var groupKey = GroupKey(rel);
        return allTitles
            .Where(x => GroupKey(x) == groupKey)
            .Select(x => x.Year)
            .Where(y => y > 0)
            .DefaultIfEmpty(0)
            .Min();
    }

    private static async Task WriteSeasonNfoAsync(
        int releaseId,
        int seasonNum,
        string seasonDir,
        ManagedLibraryManifest manifest,
        CancellationToken token)
    {
        var seasonXml = AddGeneratedXmlMarker($@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<season>
  <title>Season {seasonNum}</title>
  <seasonnumber>{seasonNum}</seasonnumber>
  <lockdata>false</lockdata>
</season>");
        await WriteManagedTextIfChangedAsync(
            new ManagedTextWrite(Path.Combine(seasonDir, "season.nfo"), seasonXml, Encoding.UTF8, "season-nfo", releaseId, null, null, true),
            manifest,
            token);
    }

    private static async Task WritePopularitySidecarAsync(
        ReleaseResponse rel,
        string itemRootDir,
        ManagedLibraryManifest manifest,
        CancellationToken token)
    {
        var document = AniLibertyPopularityDocument.FromRelease(rel);
        if (!document.HasAnyPopularityCount())
            return;

        var json = JsonSerializer.Serialize(document, PopularityJsonOptions);
        await WriteManagedTextIfChangedAsync(
            new ManagedTextWrite(
                Path.Combine(itemRootDir, AniLibertyPopularityDocument.FileName),
                json,
                Encoding.UTF8,
                "popularity-json",
                rel.Id,
                null,
                AniLibertyPopularityDocument.SourceName,
                true),
            manifest,
            token);
    }

    private async Task GenerateEpisodesAsync(
        ReleaseResponse rel,
        TitleGenerationInfo title,
        SeasonPaths paths,
        int seasonNum,
        GenerationContext context,
        CancellationToken token)
    {
        var numberState = new EpisodeNumberState(rel.Episodes!);
        var episodeContext = new EpisodeGenerationContext(rel, title, paths, seasonNum, context);
        foreach (var ep in rel.Episodes!)
        {
            token.ThrowIfCancellationRequested();
            await GenerateEpisodeAsync(ep, episodeContext, numberState, token);
        }
    }

    private async Task GenerateEpisodeAsync(
        EpisodeItem ep,
        EpisodeGenerationContext episodeContext,
        EpisodeNumberState numberState,
        CancellationToken token)
    {
        var episodeId = numberState.Resolve(ep);
        var epNum = episodeId.FileEpisodeNumber;
        var strmPath = Path.Combine(episodeContext.Paths.SeasonDir, $"S{episodeContext.SeasonNumber:00}E{epNum:00}.strm");
        var selectedRaw = ChooseHls(ep, episodeContext.Generation.Resolution) ?? string.Empty;
        var url = MakePlaybackUrl(selectedRaw);

        if (string.IsNullOrWhiteSpace(url))
        {
            LogMissingHls(episodeContext.Release, ep, episodeContext.SeasonNumber, epNum, episodeContext.Generation.PlaybackDiagnostics);
            return;
        }

        await LogTvPlaybackDiagnosticsAsync(ep, epNum, selectedRaw, url, strmPath, episodeContext, token);
        await WriteManagedTextIfChangedAsync(
            new ManagedTextWrite(strmPath, url, null, "strm", episodeContext.Release.Id, ep.Id, selectedRaw, false),
            episodeContext.Generation.Manifest,
            token);
        await WriteEpisodeIdSidecarAsync(episodeContext.Generation.Manifest, strmPath, ep.Id, episodeContext.Release.Id, token);
        await WriteEpisodePreviewAsync(episodeContext.Release.Id, ep, episodeContext.Paths.SeasonDir, episodeContext.SeasonNumber, epNum, episodeContext.Generation.Manifest, token);
        TrackMediaSegments(episodeContext.Release.Id, ep, strmPath, episodeContext.Generation.MediaSegments);
        await WriteEpisodeNfoAsync(ep, strmPath, epNum, episodeId, episodeContext, token);
    }

    private void LogMissingHls(ReleaseResponse rel, EpisodeItem ep, int seasonNum, int epNum, bool playbackDiagnostics)
    {
        if (!playbackDiagnostics)
            return;

        log.Warn(
            "[PLAYBACK-DIAG] TV relId={0} alias={1} S{2:00}E{3:00}: no HLS URL selected; hls1080=\"{4}\" hls720=\"{5}\" hls480=\"{6}\"",
            rel.Id, rel.Alias, seasonNum, epNum,
            TrimForLog(ep.Hls1080), TrimForLog(ep.Hls720), TrimForLog(ep.Hls480));
    }

    private async Task LogTvPlaybackDiagnosticsAsync(
        EpisodeItem ep,
        int epNum,
        string selectedRaw,
        string url,
        string strmPath,
        EpisodeGenerationContext episodeContext,
        CancellationToken token)
    {
        if (!episodeContext.Generation.PlaybackDiagnostics)
            return;

        await LogPlaybackDiagnosticsAsync(
            $"TV relId={episodeContext.Release.Id} alias={episodeContext.Release.Alias} S{episodeContext.SeasonNumber:00}E{epNum:00}",
            ep,
            episodeContext.Generation.Resolution,
            selectedRaw,
            url,
            strmPath,
            token);
    }

    private async Task WriteEpisodePreviewAsync(
        int releaseId,
        EpisodeItem ep,
        string seasonDir,
        int seasonNum,
        int epNum,
        ManagedLibraryManifest manifest,
        CancellationToken token)
    {
        var epPreviewUrlRaw = MakeFullUrl(PickImageUrl(ep.Preview));
        var epPreviewUrl = NormalizeImageUrlPreferJpg(epPreviewUrlRaw);
        if (string.IsNullOrWhiteSpace(epPreviewUrl))
            return;

        var ext = GetSafeImageExtensionFromUrl(epPreviewUrl);
        var thumbPath = Path.Combine(seasonDir, $"S{seasonNum:00}E{epNum:00}-thumb{ext}");
        await DownloadManagedImageAsync(manifest, epPreviewUrl, thumbPath, "episode-thumb", releaseId, ep.Id, token);
    }

    private static void TrackMediaSegments(
        int releaseId,
        EpisodeItem ep,
        string strmPath,
        AniLibertyMediaSegmentState mediaSegments)
    {
        var segments = BuildMediaSegments(ep);
        if (segments.Length == 0)
            return;

        mediaSegments.Track(strmPath, releaseId, ep.Id, segments);
    }

    internal static AniLibertyMediaSegment[] BuildMediaSegments(EpisodeItem ep)
    {
        var result = new List<AniLibertyMediaSegment>(2);
        AddMediaSegment(result, "Intro", ep.Opening);
        AddMediaSegment(result, "Outro", ep.Ending);
        return result.ToArray();
    }

    private static void AddMediaSegment(List<AniLibertyMediaSegment> result, string type, OpeningBlock? block)
    {
        var start = block?.Start ?? -1;
        var stop = block?.Stop ?? -1;
        if (start < 0 || stop <= start)
            return;

        result.Add(new AniLibertyMediaSegment
        {
            Type = type,
            StartTicks = TimeSpan.FromSeconds(start).Ticks,
            EndTicks = TimeSpan.FromSeconds(stop).Ticks
        });
    }

    private static async Task WriteEpisodeNfoAsync(
        EpisodeItem ep,
        string strmPath,
        int epNum,
        EpisodeIdentity episodeId,
        EpisodeGenerationContext episodeContext,
        CancellationToken token)
    {
        var nfoPath = Path.ChangeExtension(strmPath, ".nfo");
        var showTitle = episodeContext.Title.RuName ?? episodeContext.Title.EngName ?? episodeContext.Title.SafeName;
        var epTitleRu = !string.IsNullOrWhiteSpace(ep.Name)
            ? ep.Name.Trim()
            : $"Episode {episodeId.DisplayEpisodeNumber}";
        var epTitleEn = !string.IsNullOrWhiteSpace(ep.NameEnglish) ? ep.NameEnglish.Trim() : string.Empty;
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
  {(episodeContext.Release.Year > 0 ? $"  <year>{episodeContext.Release.Year}</year>" : string.Empty)}
  {(runtimeMin > 0 ? $"  <runtime>{runtimeMin}</runtime>" : string.Empty)}
  <showtitle>{MakeSafeXml(showTitle)}</showtitle>
  <episode>{epNum}</episode>
  {(episodeId.ShouldWriteDisplayEpisode ? $"  <displayepisode>{MakeSafeXml(episodeId.DisplayEpisodeNumber)}</displayepisode>" : string.Empty)}
  <season>{episodeContext.SeasonNumber}</season>
  <lockdata>false</lockdata>
</episodedetails>");
        await WriteManagedTextIfChangedAsync(
            new ManagedTextWrite(nfoPath, xml, Encoding.UTF8, "episode-nfo", episodeContext.Release.Id, ep.Id, null, true),
            episodeContext.Generation.Manifest,
            token);
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

    internal static bool IsRegularTvSeason(FranchiseReleaseLink link)
    {
        if (!string.Equals(link.Release?.Type?.Value, "TV", StringComparison.OrdinalIgnoreCase))
            return false;

        var en = link.Release?.Name?.English ?? string.Empty;
        var ru = link.Release?.Name?.Main ?? string.Empty;
        var alt = link.Release?.Name?.Alternative ?? string.Empty;
        var text = $"{en} {ru} {alt}";

        return !NonSeasonTvRegex().IsMatch(text);
    }

    internal static int ComputeSeasonFromFranchises(List<FranchiseInfo>? list, int releaseId)
    {
        if (list is null || list.Count == 0) return 0;

        var candidates = list.Where(f => f.FranchiseReleases?.Any(x => x.ReleaseId == releaseId) == true).ToList();
        if (candidates.Count == 0)
            candidates = list;

        var best = candidates
            .OrderByDescending(f => f.FranchiseReleases?.Count ?? 0)
            .First();

        if (best.FranchiseReleases == null || best.FranchiseReleases.Count == 0)
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

    internal static bool TextMatches(string existing, string content)
    {
        return NormalizeTextForCompare(existing) == NormalizeTextForCompare(content);
    }

    private static async Task WriteManagedTextIfChangedAsync(
        ManagedTextWrite write,
        ManagedLibraryManifest manifest,
        CancellationToken ct)
    {
        if (write.RespectUnmanagedExisting && File.Exists(write.Path) && !manifest.IsManaged(write.Path))
        {
            var existing = await File.ReadAllTextAsync(write.Path, ct);
            if (!ManagedLibraryManifest.HasGeneratedXmlMarker(existing))
                return;
        }

        await WriteTextIfChangedAsync(write.Path, write.Content, write.Encoding, ct);
        manifest.TrackText(write.Path, write.Kind, write.Content, write.Encoding, write.ReleaseId, write.EpisodeId, write.Source);
    }

    internal static string AddGeneratedXmlMarker(string xml)
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

    internal static string NormalizeTextForCompare(string text)
    {
        return text.Replace("\r\n", "\n").TrimEnd('\n', '\r');
    }

    private readonly record struct GenerationContext(
        string BasePath,
        string Resolution,
        IReadOnlyDictionary<int, int> FallbackById,
        IList<ReleaseResponse> AllTitles,
        bool PlaybackDiagnostics,
        ManagedLibraryManifest Manifest,
        AniLibertyMediaSegmentState MediaSegments);

    private readonly record struct TitleGenerationInfo(
        string? RuName,
        string? EngName,
        string? AltName,
        string SafeName,
        bool IsSpecialsTitle);

    private readonly record struct SeasonPaths(
        string ShowDir,
        string SeasonFolder,
        string SeasonDir);

    private readonly record struct EpisodeGenerationContext(
        ReleaseResponse Release,
        TitleGenerationInfo Title,
        SeasonPaths Paths,
        int SeasonNumber,
        GenerationContext Generation);

    private readonly record struct ManagedTextWrite(
        string Path,
        string Content,
        Encoding? Encoding,
        string Kind,
        int ReleaseId,
        string? EpisodeId,
        string? Source,
        bool RespectUnmanagedExisting);

    private readonly record struct EpisodeIdentity(
        int FileEpisodeNumber,
        string DisplayEpisodeNumber,
        bool ShouldWriteDisplayEpisode);

    private sealed class EpisodeNumberState
    {
        private readonly HashSet<int> _usedEpisodeNumbers = new();
        private readonly bool _useSortOrderFileNumbers;
        private int _autoNumber = 1;

        public EpisodeNumberState(IReadOnlyCollection<EpisodeItem> episodes)
        {
            _useSortOrderFileNumbers = ShouldUseSortOrderFileNumbers(episodes);
        }

        public EpisodeIdentity Resolve(EpisodeItem ep)
        {
            return ResolveEpisodeIdentity(ep, ref _autoNumber, _usedEpisodeNumbers, _useSortOrderFileNumbers);
        }
    }

    private static string TrimForLog(string? text, int max = 220)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    [ExcludeFromCodeCoverage(Justification = "Diagnostic-only network probe; generation behavior is covered without live HLS calls.")]
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

    [ExcludeFromCodeCoverage(Justification = "Diagnostic-only network probe; not deterministic in unit tests.")]
    private static async Task<string> ProbeHlsPlaylistAsync(string url, CancellationToken ct)
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

    internal static string PickImageUrl(ImageBlock? img)
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

    internal static string NormalizeImageUrlPreferJpg(string url)
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

    internal static string GetSafeImageExtensionFromUrl(string url)
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

    [ExcludeFromCodeCoverage(Justification = "Integration boundary: downloads remote poster/thumbnail assets.")]
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
            var bytes = await DownloadImageBytesAsync(url, ct);
            if (bytes is null)
            {
                TrackExistingImage(manifest, exists, path, kind, releaseId, episodeId, url);
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
            TrackExistingImage(manifest, exists, path, kind, releaseId, episodeId, url);
            log.Warn(ex, "Download image failed: {0}", url);
        }
    }

    [ExcludeFromCodeCoverage(Justification = "Integration boundary: downloads remote poster/thumbnail assets.")]
    private static async Task<byte[]?> DownloadImageBytesAsync(string url, CancellationToken ct)
    {
        using var resp = await _mediaHttp.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
            return null;

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        return IsSupportedImage(bytes) ? bytes : null;
    }

    internal static bool IsSupportedImage(byte[] bytes)
    {
        if (bytes.Length < 4)
            return false;

        var isJpg = bytes[0] == 0xFF && bytes[1] == 0xD8;
        var isPng = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        return isJpg || isPng;
    }

    [ExcludeFromCodeCoverage(Justification = "Fallback path for remote image download failures.")]
    private static void TrackExistingImage(
        ManagedLibraryManifest manifest,
        bool exists,
        string path,
        string kind,
        int releaseId,
        string? episodeId,
        string url)
    {
        if (exists)
            manifest.TrackExisting(path, kind, releaseId, episodeId, url);
    }

    [ExcludeFromCodeCoverage(Justification = "File byte comparison fallback for remote image cache writes.")]
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

    internal static string? ChooseHls(EpisodeItem ep, string pref)
    {
        return pref switch
        {
            "1080" => ep.Hls1080 ?? ep.Hls720 ?? ep.Hls480,
            "720" => ep.Hls720 ?? ep.Hls1080 ?? ep.Hls480,
            _ => ep.Hls480 ?? ep.Hls720 ?? ep.Hls1080
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
            var publishedBaseUrl = GetPublishedServerBaseUrl();
            var isContainer = IsRunningInContainer();

            if (!string.IsNullOrWhiteSpace(configuredBaseUrl) &&
                !TryNormalizePlaybackProxyBaseUrl(configuredBaseUrl, out _))
            {
                LogOnce(
                    ref _invalidConfiguredProxyUrlWarningLogged,
                    "Jellyfin Playback Proxy Base URL '{0}' is not a valid HTTP(S) base URL and will be ignored.",
                    configuredBaseUrl);
            }

            if (!string.IsNullOrWhiteSpace(publishedBaseUrl) &&
                !TryNormalizePlaybackProxyBaseUrl(publishedBaseUrl, out _))
            {
                LogOnce(
                    ref _invalidPublishedProxyUrlWarningLogged,
                    "{0} value '{1}' is not a valid HTTP(S) base URL and will be ignored.",
                    PublishedServerUrlEnvironmentVariable,
                    publishedBaseUrl);
            }

            var localBaseUrl = isContainer ? string.Empty : TryGetLocalPlaybackProxyBaseUrl();
            var baseUrl = ResolvePlaybackProxyBaseUrl(
                configuredBaseUrl,
                publishedBaseUrl,
                isContainer,
                localBaseUrl);

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                if (isContainer)
                {
                    LogOnce(
                        ref _containerProxyFallbackWarningLogged,
                        "Container detected, but no client-reachable Jellyfin playback proxy URL is configured. " +
                        "Generated STRM files will use direct AniLiberty HLS URLs instead of an internal container address. " +
                        "Set Jellyfin Playback Proxy Base URL or {0}, then regenerate the library to enable the proxy.",
                        PublishedServerUrlEnvironmentVariable);
                }

                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(configuredBaseUrl) &&
                TryNormalizePlaybackProxyBaseUrl(publishedBaseUrl, out var normalizedPublishedBaseUrl) &&
                string.Equals(baseUrl, normalizedPublishedBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                LogInfoOnce(
                    ref _publishedProxyUrlLogged,
                    "Using {0} for generated Jellyfin playback proxy URLs.",
                    PublishedServerUrlEnvironmentVariable);
            }

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

    private string TryGetLocalPlaybackProxyBaseUrl()
    {
        var bindAddresses = networkManager.GetInternalBindAddresses();
        var preferredIp = bindAddresses
            .Select(x => x.Address)
            .FirstOrDefault(ip => ip is not null &&
                                  !IPAddress.IsLoopback(ip) &&
                                  ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            ?? bindAddresses
                .Select(x => x.Address)
                .FirstOrDefault(ip => ip is not null && !IPAddress.IsLoopback(ip));

        return preferredIp is null
            ? string.Empty
            : serverHost.GetApiUrlForLocalAccess(preferredIp, serverHost.ListenWithHttps);
    }

    private static string? GetPublishedServerBaseUrl()
    {
        var publishedBaseUrl = Environment.GetEnvironmentVariable(PublishedServerUrlEnvironmentVariable);
        return string.IsNullOrWhiteSpace(publishedBaseUrl)
            ? Environment.GetEnvironmentVariable(UppercasePublishedServerUrlEnvironmentVariable)
            : publishedBaseUrl;
    }

    private static bool IsRunningInContainer()
    {
        return IsContainerEnvironment(
            Environment.GetEnvironmentVariable(DotnetRunningInContainerEnvironmentVariable),
            File.Exists("/.dockerenv"),
            File.Exists("/run/.containerenv"),
            Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST"));
    }

    internal static bool IsContainerEnvironment(
        string? dotnetRunningInContainer,
        bool hasDockerEnvironmentFile,
        bool hasContainerEnvironmentFile,
        string? kubernetesServiceHost)
    {
        return string.Equals(dotnetRunningInContainer, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(dotnetRunningInContainer, "1", StringComparison.Ordinal) ||
               hasDockerEnvironmentFile ||
               hasContainerEnvironmentFile ||
               !string.IsNullOrWhiteSpace(kubernetesServiceHost);
    }

    internal static string ResolvePlaybackProxyBaseUrl(
        string? configuredBaseUrl,
        string? publishedBaseUrl,
        bool isContainer,
        string? localBaseUrl)
    {
        if (TryNormalizePlaybackProxyBaseUrl(configuredBaseUrl, out var normalizedConfiguredBaseUrl))
            return normalizedConfiguredBaseUrl;

        if (TryNormalizePlaybackProxyBaseUrl(publishedBaseUrl, out var normalizedPublishedBaseUrl))
            return normalizedPublishedBaseUrl;

        if (isContainer)
            return string.Empty;

        return TryNormalizePlaybackProxyBaseUrl(localBaseUrl, out var normalizedLocalBaseUrl)
            ? normalizedLocalBaseUrl
            : string.Empty;
    }

    internal static bool TryNormalizePlaybackProxyBaseUrl(string? candidate, out string normalizedBaseUrl)
    {
        normalizedBaseUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) ||
            !Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        normalizedBaseUrl = candidate.Trim().TrimEnd('/');
        return true;
    }

    private void LogOnce(ref int marker, string format, params object?[] args)
    {
        if (Interlocked.Exchange(ref marker, 1) == 0)
            log.Warn(format, args);
    }

    private void LogInfoOnce(ref int marker, string format, params object?[] args)
    {
        if (Interlocked.Exchange(ref marker, 1) == 0)
            log.Info(format, args);
    }

    internal static string MakeFullUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;

        url = url.Trim();
        if (Uri.IsWellFormedUriString(url, UriKind.Absolute)) return url;

        if (url.StartsWith("//", StringComparison.Ordinal))
            return "https:" + url;

        return url.StartsWith('/')
            ? "https://api.anilibria.app" + url
            : "https://api.anilibria.app/" + url;
    }

    internal static string MakeSafe(string s)
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

        var tmp = RepeatedSeparatorRegex().Replace(sb.ToString(), " ").Trim();
        return tmp;
    }

    internal static string NormalizeTitleForFs(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        s = s.Replace('×', 'x').Replace('Ｘ', 'x');
        s = s.Replace("-", " ").Replace("–", " ").Replace("—", " ");
        s = s.Replace("Ω", "").Replace("ω", "");
        s = WhitespaceRegex().Replace(s, " ").Trim();
        return MakeSafe(CleanShowName(s));
    }

    internal static string MakeSafeXml(string? text)
    {
        return SecurityElement.Escape(text) ?? string.Empty;
    }

}

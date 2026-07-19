using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AniLibertyStrmPlugin.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class AniLibertyStrmGeneratorFocusedTests
{
    private const string DefaultHlsMarker = "__default_hls__";

    [Fact]
    public async Task GenerateTitlesAsync_ReturnsForNullEmptyAndMissingBasePathInputs()
    {
        using var output = TempOutput.Create();
        var generator = NewGenerator();

        await generator.GenerateTitlesAsync(null!, output.Path, "1080", progress: null, CancellationToken.None);
        await generator.GenerateTitlesAsync([], output.Path, "1080", progress: null, CancellationToken.None);
        await generator.GenerateTitlesAsync([BuildRelease(1301, "No Base Path")], " ", "1080", progress: null, CancellationToken.None);

        Assert.Empty(Directory.GetFiles(output.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task GenerateTitlesAsync_GeneratesMovieStrmSidecarAndNfo()
    {
        using var output = TempOutput.Create();
        var generator = NewGenerator();
        var movie = BuildRelease(
            id: 701,
            englishTitle: "Code: Sample Movie",
            type: "MOVIE",
            year: 2024,
            episodeId: "77777777-7777-7777-7777-777777777777");

        await generator.GenerateTitlesAsync([movie], output.Path, "1080", progress: null, CancellationToken.None);

        var movieDir = System.IO.Path.Combine(output.Path, "Code Sample (2024)");
        var strmPath = System.IO.Path.Combine(movieDir, "Code Sample (2024).strm");
        Assert.True(File.Exists(strmPath));
        Assert.Equal("https://api.anilibria.app/public/hls/701/playlist.m3u8", (await File.ReadAllTextAsync(strmPath)).Trim());
        Assert.Equal("77777777-7777-7777-7777-777777777777",
            (await File.ReadAllTextAsync(System.IO.Path.ChangeExtension(strmPath, ".aniid"))).Trim());
        Assert.Contains("<movie>", await File.ReadAllTextAsync(System.IO.Path.Combine(movieDir, "Code Sample (2024).nfo")));

        var sidecar = await ReadPopularityDocumentAsync(System.IO.Path.Combine(movieDir, AniLibertyPopularityDocument.FileName));
        Assert.Equal(701, sidecar.ReleaseId);
        Assert.Equal(1701, sidecar.Favorites);
        Assert.Equal(1702, sidecar.Collections.Planned);
        Assert.True(sidecar.IsPluginGenerated());
    }

    [Fact]
    public async Task GenerateTitlesAsync_GeneratesTvPopularitySidecar()
    {
        using var output = TempOutput.Create();
        var generator = NewGenerator();
        var release = BuildRelease(
            id: 702,
            englishTitle: "Popularity Show",
            year: 2026,
            episodeId: "77777777-7777-7777-7777-777777777778");

        await generator.GenerateTitlesAsync([release], output.Path, "1080", progress: null, CancellationToken.None);

        var sidecar = await ReadPopularityDocumentAsync(
            System.IO.Path.Combine(output.Path, "popularity show", AniLibertyPopularityDocument.FileName));
        Assert.Equal(702, sidecar.ReleaseId);
        Assert.Equal(1702, sidecar.Favorites);
        Assert.Equal(1703, sidecar.Collections.Planned);
        Assert.Equal(1704, sidecar.Collections.Watched);
        Assert.True(sidecar.IsPluginGenerated());
    }

    [Fact]
    public async Task GenerateTitlesAsync_HydratesMissingEpisodesAndUsesFallbackSeasons()
    {
        using var output = TempOutput.Create();
        var shell = BuildRelease(
            id: 801,
            englishTitle: "Fallback Show",
            year: 2024,
            episodeId: "88888888-8888-8888-8888-888888888888",
            includeEpisodes: false);
        var hydrated = BuildRelease(
            id: 801,
            englishTitle: "Fallback Show",
            year: 2024,
            episodeId: "88888888-8888-8888-8888-888888888888");
        var second = BuildRelease(
            id: 802,
            englishTitle: "Fallback Show",
            year: 2025,
            episodeId: "99999999-9999-9999-9999-999999999999");
        var generator = NewGenerator(new StubClient(hydrated));

        await generator.GenerateTitlesAsync([shell, second], output.Path, "1080", progress: null, CancellationToken.None);

        Assert.True(File.Exists(System.IO.Path.Combine(output.Path, "fallback show", "Season 01", "S01E01.strm")));
        Assert.True(File.Exists(System.IO.Path.Combine(output.Path, "fallback show", "Season 02", "S02E01.strm")));
    }

    [Fact]
    public async Task GenerateTitlesAsync_WritesSpecialsToSeasonZero()
    {
        using var output = TempOutput.Create();
        var generator = NewGenerator();
        var special = BuildRelease(
            id: 901,
            englishTitle: "Bonus Clip",
            type: "SPECIAL",
            year: 2026,
            episodeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        await generator.GenerateTitlesAsync([special], output.Path, "1080", progress: null, CancellationToken.None);

        Assert.True(File.Exists(System.IO.Path.Combine(output.Path, "bonus clip", "Season 00", "S00E01.strm")));
    }

    [Fact]
    public async Task GenerateTitlesAsync_RemovesSpecialsWordFromTvSpecialTitle()
    {
        using var output = TempOutput.Create();
        var generator = NewGenerator();
        var special = BuildRelease(
            id: 902,
            englishTitle: "Demo Specials",
            type: "TV",
            year: 2026,
            episodeId: "abababab-abab-abab-abab-abababababab");

        await generator.GenerateTitlesAsync([special], output.Path, "1080", progress: null, CancellationToken.None);

        Assert.True(File.Exists(System.IO.Path.Combine(output.Path, "demo", "Season 00", "S00E01.strm")));
    }

    [Fact]
    public async Task GenerateTitlesAsync_UsesFranchiseSeasonWhenAvailable()
    {
        using var output = TempOutput.Create();
        const int targetReleaseId = 1002;
        var release = BuildRelease(
            id: targetReleaseId,
            englishTitle: "Franchise Show",
            year: 2025,
            episodeId: "cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd");
        var generator = NewGenerator(new StubClient(franchises: BuildFranchise(targetReleaseId)));

        await generator.GenerateTitlesAsync([release], output.Path, "1080", progress: null, CancellationToken.None);

        Assert.True(File.Exists(System.IO.Path.Combine(output.Path, "franchise show", "Season 02", "S02E01.strm")));
    }

    [Fact]
    public async Task GenerateTitlesAsync_SkipsMissingHydrationAndHydrationFailures()
    {
        using var nullOutput = TempOutput.Create();
        var shell = BuildRelease(
            id: 1101,
            englishTitle: "Missing Detail",
            episodeId: "dededede-dede-dede-dede-dededededede",
            includeEpisodes: false);

        await NewGenerator(new StubClient()).GenerateTitlesAsync([shell], nullOutput.Path, "1080", progress: null, CancellationToken.None);
        Assert.Empty(Directory.GetFiles(nullOutput.Path, "*.strm", SearchOption.AllDirectories));

        using var throwOutput = TempOutput.Create();
        await NewGenerator(new StubClient(throwOnFetchRelease: true)).GenerateTitlesAsync([shell], throwOutput.Path, "1080", progress: null, CancellationToken.None);
        Assert.Empty(Directory.GetFiles(throwOutput.Path, "*.strm", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task GenerateTitlesAsync_SkipsEpisodeWithoutPlayableHls()
    {
        using var output = TempOutput.Create();
        var release = BuildRelease(
            id: 1201,
            englishTitle: "No Hls",
            episodeId: "efefefef-efef-efef-efef-efefefefefef",
            hls1080: null);

        await NewGenerator().GenerateTitlesAsync([release], output.Path, "1080", progress: null, CancellationToken.None);

        Assert.Empty(Directory.GetFiles(output.Path, "*.strm", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task GenerateTitlesAsync_LogsMissingHlsWhenPlaybackDiagnosticsEnabled()
    {
        using var host = PluginTestHost.Create(cfg => cfg.EnablePlaybackDiagnostics = true);
        using var output = TempOutput.Create();
        var release = BuildRelease(
            id: 1202,
            englishTitle: "No Hls Diagnostics",
            episodeId: "12121212-1212-1212-1212-121212121212",
            hls1080: null);

        await NewGenerator().GenerateTitlesAsync([release], output.Path, "1080", progress: null, CancellationToken.None);

        Assert.Empty(Directory.GetFiles(output.Path, "*.strm", SearchOption.AllDirectories));
        Assert.Contains("[PLAYBACK-DIAG] TV", host.Plugin.Configuration.LastTaskLog);
    }

    [Fact]
    public async Task GenerateTitlesAsync_UsesSortOrderForFractionalEpisodeSets()
    {
        using var output = TempOutput.Create();
        var release = BuildRelease(
            id: 1203,
            englishTitle: "Fractional Show",
            episodeId: "13131313-1313-1313-1313-131313131313");
        release.Episodes =
        [
            new EpisodeItem
            {
                Id = "13131313-1313-1313-1313-131313131313",
                Name = "Episode 1.5",
                NameEnglish = "Episode One Point Five",
                Ordinal = 1.5,
                SortOrder = 10,
                Duration = 90,
                Hls1080 = "/public/hls/1203/10.m3u8"
            },
            new EpisodeItem
            {
                Id = "14141414-1414-1414-1414-141414141414",
                Name = "Episode 2",
                NameEnglish = "Episode Two",
                Ordinal = 2,
                SortOrder = 11,
                Duration = 120,
                Hls1080 = "/public/hls/1203/11.m3u8"
            }
        ];

        await NewGenerator().GenerateTitlesAsync([release], output.Path, "1080", progress: null, CancellationToken.None);

        var seasonDir = System.IO.Path.Combine(output.Path, "fractional show", "Season 01");
        Assert.True(File.Exists(System.IO.Path.Combine(seasonDir, "S01E10.strm")));
        Assert.True(File.Exists(System.IO.Path.Combine(seasonDir, "S01E11.strm")));

        var firstNfo = await File.ReadAllTextAsync(System.IO.Path.Combine(seasonDir, "S01E10.nfo"));
        Assert.Contains("<displayepisode>1.5</displayepisode>", firstNfo);
        Assert.Contains("<runtime>2</runtime>", await File.ReadAllTextAsync(System.IO.Path.Combine(seasonDir, "S01E11.nfo")));
    }

    [Fact]
    public async Task GenerateTitlesAsync_DoesNotOverwriteUnmanagedTvShowNfo()
    {
        using var output = TempOutput.Create();
        var showDir = System.IO.Path.Combine(output.Path, "unmanaged show");
        Directory.CreateDirectory(showDir);
        var tvshowNfo = System.IO.Path.Combine(showDir, "tvshow.nfo");
        await File.WriteAllTextAsync(tvshowNfo, "<tvshow><title>Custom</title></tvshow>");

        await NewGenerator().GenerateTitlesAsync(
            [BuildRelease(1204, "Unmanaged Show", episodeId: "15151515-1515-1515-1515-151515151515")],
            output.Path,
            "1080",
            progress: null,
            CancellationToken.None);

        Assert.Equal("<tvshow><title>Custom</title></tvshow>", await File.ReadAllTextAsync(tvshowNfo));
    }

    [Fact]
    public async Task GenerateTitlesAsync_ReusesExistingEpisodeSidecarWhenItAlreadyMatches()
    {
        using var output = TempOutput.Create();
        var release = BuildRelease(
            id: 1205,
            englishTitle: "Sidecar Show",
            episodeId: "16161616-1616-1616-1616-161616161616");
        var generator = NewGenerator();

        await generator.GenerateTitlesAsync([release], output.Path, "1080", progress: null, CancellationToken.None);
        await generator.GenerateTitlesAsync([release], output.Path, "1080", progress: null, CancellationToken.None);

        var sidecar = System.IO.Path.Combine(output.Path, "sidecar show", "Season 01", "S01E01.aniid");
        Assert.Equal("16161616-1616-1616-1616-161616161616", (await File.ReadAllTextAsync(sidecar)).Trim());
    }

    [Fact]
    public void IsSupportedImage_DetectsJpegAndPngSignatures()
    {
        Assert.False(AniLibertyStrmGenerator.IsSupportedImage([0xFF, 0xD8, 0x00]));
        Assert.True(AniLibertyStrmGenerator.IsSupportedImage([0xFF, 0xD8, 0x00, 0x00]));
        Assert.True(AniLibertyStrmGenerator.IsSupportedImage([0x89, 0x50, 0x4E, 0x47]));
        Assert.False(AniLibertyStrmGenerator.IsSupportedImage([0x89, 0x50, 0x00, 0x47]));
    }

    private static AniLibertyStrmGenerator NewGenerator(IAniLibertyClient? client = null)
        => new AniLibertyStrmGenerator(
            NullLogger<AniLibertyStrmGenerator>.Instance,
            serverHost: null!,
            networkManager: null!,
            client: client ?? new StubClient());

    private static ReleaseResponse BuildRelease(
        int id,
        string englishTitle,
        string type = "TV",
        int year = 2026,
        string episodeId = "12345678-1234-1234-1234-123456789abc",
        bool includeEpisodes = true,
        string? hls1080 = DefaultHlsMarker)
    {
        if (hls1080 == DefaultHlsMarker)
            hls1080 = $"/public/hls/{id}/playlist.m3u8";

        return new ReleaseResponse
        {
            Id = id,
            Alias = $"release-{id}",
            Type = new ReleaseType { Value = type },
            Year = year,
            Name = new NameBlock { Main = englishTitle, English = englishTitle, Alternative = $"{englishTitle} alt" },
            Description = "Generated by focused tests.",
            EpisodesTotal = includeEpisodes ? 1 : 0,
            AddedInUsersFavorites = id + 1000,
            AddedInPlannedCollection = id + 1001,
            AddedInWatchedCollection = id + 1002,
            AddedInWatchingCollection = id + 1003,
            AddedInPostponedCollection = id + 1004,
            AddedInAbandonedCollection = id + 1005,
            Episodes = includeEpisodes
                ?
                [
                    new EpisodeItem
                    {
                        Id = episodeId,
                        Name = "Episode 1",
                        NameEnglish = "Episode One",
                        Ordinal = 1,
                        Duration = 1500,
                        Hls1080 = hls1080
                    }
                ]
                : []
        };
    }

    private static async Task<AniLibertyPopularityDocument> ReadPopularityDocumentAsync(string path)
    {
        Assert.True(File.Exists(path));
        var document = JsonSerializer.Deserialize<AniLibertyPopularityDocument>(await File.ReadAllTextAsync(path));
        return Assert.IsType<AniLibertyPopularityDocument>(document);
    }

    private static List<FranchiseInfo> BuildFranchise(int targetReleaseId)
        =>
        [
            new FranchiseInfo
            {
                Id = "franchise-1",
                Name = "Franchise",
                FranchiseReleases =
                [
                    BuildFranchiseLink(1000, "Franchise Specials", 2023, 0),
                    BuildFranchiseLink(1001, "Franchise Show", 2024, 1),
                    BuildFranchiseLink(targetReleaseId, "Franchise Show", 2025, 2)
                ]
            }
        ];

    private static FranchiseReleaseLink BuildFranchiseLink(int releaseId, string title, int year, int sortOrder)
        => new()
        {
            ReleaseId = releaseId,
            SortOrder = sortOrder,
            Release = new FranchiseReleaseRef
            {
                Id = releaseId,
                Type = new ReleaseType { Value = "TV" },
                Year = year,
                Name = new NameBlock { English = title, Main = title }
            }
        };

    private sealed class StubClient(
        ReleaseResponse? hydratedRelease = null,
        List<FranchiseInfo>? franchises = null,
        bool throwOnFetchRelease = false) : IAniLibertyClient
    {
        public Task<string> GetStringWithLoggingAsync(string url, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<string> GetStringAuthAsync(string url, string bearer, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<ReleaseResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<ReleaseResponse>> FetchFavoritesAsync(string bearerToken, int pageSize, int maxPages, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> UpdateViewTimecodesAsync(string bearerToken, IEnumerable<ViewTimecodeUpdateItem> updates, CancellationToken ct)
            => Task.FromResult(true);

        public Task<List<ViewTimecodeEntry>> FetchViewTimecodesAsync(string bearerToken, DateTimeOffset? since, CancellationToken ct)
            => Task.FromResult(new List<ViewTimecodeEntry>());

        public Task<ReleaseResponse?> FetchReleaseByIdAsync(int id, CancellationToken ct)
        {
            if (throwOnFetchRelease)
                throw new InvalidOperationException("detail fetch failed");

            return Task.FromResult(hydratedRelease?.Id == id ? hydratedRelease : null);
        }

        public Task<List<FranchiseInfo>?> FetchFranchisesForReleaseAsync(int releaseId, CancellationToken ct)
            => Task.FromResult(franchises);
    }

    private sealed class TempOutput : IDisposable
    {
        private TempOutput(string path) => Path = path;

        public string Path { get; }

        public static TempOutput Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "alib-focused-gen-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempOutput(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}

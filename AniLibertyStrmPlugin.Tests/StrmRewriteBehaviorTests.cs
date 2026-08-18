using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AniLibertyStrmPlugin.Models;
using AniLibertyStrmPlugin.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class StrmRewriteBehaviorTests
{
    [Fact]
    public async Task GenerateTitles_RewritesExistingStrmWhenUrlChanges()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "alib-strm-rewrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        try
        {
            var generator = NewGenerator();

            await generator.GenerateTitlesAsync(
                new[] { BuildRelease("/public/hls/demo/one.m3u8") },
                outDir,
                "1080",
                progress: null,
                token: CancellationToken.None);

            await generator.GenerateTitlesAsync(
                new[] { BuildRelease("/public/hls/demo/two.m3u8") },
                outDir,
                "1080",
                progress: null,
                token: CancellationToken.None);

            var strmFiles = Directory.GetFiles(outDir, "*.strm", SearchOption.AllDirectories);
            Assert.Single(strmFiles);

            var written = (await File.ReadAllTextAsync(strmFiles[0])).Trim();
            Assert.Equal("https://api.anilibria.app/public/hls/demo/two.m3u8", written);
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateTitles_RewritesExistingMediaSegmentStateWhenMarkersChange()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "alib-skip-rewrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        try
        {
            var generator = NewGenerator();

            await generator.GenerateTitlesAsync(
                new[] { BuildRelease("/public/hls/demo/one.m3u8", openingStart: 10, openingStop: 30) },
                outDir,
                "1080",
                progress: null,
                token: CancellationToken.None);

            await generator.GenerateTitlesAsync(
                new[]
                {
                    BuildRelease(
                        "/public/hls/demo/one.m3u8",
                        openingStart: 20,
                        openingStop: 40,
                        endingStart: 41,
                        endingStop: 55)
                },
                outDir,
                "1080",
                progress: null,
                token: CancellationToken.None);

            var strmPath = Assert.Single(Directory.GetFiles(outDir, "*.strm", SearchOption.AllDirectories));
            var mediaSegmentIndex = new AniLibertyMediaSegmentIndex();
            var entry = await mediaSegmentIndex.TryGetEntryForPathAsync(strmPath, CancellationToken.None);

            Assert.NotNull(entry);
            Assert.Equal("11111111-1111-1111-1111-111111111111", entry!.ReleaseEpisodeId);
            Assert.Equal(123, entry.ReleaseId);
            Assert.Collection(
                entry.Segments,
                segment =>
                {
                    Assert.Equal("Intro", segment.Type);
                    Assert.Equal(TimeSpan.FromSeconds(20).Ticks, segment.StartTicks);
                    Assert.Equal(TimeSpan.FromSeconds(40).Ticks, segment.EndTicks);
                },
                segment =>
                {
                    Assert.Equal("Outro", segment.Type);
                    Assert.Equal(TimeSpan.FromSeconds(41).Ticks, segment.StartTicks);
                    Assert.Equal(TimeSpan.FromSeconds(55).Ticks, segment.EndTicks);
                });

            Assert.Empty(Directory.GetFiles(outDir, "*.edl", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(outDir, "*.chapters.xml", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    private static AniLibertyStrmGenerator NewGenerator()
    {
        return new AniLibertyStrmGenerator(
            NullLogger<AniLibertyStrmGenerator>.Instance,
            serverHost: null!,
            networkManager: null!,
            client: new StubClient());
    }

    private static ReleaseResponse BuildRelease(
        string hls1080,
        int? openingStart = null,
        int? openingStop = null,
        int? endingStart = null,
        int? endingStop = null)
    {
        return new ReleaseResponse
        {
            Id = 123,
            Alias = "demo",
            Name = new NameBlock { Main = "Demo Show", English = "Demo Show" },
            Type = new ReleaseType { Value = "TV" },
            Year = 2025,
            Episodes =
            [
                new EpisodeItem
                {
                    Id = "11111111-1111-1111-1111-111111111111",
                    Ordinal = 1,
                    Name = "Episode 1",
                    Hls1080 = hls1080,
                    Opening = openingStart.HasValue && openingStop.HasValue
                        ? new OpeningBlock { Start = openingStart, Stop = openingStop }
                        : null,
                    Ending = endingStart.HasValue && endingStop.HasValue
                        ? new OpeningBlock { Start = endingStart, Stop = endingStop }
                        : null
                }
            ]
        };
    }

    private sealed class StubClient : IAniLibertyClient
    {
        public Task<string> GetStringWithLoggingAsync(string url, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<string> GetStringAuthAsync(string url, string bearer, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<ReleaseResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<ReleaseResponse>> FetchFavoritesAsync(
            string bearerToken,
            int pageSize,
            int maxPages,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> UpdateViewTimecodesAsync(
            string bearerToken,
            IEnumerable<ViewTimecodeUpdateItem> updates,
            CancellationToken ct)
            => Task.FromResult(true);

        public Task<List<ViewTimecodeEntry>> FetchViewTimecodesAsync(
            string bearerToken,
            DateTimeOffset? since,
            CancellationToken ct)
            => Task.FromResult(new List<ViewTimecodeEntry>());

        public Task<ReleaseResponse?> FetchReleaseByIdAsync(int id, CancellationToken ct)
            => Task.FromResult<ReleaseResponse?>(null);

        public Task<List<FranchiseInfo>?> FetchFranchisesForReleaseAsync(int releaseId, CancellationToken ct)
            => Task.FromResult<List<FranchiseInfo>?>(null);
    }
}

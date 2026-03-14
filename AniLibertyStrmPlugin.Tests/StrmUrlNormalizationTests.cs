using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AniLibertyStrmPlugin.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class StrmUrlNormalizationTests
{
    [Theory]
    [InlineData("/public/hls/a/master.m3u8", "https://api.anilibria.app/public/hls/a/master.m3u8")]
    [InlineData("//cdn.example.org/hls/master.m3u8", "https://cdn.example.org/hls/master.m3u8")]
    [InlineData("https://media.example.net/hls/master.m3u8", "https://media.example.net/hls/master.m3u8")]
    public async Task GenerateTitles_WritesNormalizedAbsoluteHlsUrl(string source, string expected)
    {
        var outDir = Path.Combine(Path.GetTempPath(), "alib-strm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        try
        {
            var generator = new AniLibertyStrmGenerator(
                NullLogger<AniLibertyStrmGenerator>.Instance,
                serverHost: null!,
                networkManager: null!,
                library: null!,
                chapters: null!,
                client: new StubClient());

            var release = new ReleaseResponse
            {
                Id = 123,
                Alias = "demo",
                Name = new NameBlock { Main = "Demo Show", English = "Demo Show" },
                Type = new ReleaseType { Value = "TV" },
                Year = 2025,
                Episodes = new List<EpisodeItem>
                {
                    new()
                    {
                        Id = "ep-1",
                        Ordinal = 1,
                        Name = "Episode 1",
                        Hls1080 = source
                    }
                }
            };

            await generator.GenerateTitlesAsync(
                new[] { release },
                outDir,
                "1080",
                progress: null,
                token: CancellationToken.None);

            var strmFiles = Directory.GetFiles(outDir, "*.strm", SearchOption.AllDirectories);
            Assert.Single(strmFiles);

            var written = (await File.ReadAllTextAsync(strmFiles[0])).Trim();
            Assert.Equal(expected, written);
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
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

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

public class EpisodeOrdinalHandlingTests
{
    [Fact]
    public void EpisodeItem_DeserializesFractionalOrdinal()
    {
        const string json = """
                            {
                              "id": "11111111-1111-1111-1111-111111111111",
                              "name": "",
                              "ordinal": 12.5,
                              "sort_order": 13
                            }
                            """;

        var episode = JsonSerializer.Deserialize<EpisodeItem>(json);

        Assert.NotNull(episode);
        Assert.Equal(12.5, episode!.Ordinal);
        Assert.Equal(13, episode.SortOrder);
    }

    [Fact]
    public async Task GenerateTitles_UsesSortOrderForFractionalEpisodeFileNames()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "alib-ordinal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        try
        {
            var generator = new AniLibertyStrmGenerator(
                NullLogger<AniLibertyStrmGenerator>.Instance,
                serverHost: null!,
                networkManager: null!,
                client: new StubClient());

            var release = new ReleaseResponse
            {
                Id = 700,
                Alias = "ordinal-demo",
                Name = new NameBlock { Main = "Ordinal Demo", English = "Ordinal Demo" },
                Type = new ReleaseType { Value = "TV" },
                Year = 2025,
                Episodes = new List<EpisodeItem>
                {
                    new()
                    {
                        Id = "11111111-1111-1111-1111-111111111111",
                        Ordinal = 12,
                        SortOrder = 12,
                        Hls1080 = "/public/hls/demo/e12.m3u8"
                    },
                    new()
                    {
                        Id = "22222222-2222-2222-2222-222222222222",
                        Ordinal = 12.5,
                        SortOrder = 13,
                        Hls1080 = "/public/hls/demo/e12-5.m3u8"
                    },
                    new()
                    {
                        Id = "33333333-3333-3333-3333-333333333333",
                        Ordinal = 13,
                        SortOrder = 14,
                        Hls1080 = "/public/hls/demo/e13.m3u8"
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
            Assert.Contains(strmFiles, path => path.EndsWith("S01E12.strm", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(strmFiles, path => path.EndsWith("S01E13.strm", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(strmFiles, path => path.EndsWith("S01E14.strm", StringComparison.OrdinalIgnoreCase));

            var nfo12_5 = await File.ReadAllTextAsync(
                Array.Find(Directory.GetFiles(outDir, "*.nfo", SearchOption.AllDirectories),
                    path => path.EndsWith("S01E13.nfo", StringComparison.OrdinalIgnoreCase))!);
            var nfo13 = await File.ReadAllTextAsync(
                Array.Find(Directory.GetFiles(outDir, "*.nfo", SearchOption.AllDirectories),
                    path => path.EndsWith("S01E14.nfo", StringComparison.OrdinalIgnoreCase))!);

            Assert.Contains("<title>Episode 12.5</title>", nfo12_5, StringComparison.Ordinal);
            Assert.Contains("<displayepisode>12.5</displayepisode>", nfo12_5, StringComparison.Ordinal);
            Assert.Contains("<displayepisode>13</displayepisode>", nfo13, StringComparison.Ordinal);
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

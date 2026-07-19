// ===== File: ManualStrmGenerationTests.cs =====
// Manual test: fetches favorite releases and generates all show/season folders.
// IMPORTANT:
//  - This test exits immediately unless both env vars are set.
//  - DO NOT STORE TOKENS IN THE REPOSITORY.
// How to run locally:
//  1) Set environment variables:
//     ANI_TOKEN      = <JWT AniLiberty>
//     ANI_OUTPUT_DIR = <output folder>
//  2) Run the test.

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests
{
    public class ManualStrmGenerationTests
    {
        // Preferred resolution: "1080", "720", or "480"
        private const string PreferredResolution = "1080";

        // Favorites pagination parameters
        private const int PageSize = 50;
        private const int MaxPages = 20;

        private static AniLibertyClient NewClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var log = NullLogger<AniLibertyClient>.Instance;
            return new AniLibertyClient(http, log);
        }

        [Fact]
        public async Task Generate_Folders_From_Favorites()
        {
            var aniToken = Environment.GetEnvironmentVariable("ANI_TOKEN") ?? string.Empty;
            var outputDir = Environment.GetEnvironmentVariable("ANI_OUTPUT_DIR") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(aniToken) || string.IsNullOrWhiteSpace(outputDir))
            {
                Assert.True(
                    string.IsNullOrWhiteSpace(aniToken) || string.IsNullOrWhiteSpace(outputDir),
                    "Manual generation test runs only when ANI_TOKEN and ANI_OUTPUT_DIR are both set.");
                return;
            }

            Directory.CreateDirectory(outputDir);

            var client = NewClient();
            var genLogger = NullLogger<AniLibertyStrmGenerator>.Instance;

            // No Jellyfin runtime in tests -> server/network services are null (generator handles this).
            var generator = new AniLibertyStrmGenerator(
                genLogger,
                serverHost: null!,
                networkManager: null!,
                client: client
            );

            var ct = CancellationToken.None;

            // 1) Fetch favorites
            var favorites = await client.FetchFavoritesAsync(aniToken, PageSize, MaxPages, ct);

            if (favorites.Count == 0)
                throw new InvalidOperationException("Избранное пустое или токен невалиден (API вернул 0 элементов).");

            // 2) Generate STRM/folders/seasons using the same rules as the plugin
            await generator.GenerateTitlesAsync(
                favorites,
                outputDir,
                PreferredResolution,
                progress: null,
                token: ct
            );
        }
    }
}

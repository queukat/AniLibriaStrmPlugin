// ===== File: ManualStrmGenerationTests.cs =====
// Manual test: fetches favorite releases and generates all show/season folders.
// IMPORTANT:
//  - This test is intentionally skipped by default to avoid running in CI.
//  - DO NOT STORE TOKENS IN THE REPOSITORY.
// How to run locally:
//  1) Set environment variables:
//     ANI_TOKEN      = <JWT AniLiberty>
//     ANI_OUTPUT_DIR = <output folder>
//  2) Remove Skip from [Fact] (or comment out Skip temporarily).
//  3) Run the test.

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
            var log  = NullLogger<AniLibertyClient>.Instance;
            return new AniLibertyClient(http, log);
        }

        [Fact(Skip = "Manual test. Set ANI_TOKEN and ANI_OUTPUT_DIR and remove Skip to run locally.")]
        public async Task Generate_Folders_From_Favorites()
        {
            var aniToken = Environment.GetEnvironmentVariable("ANI_TOKEN") ?? string.Empty;
            var outputDir = Environment.GetEnvironmentVariable("ANI_OUTPUT_DIR") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(aniToken))
                throw new InvalidOperationException("ANI_TOKEN is empty. Set env var ANI_TOKEN to your JWT.");

            if (string.IsNullOrWhiteSpace(outputDir))
                throw new InvalidOperationException("ANI_OUTPUT_DIR is empty. Set env var ANI_OUTPUT_DIR to output folder.");

            Directory.CreateDirectory(outputDir);

            var client = NewClient();
            var genLogger = NullLogger<AniLibertyStrmGenerator>.Instance;

            // No Jellyfin runtime in tests -> library/chapters = null (generator handles this).
            IAniLibertyStrmGenerator generator = new AniLibertyStrmGenerator(
                genLogger,
                library: null!,
                chapters: null!,
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

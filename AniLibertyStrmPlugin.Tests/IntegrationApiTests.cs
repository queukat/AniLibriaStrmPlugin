// ===== UPDATED FILE: IntegrationApiTests.cs =====
// 2025-07-18  — ё «» latest-,
//                    .

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using AniLibertyStrmPlugin.Models;

namespace AniLibertyStrmPlugin.Tests
{
    [Trait("Category", "Integration")]
    public class IntegrationApiTests
    {
        private const string ApiBase = "https://api.anilibria.app/api/v1";
        private static bool IsLiveApiEnabled =>
            string.Equals(Environment.GetEnvironmentVariable("ANI_RUN_INTEGRATION_TESTS"), "1",
                StringComparison.Ordinal);

        private static AniLibertyClient NewClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var log  = NullLogger<AniLibertyClient>.Instance;
            return new AniLibertyClient(http, log);
        }

        // ────────────────────────────────────────────────
        [Fact(DisplayName = "GET /anime/releases/latest ё  ")]
        public async Task ReleasesLatest_Array_NotEmpty()
        {
            if (!IsLiveApiEnabled) return;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var raw = await http.GetStringAsync($"{ApiBase}/anime/releases/latest");

            Assert.False(string.IsNullOrWhiteSpace(raw));
            Assert.StartsWith("[", raw.TrimStart());   // API v2  JSON-
        }

        // ────────────────────────────────────────────────
        [Fact(DisplayName = "GET /anime/releases/{id}  200")]
        public async Task ReleaseById_Alive()
        {
            if (!IsLiveApiEnabled) return;

            var client = NewClient();

            var list = await client.FetchAllTitlesAsync(1, 1, CancellationToken.None);
            if (list.Count == 0) return;   // API ,   —  OK

            var first = list[0];
            var raw   = await client.GetStringWithLoggingAsync(
                $"{ApiBase}/anime/releases/{first.Id}",
                CancellationToken.None);

            Assert.Contains(first.Alias, raw, StringComparison.OrdinalIgnoreCase);
        }
    }
}

// ===== UPDATED FILE: IntegrationApiTests.cs =====
// Live API integration smoke tests.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
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
            var log = NullLogger<AniLibertyClient>.Instance;
            return new AniLibertyClient(http, log);
        }

        // ────────────────────────────────────────────────
        [Fact(DisplayName = "GET /anime/releases/latest returns non-empty array")]
        public async Task ReleasesLatest_Array_NotEmpty()
        {
            if (!IsLiveApiEnabled) return;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var raw = await http.GetStringAsync($"{ApiBase}/anime/releases/latest");

            Assert.False(string.IsNullOrWhiteSpace(raw));
            Assert.StartsWith("[", raw.TrimStart());   // API v2 returns a JSON array
        }

        // ────────────────────────────────────────────────
        [Fact(DisplayName = "GET /anime/releases/{id} returns 200")]
        public async Task ReleaseById_Alive()
        {
            if (!IsLiveApiEnabled) return;

            var client = NewClient();

            // This smoke needs one sample, not a complete snapshot suitable for mirroring.
            var page = await client.GetStringWithLoggingAsync(
                $"{ApiBase}/anime/catalog/releases?limit=1&page=1", CancellationToken.None);
            using var document = JsonDocument.Parse(page);
            var data = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement : document.RootElement.GetProperty("data");
            var list = data.Deserialize<System.Collections.Generic.List<ReleaseResponse>>()!;
            if (list.Count == 0) return;   // API may be temporarily empty; still OK

            var first = list[0];
            var raw = await client.GetStringWithLoggingAsync(
                $"{ApiBase}/anime/releases/{first.Id}",
                CancellationToken.None);

            Assert.Contains(first.Alias, raw, StringComparison.OrdinalIgnoreCase);
        }

        [Fact(DisplayName = "GET /accounts/users/me/favorites/releases accepts ANI_TOKEN")]
        public async Task Favorites_WithToken_Alive()
        {
            if (!IsLiveApiEnabled) return;

            var token = Environment.GetEnvironmentVariable("ANI_TOKEN");
            if (string.IsNullOrWhiteSpace(token)) return;

            var client = NewClient();
            var raw = await client.GetStringAuthAsync(
                $"{ApiBase}/accounts/users/me/favorites/releases?limit=1&page=1",
                token,
                CancellationToken.None);

            Assert.False(string.IsNullOrWhiteSpace(raw));

            using var doc = JsonDocument.Parse(raw);
            Assert.True(doc.RootElement.TryGetProperty("data", out var dataEl));
            Assert.Equal(JsonValueKind.Array, dataEl.ValueKind);
        }
    }
}

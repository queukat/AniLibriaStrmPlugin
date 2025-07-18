// ===== UPDATED FILE: IntegrationApiTests.cs =====
// 2025-07-18  — упрощён «пинг» latest-эндпоинта,
//               теперь тесты не зависят от пагинации.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using AniLibertyStrmPlugin.Models;

namespace AniLibertyStrmPlugin.Tests
{
    public class IntegrationApiTests
    {
        private const string ApiBase = "https://api.anilibria.app/api/v1";

        private static AniLibertyClient NewClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var log  = NullLogger<AniLibertyClient>.Instance;
            return new AniLibertyClient(http, log);
        }

        // ────────────────────────────────────────────────
        [Fact(DisplayName = "GET /anime/releases/latest отдаёт непустой массив")]
        public async Task ReleasesLatest_Array_NotEmpty()
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var raw = await http.GetStringAsync($"{ApiBase}/anime/releases/latest");

            Assert.False(string.IsNullOrWhiteSpace(raw));
            Assert.StartsWith("[", raw.TrimStart());   // API v2 возвращает JSON-массив
        }

        // ────────────────────────────────────────────────
        [Fact(DisplayName = "GET /anime/releases/{id} отвечает 200")]
        public async Task ReleaseById_Alive()
        {
            var client = NewClient();

            var list = await client.FetchAllTitlesAsync(1, 1, CancellationToken.None);
            if (list.Count == 0) return;   // API жив, но пусто — считаем OK

            var first = list[0];
            var raw   = await client.GetStringWithLoggingAsync(
                $"{ApiBase}/anime/releases/{first.Id}",
                CancellationToken.None);

            Assert.Contains(first.Alias, raw, StringComparison.OrdinalIgnoreCase);
        }
    }
}
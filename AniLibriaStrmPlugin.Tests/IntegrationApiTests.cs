// ===== UPDATED FILE: IntegrationApiTests.cs =====
// 2025-07-15 — приведено в соответствие с API v1
// * Тесты используют /anime/releases/latest и /anime/releases/{id}
// * Проверяем наличие поля Alias вместо устаревшего Code

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AniLibriaStrmPlugin;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibriaStrmPlugin.Tests;

/// <summary>
///  Лёгкий интеграционный тест: минимум запросов к прод-API,
///  чтобы убедиться, что /anime/releases/latest и /anime/releases/{id} живы.
/// </summary>
public class IntegrationApiTests
{
    private static AniLibriaClient NewClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var log  = NullLogger<AniLibriaClient>.Instance;
        return new AniLibriaClient(http, log);
    }

    [Fact(DisplayName = "GET /anime/releases/latest отвечает 200 и JSON десериализуется")]
    public async Task ReleasesLatest_Alive()
    {
        var client = NewClient();
        var list   = await client.FetchAllTitlesAsync(5, 1, CancellationToken.None);

        // Проверяем только, что запрос не упал и вернулся валидный объект
        Assert.NotNull(list);
        Assert.NotEmpty(list);
    }

    [Fact(DisplayName = "GET /anime/releases/{id} отвечает 200")]
    public async Task ReleaseById_Alive()
    {
        var client = NewClient();

        var list = await client.FetchAllTitlesAsync(1, 1, CancellationToken.None);
        if (list.Count == 0) return;   // API живо, но пусто — считаем ок

        var first = list[0];
        var raw   = await client.GetStringWithLoggingAsync(
            $"https://api.anilibria.app/api/v1/anime/releases/{first.Id}",
            CancellationToken.None);

        // В ответе должен присутствовать alias релиза
        Assert.Contains(first.Alias, raw, StringComparison.OrdinalIgnoreCase);
    }
}
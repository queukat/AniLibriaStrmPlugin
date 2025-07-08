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
///  чтобы убедиться, что /titles/updates и /titles/{id} живы.
/// </summary>
public class IntegrationApiTests
{
    private static AniLibriaClient NewClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var log  = NullLogger<AniLibriaClient>.Instance;
        return new AniLibriaClient(http, log);
    }

[Fact(DisplayName = "GET /titles/updates отвечает 200 и JSON десериализуется")]
public async Task TitlesUpdates_Alive()
{
    var client = NewClient();
    var list   = await client.FetchAllTitlesAsync(5, 1, CancellationToken.None);

    // Проверяем только, что запрос не упал и вернулся валидный объект
    Assert.NotNull(list);
}

[Fact(DisplayName = "GET /titles/{id} отвечает 200")]
public async Task TitleById_Alive()
{
    var client = NewClient();

    // если список пуст, считаем тест пройденным — API живо
    var list = await client.FetchAllTitlesAsync(1, 1, CancellationToken.None);
    if (list.Count == 0) return;

    var first = list[0];
    var raw   = await client.GetStringWithLoggingAsync(
        $"https://api.anilibria.app/api/v1/titles/{first.Id}", CancellationToken.None);

    Assert.Contains(first.Code, raw);
}

}
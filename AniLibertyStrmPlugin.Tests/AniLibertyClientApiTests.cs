using System.Net;
using System.Net.Http;
using System.Text.Json;
using AniLibertyStrmPlugin.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class AniLibertyClientApiTests
{
    private static readonly int[] ExpectedAllTitleIds = [1, 2, 3];
    private static readonly int[] ExpectedFavoriteIds = [10, 11];

    [Fact]
    public async Task GetStringWithLoggingAsync_ReturnsBodyAndThrowsOnFailure()
    {
        var okHandler = new QueueHandler(_ => Json(HttpStatusCode.OK, """{"ok":true}"""));
        var okClient = NewClient(okHandler);

        Assert.Equal("""{"ok":true}""", await okClient.GetStringWithLoggingAsync("https://api.test/ok", CancellationToken.None));

        var failClient = NewClient(new QueueHandler(_ => Json(HttpStatusCode.BadGateway, """{"error":"no"}""")));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            failClient.GetStringWithLoggingAsync("https://api.test/fail", CancellationToken.None));
    }

    [Fact]
    public async Task GetStringAuthAsync_SendsBearerToken()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK, "body"));
        var client = NewClient(handler);

        var body = await client.GetStringAuthAsync("https://api.test/auth", "token-value", CancellationToken.None);

        Assert.Equal("body", body);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("token-value", request.AuthorizationParameter);
    }

    [Fact]
    public async Task FetchAllTitlesAsync_ReadsArrayAndLegacyDataPayloads()
    {
        var firstPage = """
            [
              { "id": 1, "alias": "first", "name": { "main": "First" } },
              { "id": 2, "alias": "second", "name": { "main": "Second" } }
            ]
            """;
        var secondPage = """
            {
              "data": [
                { "id": 3, "alias": "third", "name": { "main": "Third" } }
              ]
            }
            """;
        var handler = new QueueHandler(
            _ => Json(HttpStatusCode.OK, firstPage),
            _ => Json(HttpStatusCode.OK, secondPage));
        var client = NewClient(handler);

        var titles = await client.FetchAllTitlesAsync(pageSize: 2, maxPages: 3, CancellationToken.None);

        Assert.Equal(ExpectedAllTitleIds, titles.Select(x => x.Id));
        Assert.Equal("first", titles[0].Alias);
        Assert.Equal("Third", titles[2].Name.Main);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("page=1", handler.Requests[0].Uri.Query);
        Assert.Contains("page=2", handler.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task FetchAllTitlesAsync_StopsOnEmptyOrBadPayload()
    {
        var emptyClient = NewClient(new QueueHandler(_ => Json(HttpStatusCode.OK, "[]")));
        Assert.Empty(await emptyClient.FetchAllTitlesAsync(pageSize: 10, maxPages: 5, CancellationToken.None));

        var badClient = NewClient(new QueueHandler(_ => Json(HttpStatusCode.OK, "{ not-json")));
        Assert.Empty(await badClient.FetchAllTitlesAsync(pageSize: 10, maxPages: 5, CancellationToken.None));
    }

    [Fact]
    public async Task FetchFavoritesAsync_AccumulatesPagesAndStopsOnEmptyPage()
    {
        var firstPage = """
            { "data": [
              { "id": 10, "alias": "fav-one", "name": { "main": "Fav one" } },
              { "id": 11, "alias": "fav-two", "name": { "main": "Fav two" } }
            ] }
            """;
        var secondPage = """{ "data": [] }""";
        var handler = new QueueHandler(
            _ => Json(HttpStatusCode.OK, firstPage),
            _ => Json(HttpStatusCode.OK, secondPage));
        var client = NewClient(handler);

        var favorites = await client.FetchFavoritesAsync("token-value", pageSize: 2, maxPages: 5, CancellationToken.None);

        Assert.Equal(ExpectedFavoriteIds, favorites.Select(x => x.Id));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer", request.AuthorizationScheme);
            Assert.Equal("token-value", request.AuthorizationParameter);
        });
    }

    [Fact]
    public async Task FetchFavoritesAsync_ThrowsAuthExpiredOnUnauthorized()
    {
        var client = NewClient(new QueueHandler(_ => Json(HttpStatusCode.Unauthorized, """{"error":"bad token"}""")));

        var ex = await Assert.ThrowsAsync<AniLibertyAuthExpiredException>(() =>
            client.FetchFavoritesAsync("token-value", pageSize: 20, maxPages: 2, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task FetchFavoritesAsync_ReturnsEmptyOnNonAuthPageFailure()
    {
        var client = NewClient(new QueueHandler(_ => Json(HttpStatusCode.InternalServerError, """{"error":"fail"}""")));

        var favorites = await client.FetchFavoritesAsync("token-value", pageSize: 20, maxPages: 2, CancellationToken.None);

        Assert.Empty(favorites);
    }

    [Fact]
    public async Task UpdateViewTimecodesAsync_ValidatesTokenAndPayload()
    {
        var client = NewClient(new QueueHandler());

        Assert.False(await client.UpdateViewTimecodesAsync(" ", [new ViewTimecodeUpdateItem { ReleaseEpisodeId = "id" }], CancellationToken.None));
        Assert.False(await client.UpdateViewTimecodesAsync("token", [], CancellationToken.None));
        Assert.False(await client.UpdateViewTimecodesAsync("token", [new ViewTimecodeUpdateItem { ReleaseEpisodeId = " " }], CancellationToken.None));
    }

    [Fact]
    public async Task UpdateViewTimecodesAsync_PostsFilteredPayload()
    {
        var handler = new QueueHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("token-value", request.Headers.Authorization?.Parameter);

            var json = request.Content?.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult() ?? string.Empty;
            using var doc = JsonDocument.Parse(json);
            var item = Assert.Single(doc.RootElement.EnumerateArray());
            Assert.Equal("episode-id", item.GetProperty("release_episode_id").GetString());
            Assert.Equal(12.5, item.GetProperty("time").GetDouble());
            Assert.True(item.GetProperty("is_watched").GetBoolean());

            return Json(HttpStatusCode.NoContent, string.Empty);
        });
        var client = NewClient(handler);

        var ok = await client.UpdateViewTimecodesAsync(
            "token-value",
            [
                new ViewTimecodeUpdateItem { ReleaseEpisodeId = "episode-id", Time = 12.5, IsWatched = true },
                new ViewTimecodeUpdateItem { ReleaseEpisodeId = " ", Time = 99, IsWatched = false }
            ],
            CancellationToken.None);

        Assert.True(ok);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UpdateViewTimecodesAsync_ReturnsFalseOnHttpFailure()
    {
        var client = NewClient(new QueueHandler(_ => Json(HttpStatusCode.InternalServerError, """{"error":"fail"}""")));

        var ok = await client.UpdateViewTimecodesAsync(
            "token-value",
            [new ViewTimecodeUpdateItem { ReleaseEpisodeId = "episode-id", Time = 1 }],
            CancellationToken.None);

        Assert.False(ok);

        var authClient = NewClient(new QueueHandler(_ => Json(HttpStatusCode.Forbidden, """{"error":"expired"}""")));
        await Assert.ThrowsAsync<AniLibertyAuthExpiredException>(() =>
            authClient.UpdateViewTimecodesAsync(
                "token-value",
                [new ViewTimecodeUpdateItem { ReleaseEpisodeId = "episode-id", Time = 1 }],
                CancellationToken.None));
    }

    [Fact]
    public async Task FetchReleaseByIdAsync_ReturnsReleaseOrNull()
    {
        var okClient = NewClient(new QueueHandler(_ => Json(HttpStatusCode.OK, """{"id":42,"alias":"answer","name":{"main":"Answer"}}""")));

        var release = await okClient.FetchReleaseByIdAsync(42, CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal(42, release.Id);
        Assert.Equal("answer", release.Alias);

        var failClient = NewClient(new QueueHandler(_ => Json(HttpStatusCode.NotFound, """{"error":"missing"}""")));
        Assert.Null(await failClient.FetchReleaseByIdAsync(404, CancellationToken.None));
    }

    [Fact]
    public async Task FetchFranchisesForReleaseAsync_ReturnsFranchisesOrNull()
    {
        var okClient = NewClient(new QueueHandler(_ => Json(HttpStatusCode.OK, """[{ "id": "f1", "name": "Franchise" }]""")));

        var franchises = await okClient.FetchFranchisesForReleaseAsync(42, CancellationToken.None);

        var franchise = Assert.Single(franchises!);
        Assert.Equal("f1", franchise.Id);
        Assert.Equal("Franchise", franchise.Name);

        var failClient = NewClient(new QueueHandler(_ => Json(HttpStatusCode.InternalServerError, """{"error":"fail"}""")));
        Assert.Null(await failClient.FetchFranchisesForReleaseAsync(42, CancellationToken.None));
    }

    private static AniLibertyClient NewClient(HttpMessageHandler handler)
        => new(new HttpClient(handler), NullLogger<AniLibertyClient>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body) };

    private sealed class QueueHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri ?? new Uri("http://missing.local"),
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));

            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter);
}

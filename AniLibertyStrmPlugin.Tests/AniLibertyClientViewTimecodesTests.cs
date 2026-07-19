using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class AniLibertyClientViewTimecodesTests
{
    [Fact]
    public async Task FetchViewTimecodesAsync_ParsesObjectsArraysAndMergesDuplicates()
    {
        const string firstId = "11111111-1111-1111-1111-111111111111";
        const string secondId = "22222222-2222-2222-2222-222222222222";
        var since = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.FromHours(4));
        var payload = $$"""
            {
              "data": [
                { "release_episode_id": " {{firstId}} ", "time": 12.5, "is_watched": false },
                [ "{{secondId}}", "42.5", "true" ],
                { "release_episode_id": "{{firstId}}", "time": 8, "is_watched": true },
                { "release_episode_id": "{{firstId}}", "time": 5, "is_watched": false }
              ]
            }
            """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, payload));
        var client = NewClient(handler);

        var rows = await client.FetchViewTimecodesAsync("token-value", since, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        var first = rows.Single(x => x.ReleaseEpisodeId == firstId);
        Assert.Equal(12.5, first.Time);
        Assert.True(first.IsWatched);

        var second = rows.Single(x => x.ReleaseEpisodeId == secondId);
        Assert.Equal(42.5, second.Time);
        Assert.True(second.IsWatched);

        Assert.Single(handler.Requests);
        Assert.Equal("Bearer", handler.Requests[0].AuthorizationScheme);
        Assert.Equal("token-value", handler.Requests[0].AuthorizationParameter);
        Assert.Contains("since=", handler.Requests[0].Uri.Query);
    }

    [Fact]
    public async Task FetchViewTimecodesAsync_IgnoresMalformedRowsButKeepsValidStringValues()
    {
        const string validId = "33333333-3333-3333-3333-333333333333";
        var payload = $$"""
            [
              [ "too-short", 1 ],
              [ 123, 10, true ],
              { "release_episode_id": "not-a-guid", "time": "1", "is_watched": "false" },
              { "release_episode_id": "{{validId}}", "time": false, "is_watched": true },
              { "release_episode_id": "{{validId}}", "time": "2", "is_watched": "false" }
            ]
            """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, payload));
        var client = NewClient(handler);

        var rows = await client.FetchViewTimecodesAsync("token-value", since: null, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(validId, row.ReleaseEpisodeId);
        Assert.Equal(2, row.Time);
        Assert.False(row.IsWatched);
    }

    [Fact]
    public async Task FetchViewTimecodesAsync_ReturnsEmptyForBlankTokenHttpFailureAndBadJson()
    {
        var handler = new StubHandler(
            _ => Json(HttpStatusCode.InternalServerError, """{"error":"nope"}"""),
            _ => Json(HttpStatusCode.OK, "{ not valid json"));
        var client = NewClient(handler);

        Assert.Empty(await client.FetchViewTimecodesAsync(" ", null, CancellationToken.None));
        Assert.Empty(await client.FetchViewTimecodesAsync("token-value", null, CancellationToken.None));
        Assert.Empty(await client.FetchViewTimecodesAsync("token-value", null, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchViewTimecodesAsync_ThrowsAuthExpiredOnUnauthorized()
    {
        var client = NewClient(new StubHandler(_ => Json(HttpStatusCode.Forbidden, """{"error":"expired"}""")));

        var ex = await Assert.ThrowsAsync<AniLibertyAuthExpiredException>(() =>
            client.FetchViewTimecodesAsync("token-value", null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    private static AniLibertyClient NewClient(HttpMessageHandler handler)
        => new(new HttpClient(handler), NullLogger<AniLibertyClient>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body) };

    private sealed class StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri ?? new Uri("http://missing.local"),
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));

            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed record CapturedRequest(
        Uri Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter);
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class PlaybackProxyControllerTests
{
    [Fact]
    public async Task Hls_StreamsNonPlaylistContentToResponseBody()
    {
        var factory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3, 4])
        });
        factory.Response.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp2t");

        var (controller, body) = CreateController(factory);

        var result = await controller.Hls(
            "https://cache.libria.fun/videos/media/ts/10095/6/1080/seg-0001.ts",
            CancellationToken.None);

        Assert.IsType<EmptyResult>(result);
        Assert.Equal("video/mp2t", controller.Response.ContentType);
        Assert.Equal([1, 2, 3, 4], body.ToArray());
        Assert.Equal("AniLibertyMediaProxy", Assert.Single(factory.ClientNames));
    }

    [Fact]
    public async Task Hls_RewritesPlaylistThroughLocalProxy()
    {
        const string playlist = """
                                #EXTM3U
                                #EXT-X-KEY:METHOD=AES-128,URI="key.bin"
                                #EXTINF:4.0,
                                seg-0001.ts
                                """;

        var factory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(playlist, Encoding.UTF8, "application/vnd.apple.mpegurl")
        });
        var (controller, _) = CreateController(factory);

        var result = await controller.Hls(
            "https://cache.libria.fun/videos/media/ts/10095/6/1080/master.m3u8",
            CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/vnd.apple.mpegurl", content.ContentType);
        Assert.Contains("http://127.0.0.1:8096/AniLibertyPlayback/hls?url=", content.Content, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("https://cache.libria.fun/videos/media/ts/10095/6/1080/seg-0001.ts"), content.Content, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("https://cache.libria.fun/videos/media/ts/10095/6/1080/key.bin"), content.Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/evil.ts")]
    public async Task Hls_RejectsMissingOrUnsupportedUpstream(string? url)
    {
        var factory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK));
        var (controller, _) = CreateController(factory);

        var result = await controller.Hls(url, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(factory.ClientNames);
    }

    [Fact]
    public async Task Hls_RejectsUnsupportedRedirect()
    {
        var factory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3])
        })
        {
            EffectiveResponseUri = new Uri("https://example.com/evil.ts")
        };
        var (controller, _) = CreateController(factory);

        var result = await controller.Hls(
            "https://cache.libria.fun/videos/media/ts/10095/6/1080/seg-0001.ts",
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Unsupported upstream redirect.", badRequest.Value);
    }

    [Fact]
    public async Task Hls_ReturnsUpstreamFailureStatus()
    {
        var factory = new StubHttpClientFactory(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var (controller, _) = CreateController(factory);

        var result = await controller.Hls(
            "https://cache.libria.fun/videos/media/ts/10095/6/1080/seg-0001.ts",
            CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, status.StatusCode);
    }

    private static (AniLibertyPlaybackController Controller, MemoryStream Body) CreateController(StubHttpClientFactory factory)
    {
        var controller = new AniLibertyPlaybackController(
            factory,
            NullLogger<AniLibertyPlaybackController>.Instance);

        var body = new MemoryStream();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Response = { Body = body },
                Request =
                {
                    Scheme = "http",
                    Host = new HostString("127.0.0.1:8096"),
                    Path = "/AniLibertyPlayback/hls"
                }
            }
        };

        return (controller, body);
    }

    private sealed class StubHttpClientFactory(HttpResponseMessage response) : IHttpClientFactory
    {
        public HttpResponseMessage Response { get; } = response;
        public Uri? EffectiveResponseUri { get; init; }
        public List<string> ClientNames { get; } = new();

        public HttpClient CreateClient(string name)
        {
            ClientNames.Add(name);
            return new HttpClient(new StubHandler(Response, EffectiveResponseUri));
        }
    }

    private sealed class StubHandler(HttpResponseMessage response, Uri? effectiveResponseUri) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            response.RequestMessage = effectiveResponseUri is null
                ? request
                : new HttpRequestMessage(request.Method, effectiveResponseUri);
            return Task.FromResult(response);
        }
    }
}

using System.Net;
using System.Net.Http.Headers;
using AniLibertyStrmPlugin.Configuration;
using AniLibertyStrmPlugin.Models;
using AniLibertyStrmPlugin.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class ManagedImageRefreshTests
{
    private static readonly byte[] OldImage = [0xff, 0xd8, 0xff, 0xe0, 1];
    private static readonly byte[] NewImage = [0xff, 0xd8, 0xff, 0xe0, 2];
    private const string Url = "https://images.test/poster.jpg";

    [Fact]
    public async Task SameRun_ReusesBytesAcrossDestinationPaths()
    {
        using var host = PluginTestHost.Create();
        var manifest = await ManagedLibraryManifest.LoadAsync(host.Root, CancellationToken.None);
        var handler = new Handler(_ => Image(OldImage));
        using var http = new HttpClient(handler);
        var session = new AniLibertyStrmGenerator.ImageRefreshSession(http);
        var first = await session.GetAsync(Url, Path.Combine(host.Root, "one.jpg"), manifest, CancellationToken.None);
        var second = await session.GetAsync(Url, Path.Combine(host.Root, "two.jpg"), manifest, CancellationToken.None);
        Assert.Same(first, second);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NextRun_UsesPersistedValidatorsAnd304WithoutReadingResponseBody(bool useEtag)
    {
        using var host = PluginTestHost.Create();
        var path = Path.Combine(host.Root, "poster.jpg");
        var modified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var manifest = await ManagedLibraryManifest.LoadAsync(host.Root, CancellationToken.None);
        await File.WriteAllBytesAsync(path, OldImage);
        manifest.TrackImage(path, "show-poster", OldImage, 1, null, Url, useEtag ? "\"v1\"" : null, modified);
        await manifest.SaveAsync(StaleCleanupMode.Off, CancellationToken.None);
        manifest = await ManagedLibraryManifest.LoadAsync(host.Root, CancellationToken.None);
        var handler = new Handler(request =>
        {
            Assert.Equal(modified, request.Headers.IfModifiedSince);
            if (useEtag) Assert.Equal("\"v1\"", Assert.Single(request.Headers.IfNoneMatch).ToString());
            return new HttpResponseMessage(HttpStatusCode.NotModified) { Content = new UnreadableContent() };
        });
        using var http = new HttpClient(handler);
        var image = await new AniLibertyStrmGenerator.ImageRefreshSession(http).GetAsync(Url, path, manifest, CancellationToken.None);
        Assert.Equal(OldImage, image!.Bytes);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task SameUrlChangedContent_IsRefreshedAndValidatorsReplaced()
    {
        using var host = PluginTestHost.Create();
        var path = Path.Combine(host.Root, "poster.jpg");
        var manifest = await ManagedLibraryManifest.LoadAsync(host.Root, CancellationToken.None);
        await File.WriteAllBytesAsync(path, OldImage);
        manifest.TrackImage(path, "show-poster", OldImage, 1, null, Url, "\"v1\"", null);
        var handler = new Handler(request =>
        {
            Assert.Equal("\"v1\"", Assert.Single(request.Headers.IfNoneMatch).ToString());
            return Image(NewImage, "\"v2\"");
        });
        using var http = new HttpClient(handler);
        var image = await new AniLibertyStrmGenerator.ImageRefreshSession(http).GetAsync(Url, path, manifest, CancellationToken.None);
        Assert.Equal(NewImage, image!.Bytes);
        Assert.Equal("\"v2\"", image.ETag);
    }

    [Theory]
    [InlineData("modified")]
    [InlineData("missing")]
    [InlineData("changed-url")]
    [InlineData("legacy")]
    public async Task UnboundLocalContent_DoesNotSendValidators(string scenario)
    {
        using var host = PluginTestHost.Create();
        var path = Path.Combine(host.Root, "poster.jpg");
        var manifest = await ManagedLibraryManifest.LoadAsync(host.Root, CancellationToken.None);
        await File.WriteAllBytesAsync(path, OldImage);
        if (scenario != "legacy")
            manifest.TrackImage(path, "show-poster", OldImage, 1, null, Url, "\"v1\"", null);
        if (scenario == "modified") await File.WriteAllBytesAsync(path, NewImage);
        if (scenario == "missing") File.Delete(path);
        var handler = new Handler(request =>
        {
            Assert.Empty(request.Headers.IfNoneMatch);
            Assert.Null(request.Headers.IfModifiedSince);
            return Image(NewImage);
        });
        using var http = new HttpClient(handler);
        var requested = scenario == "changed-url" ? Url + "?v=2" : Url;
        var image = await new AniLibertyStrmGenerator.ImageRefreshSession(http).GetAsync(requested, path, manifest, CancellationToken.None);
        Assert.Equal(NewImage, image!.Bytes);
    }

    [Fact]
    public async Task Generator_DeduplicatesPostersAndPreservesNoopMtimeAcrossRuns()
    {
        using var host = PluginTestHost.Create();
        var output = Path.Combine(host.Root, "output");
        var handler = new Handler(request => request.Headers.IfNoneMatch.Count > 0
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : Image(OldImage, "\"v1\""));
        using var http = new HttpClient(handler);
        using var apiHttp = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("[]") }));
        var api = new AniLibertyClient(apiHttp, NullLogger<AniLibertyClient>.Instance);
        var release = new ReleaseResponse
        {
            Id = 1, Name = new NameBlock { English = "Image Show 2" }, Type = new ReleaseType { Value = "TV" },
            Poster = new ImageBlock { Preview = Url },
            Episodes = [new EpisodeItem { Id = "11111111-1111-1111-1111-111111111111", Ordinal = 1, Hls1080 = "/public/hls/test.m3u8" }]
        };
        var generator = new AniLibertyStrmGenerator(NullLogger<AniLibertyStrmGenerator>.Instance, null!, null!, api) { ImageHttp = http };
        await generator.GenerateTitlesAsync([release], output, "1080", null, CancellationToken.None);
        var posters = Directory.GetFiles(output, "*.jpg", SearchOption.AllDirectories);
        Assert.Equal(2, posters.Length);
        Assert.Equal(1, handler.Calls);
        var mtimes = posters.ToDictionary(path => path, File.GetLastWriteTimeUtc);
        await generator.GenerateTitlesAsync([release], output, "1080", null, CancellationToken.None);
        Assert.Equal(2, handler.Calls);
        foreach (var path in posters)
        {
            Assert.Equal(OldImage, await File.ReadAllBytesAsync(path));
            Assert.Equal(mtimes[path], File.GetLastWriteTimeUtc(path));
        }
    }

    private static HttpResponseMessage Image(byte[] bytes, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        if (etag is not null) response.Headers.ETag = new EntityTagHeaderValue(etag);
        return response;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class UnreadableContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => throw new InvalidOperationException("304 response body must not be read");
        protected override bool TryComputeLength(out long length) { length = 0; return true; }
    }
}

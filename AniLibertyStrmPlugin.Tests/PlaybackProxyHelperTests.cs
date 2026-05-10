using System;
using AniLibertyStrmPlugin.Utils;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class PlaybackProxyHelperTests
{
    [Fact]
    public void BuildProxyUrl_EmbedsEncodedUpstreamUrl()
    {
        var proxyUrl = PlaybackProxyHelper.BuildProxyUrl(
            "http://192.168.100.6:8096/AniLibertyPlayback/hls",
            "https://cache.libria.fun/videos/media/ts/10095/6/1080/demo.m3u8?countryIso=GE");

        Assert.StartsWith("http://192.168.100.6:8096/AniLibertyPlayback/hls?url=", proxyUrl, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("https://cache.libria.fun/videos/media/ts/10095/6/1080/demo.m3u8?countryIso=GE"), proxyUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildProxyEndpoint_CombinesBaseUrlWithRelativeRoute()
    {
        var endpoint = PlaybackProxyHelper.BuildProxyEndpoint(
            "http://172.17.0.4:8096/",
            "/AniLibertyPlayback/hls");

        Assert.Equal("http://172.17.0.4:8096/AniLibertyPlayback/hls", endpoint);
    }

    [Fact]
    public void BuildProxyEndpoint_PreservesHttpAbsoluteRoute()
    {
        var endpoint = PlaybackProxyHelper.BuildProxyEndpoint(
            "http://172.17.0.4:8096",
            "https://jellyfin.example.test/AniLibertyPlayback/hls");

        Assert.Equal("https://jellyfin.example.test/AniLibertyPlayback/hls", endpoint);
    }

    [Fact]
    public void BuildProxyEndpoint_TreatsFileReverseVirtualPathAsRoutePath()
    {
        var endpoint = PlaybackProxyHelper.BuildProxyEndpoint(
            "http://172.17.0.4:8096",
            "file:///AniLibertyPlayback/hls");

        Assert.Equal("http://172.17.0.4:8096/AniLibertyPlayback/hls", endpoint);
    }

    [Fact]
    public void RewritePlaylist_RewritesSegmentAndKeyUrisToProxy()
    {
        const string playlist = """
                                #EXTM3U
                                #EXT-X-VERSION:3
                                #EXT-X-KEY:METHOD=AES-128,URI="keys/key.bin"
                                #EXTINF:4.0,
                                seg-0001.ts
                                """;

        var rewritten = PlaybackProxyHelper.RewritePlaylist(
            playlist,
            new Uri("https://cache.libria.fun/videos/media/ts/10095/6/1080/demo.m3u8"),
            "http://192.168.100.6:8096/AniLibertyPlayback/hls");

        Assert.Contains(
            "URI=\"http://192.168.100.6:8096/AniLibertyPlayback/hls?url=https%3A%2F%2Fcache.libria.fun%2Fvideos%2Fmedia%2Fts%2F10095%2F6%2F1080%2Fkeys%2Fkey.bin\"",
            rewritten,
            StringComparison.Ordinal);
        Assert.Contains(
            "http://192.168.100.6:8096/AniLibertyPlayback/hls?url=https%3A%2F%2Fcache.libria.fun%2Fvideos%2Fmedia%2Fts%2F10095%2F6%2F1080%2Fseg-0001.ts",
            rewritten,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://cache.libria.fun/videos/media/ts/10095/6/1080/demo.m3u8", true)]
    [InlineData("https://api.anilibria.app/public/hls/demo/master.m3u8", true)]
    [InlineData("https://example.com/evil.m3u8", false)]
    public void IsAllowedUpstream_UsesAniLibertyHostAllowList(string url, bool expected)
    {
        Assert.Equal(expected, PlaybackProxyHelper.IsAllowedUpstream(new Uri(url)));
    }
}

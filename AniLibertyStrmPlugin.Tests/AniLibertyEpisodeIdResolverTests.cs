using AniLibertyStrmPlugin.Utils;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class AniLibertyEpisodeIdResolverTests
{
    [Fact]
    public void TryResolveFromStrmPath_ReadsSidecarBeforeNfo()
    {
        var root = NewTempDir();
        try
        {
            var strm = Path.Combine(root, "S01E01.strm");
            var sidecar = AniLibertyEpisodeIdResolver.GetSidecarPath(strm);
            File.WriteAllText(strm, "https://example.test");
            File.WriteAllText(sidecar, "  11111111-1111-1111-1111-111111111111  ");
            File.WriteAllText(Path.ChangeExtension(strm, ".nfo"),
                "<uniqueid type=\"aniliberty_episode_id\">22222222-2222-2222-2222-222222222222</uniqueid>");

            Assert.True(AniLibertyEpisodeIdResolver.TryResolveFromStrmPath(strm, out var episodeId));
            Assert.Equal("11111111-1111-1111-1111-111111111111", episodeId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryResolveFromStrmPath_FallsBackToNfoAndRejectsInvalidInputs()
    {
        var root = NewTempDir();
        try
        {
            Assert.False(AniLibertyEpisodeIdResolver.TryResolveFromStrmPath(null, out _));

            var strm = Path.Combine(root, "S01E02.strm");
            File.WriteAllText(strm, "https://example.test");
            Assert.False(AniLibertyEpisodeIdResolver.TryResolveFromStrmPath(strm, out _));

            File.WriteAllText(Path.ChangeExtension(strm, ".nfo"),
                "<uniqueid type=\"aniliberty_episode_id\">bad</uniqueid>");
            Assert.False(AniLibertyEpisodeIdResolver.TryResolveFromStrmPath(strm, out _));

            File.WriteAllText(Path.ChangeExtension(strm, ".nfo"),
                "<uniqueid type=\"aniliberty_episode_id\" default=\"false\">33333333-3333-3333-3333-333333333333</uniqueid>");
            Assert.True(AniLibertyEpisodeIdResolver.TryResolveFromStrmPath(strm, out var episodeId));
            Assert.Equal("33333333-3333-3333-3333-333333333333", episodeId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "alib-episode-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

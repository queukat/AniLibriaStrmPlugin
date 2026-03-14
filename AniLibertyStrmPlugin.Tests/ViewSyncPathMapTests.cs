using System;
using System.IO;
using AniLibertyStrmPlugin.Configuration;
using AniLibertyStrmPlugin.Utils;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class ViewSyncPathMapTests
{
    [Fact]
    public void Build_CollectsAllCandidatePathsForSameEpisode()
    {
        var root = Path.Combine(Path.GetTempPath(), "alib-pathmap-" + Guid.NewGuid().ToString("N"));
        var allRoot = Path.Combine(root, "all");
        var favRoot = Path.Combine(root, "fav");
        var episodeId = "11111111-1111-1111-1111-111111111111";

        Directory.CreateDirectory(Path.Combine(allRoot, "Show", "Season 01"));
        Directory.CreateDirectory(Path.Combine(favRoot, "Show", "Season 01"));

        var allSidecar = Path.Combine(allRoot, "Show", "Season 01", "S01E01.aniid");
        var favSidecar = Path.Combine(favRoot, "Show", "Season 01", "S01E01.aniid");

        try
        {
            File.WriteAllText(allSidecar, episodeId);
            File.WriteAllText(favSidecar, episodeId);

            var cfg = new PluginConfiguration
            {
                StrmAllPath = allRoot,
                StrmFavoritesPath = favRoot
            };

            var map = ViewSyncPathMap.Build(cfg);

            Assert.True(map.TryGetValue(episodeId, out var paths));
            Assert.NotNull(paths);
            Assert.Equal(2, paths!.Count);
            Assert.Equal(Path.ChangeExtension(allSidecar, ".strm"), paths[0]);
            Assert.Equal(Path.ChangeExtension(favSidecar, ".strm"), paths[1]);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveFirst_ReturnsFirstResolvableCandidate()
    {
        var preferred = @"D:\fav\Show\Season 01\S01E01.strm";

        var resolved = ViewSyncPathMap.ResolveFirst(
            [@"D:\all\Show\Season 01\S01E01.strm", preferred],
            path => path == preferred ? path : null);

        Assert.Equal(preferred, resolved);
    }
}

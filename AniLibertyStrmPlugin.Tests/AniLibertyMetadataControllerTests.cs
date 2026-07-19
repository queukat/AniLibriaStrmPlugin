using System;
using System.IO;
using System.Text.Json;
using AniLibertyStrmPlugin.Models;
using AniLibertyStrmPlugin.Utils;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class AniLibertyMetadataControllerTests
{
    [Fact]
    public void TryResolvePopularityForPath_UsesNearestSidecarFromEpisodePath()
    {
        using var output = TempOutput.Create();
        var showDir = Path.Combine(output.Path, "demo show");
        var seasonDir = Path.Combine(showDir, "Season 01");
        Directory.CreateDirectory(seasonDir);
        WriteSidecar(Path.Combine(output.Path, AniLibertyPopularityDocument.FileName), 1, 10);
        WriteSidecar(Path.Combine(showDir, AniLibertyPopularityDocument.FileName), 2, 20);

        var response = AniLibertyMetadataController.TryResolvePopularityForPath(Path.Combine(seasonDir, "S01E01.strm"));

        Assert.NotNull(response);
        Assert.Equal(2, response.ReleaseId);
        Assert.Equal(20, response.Count);
        Assert.Equal("AniLiberty", response.Source);
        Assert.Equal("added_in_users_favorites", response.Metric);
    }

    [Fact]
    public void TryResolvePopularityForPath_ReturnsNullWhenSidecarIsAbsent()
    {
        using var output = TempOutput.Create();
        var episodePath = Path.Combine(output.Path, "demo show", "Season 01", "S01E01.strm");

        Assert.Null(AniLibertyMetadataController.FindPopularitySidecarPath(episodePath));
        Assert.Null(AniLibertyMetadataController.TryResolvePopularityForPath(episodePath));
    }

    [Fact]
    public void ReadPopularityDocument_ReturnsNullForUnmanagedSidecar()
    {
        using var output = TempOutput.Create();
        var sidecarPath = Path.Combine(output.Path, AniLibertyPopularityDocument.FileName);
        File.WriteAllText(sidecarPath, "{\"source\":\"AniLiberty\",\"metric\":\"added_in_users_favorites\",\"favorites\":123}");

        Assert.Null(AniLibertyMetadataController.ReadPopularityDocument(sidecarPath));
        Assert.Null(AniLibertyMetadataController.TryResolvePopularityForPath(Path.Combine(output.Path, "movie.strm")));
    }

    private static void WriteSidecar(string path, int releaseId, int favorites)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var document = new AniLibertyPopularityDocument
        {
            GeneratedBy = ManagedLibraryManifest.GeneratedXmlMarker,
            Source = AniLibertyPopularityDocument.SourceName,
            Metric = AniLibertyPopularityDocument.FavoritesMetric,
            ReleaseId = releaseId,
            Alias = $"release-{releaseId}",
            Favorites = favorites,
            Collections = new AniLibertyCollectionCounts
            {
                Planned = favorites + 1,
                Watched = favorites + 2
            }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document));
    }

    private sealed class TempOutput : IDisposable
    {
        private TempOutput(string path) => Path = path;

        public string Path { get; }

        public static TempOutput Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "alib-metadata-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempOutput(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}

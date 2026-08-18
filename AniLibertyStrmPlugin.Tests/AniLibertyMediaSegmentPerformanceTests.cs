using AniLibertyStrmPlugin.Media;
using AniLibertyStrmPlugin.Utils;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public sealed class AniLibertyMediaSegmentPerformanceTests
{
    [Fact]
    public async Task Index_LoadsStateOnceForConcurrentLookups_AndReloadsAfterInvalidation()
    {
        var root = Path.Combine(Path.GetTempPath(), "alib-segment-index-" + Guid.NewGuid().ToString("N"));
        var firstPath = Path.Combine(root, "Show", "Season 01", "S01E01.strm");
        var secondPath = Path.Combine(root, "Show", "Season 01", "S01E02.strm");
        Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
        await File.WriteAllTextAsync(firstPath, "https://example.test/one.m3u8");
        await File.WriteAllTextAsync(secondPath, "https://example.test/two.m3u8");

        try
        {
            await SaveStateAsync(root, firstPath, secondPath, introStartSeconds: 10);
            var index = new AniLibertyMediaSegmentIndex();

            var lookups = Enumerable.Range(0, 32)
                .Select(i => index.TryGetEntryForPathAsync(
                    i % 2 == 0 ? firstPath : secondPath,
                    CancellationToken.None));
            var entries = await Task.WhenAll(lookups);

            Assert.All(entries, Assert.NotNull);
            Assert.Equal(1, index.LoadCount);

            var firstSnapshot = await index.GetSnapshotForPathAsync(firstPath, CancellationToken.None);
            var secondSnapshot = await index.GetSnapshotForPathAsync(secondPath, CancellationToken.None);
            Assert.Same(firstSnapshot, secondSnapshot);
            Assert.Equal(2, firstSnapshot!.Count);

            await SaveStateAsync(root, firstPath, secondPath, introStartSeconds: 20);
            index.InvalidateRoot(root);

            var updated = await index.TryGetEntryForPathAsync(firstPath, CancellationToken.None);
            Assert.NotNull(updated);
            Assert.Equal(TimeSpan.FromSeconds(20).Ticks, updated!.Segments[0].StartTicks);
            Assert.Equal(2, index.LoadCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WarmupQuery_UsesBoundedStablePagination()
    {
        var query = AniLibertyMediaSegmentWarmupHostedService.CreateMissingItemsQuery(400);

        Assert.Equal(400, query.StartIndex);
        Assert.Equal(200, query.Limit);
        Assert.False(query.EnableTotalRecordCount);
        Assert.True(query.Recursive);
        Assert.Equal(new[] { BaseItemKind.Episode }, query.IncludeItemTypes);
        Assert.Collection(
            query.OrderBy,
            order => Assert.Equal((ItemSortBy.SortName, SortOrder.Ascending), order),
            order => Assert.Equal((ItemSortBy.DateCreated, SortOrder.Ascending), order));
    }

    private static async Task SaveStateAsync(
        string root,
        string firstPath,
        string secondPath,
        int introStartSeconds)
    {
        var state = new AniLibertyMediaSegmentState(root);
        state.Track(firstPath, 1, "episode-1", [Segment(introStartSeconds)]);
        state.Track(secondPath, 1, "episode-2", [Segment(introStartSeconds + 1)]);
        await state.SaveAsync(CancellationToken.None);
    }

    private static AniLibertyMediaSegment Segment(int startSeconds)
        => new()
        {
            Type = "Intro",
            StartTicks = TimeSpan.FromSeconds(startSeconds).Ticks,
            EndTicks = TimeSpan.FromSeconds(startSeconds + 10).Ticks
        };
}

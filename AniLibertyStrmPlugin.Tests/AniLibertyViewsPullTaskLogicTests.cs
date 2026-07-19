using AniLibertyStrmPlugin.Models;
using AniLibertyStrmPlugin.Tasks;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class AniLibertyViewsPullTaskLogicTests
{
    [Fact]
    public void ApplyTimecode_MarksItemPlayed()
    {
        var userData = new UserItemData
        {
            Key = "item",
            Played = false,
            PlayCount = 0,
            PlaybackPositionTicks = TimeSpan.FromSeconds(10).Ticks
        };

        var changed = AniLibertyViewsPullTask.ApplyTimecode(
            new ViewTimecodeEntry { IsWatched = true, Time = 120, ReleaseEpisodeId = Guid.NewGuid().ToString() },
            userData);

        Assert.True(changed);
        Assert.True(userData.Played);
        Assert.Equal(1, userData.PlayCount);
        Assert.Equal(0, userData.PlaybackPositionTicks);
        Assert.NotNull(userData.LastPlayedDate);
    }

    [Fact]
    public void ApplyTimecode_LeavesAlreadyPlayedItemUnchanged()
    {
        var lastPlayed = DateTime.UtcNow.AddDays(-1);
        var userData = new UserItemData
        {
            Key = "item",
            Played = true,
            PlayCount = 3,
            PlaybackPositionTicks = 0,
            LastPlayedDate = lastPlayed
        };

        var changed = AniLibertyViewsPullTask.ApplyTimecode(
            new ViewTimecodeEntry { IsWatched = true, Time = 120, ReleaseEpisodeId = Guid.NewGuid().ToString() },
            userData);

        Assert.False(changed);
        Assert.True(userData.Played);
        Assert.Equal(3, userData.PlayCount);
        Assert.Equal(0, userData.PlaybackPositionTicks);
        Assert.NotEqual(lastPlayed, userData.LastPlayedDate);
    }

    [Fact]
    public void ApplyTimecode_DoesNotDowngradePlayedItem()
    {
        var userData = new UserItemData
        {
            Key = "item",
            Played = true,
            PlayCount = 1,
            PlaybackPositionTicks = 0
        };

        var changed = AniLibertyViewsPullTask.ApplyTimecode(
            new ViewTimecodeEntry { IsWatched = false, Time = 60, ReleaseEpisodeId = Guid.NewGuid().ToString() },
            userData);

        Assert.False(changed);
        Assert.True(userData.Played);
        Assert.Equal(0, userData.PlaybackPositionTicks);
    }

    [Fact]
    public void ApplyTimecode_AdvancesOnlyForwardProgress()
    {
        var userData = new UserItemData
        {
            Key = "item",
            PlaybackPositionTicks = TimeSpan.FromSeconds(30).Ticks
        };

        var unchanged = AniLibertyViewsPullTask.ApplyTimecode(
            new ViewTimecodeEntry { IsWatched = false, Time = 20, ReleaseEpisodeId = Guid.NewGuid().ToString() },
            userData);

        Assert.False(unchanged);
        Assert.Equal(TimeSpan.FromSeconds(30).Ticks, userData.PlaybackPositionTicks);

        var changed = AniLibertyViewsPullTask.ApplyTimecode(
            new ViewTimecodeEntry { IsWatched = false, Time = 45.4, ReleaseEpisodeId = Guid.NewGuid().ToString() },
            userData);

        Assert.True(changed);
        Assert.Equal(TimeSpan.FromSeconds(45.4).Ticks, userData.PlaybackPositionTicks);
    }

    [Fact]
    public void ApplyTimecode_ClampsNegativeProgressToZero()
    {
        var userData = new UserItemData
        {
            Key = "item",
            PlaybackPositionTicks = 0
        };

        var changed = AniLibertyViewsPullTask.ApplyTimecode(
            new ViewTimecodeEntry { IsWatched = false, Time = -5, ReleaseEpisodeId = Guid.NewGuid().ToString() },
            userData);

        Assert.False(changed);
        Assert.Equal(0, userData.PlaybackPositionTicks);
    }
}

using AniLibertyStrmPlugin.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class ScheduledTaskMetadataTests
{
    [Fact]
    public void AniLibertyScheduledTasks_AreVisibleInJellyfinScheduler()
    {
        var all = new AniLibertyAllTask(
            client: null!,
            gen: null!,
            log: NullLogger<AniLibertyAllTask>.Instance);
        var favorites = new AniLibertyFavoritesTask(
            client: null!,
            gen: null!,
            log: NullLogger<AniLibertyFavoritesTask>.Instance);
        var viewsPull = new AniLibertyViewsPullTask(
            client: null!,
            library: null!,
            userDataManager: null!,
            userManager: null!,
            log: NullLogger<AniLibertyViewsPullTask>.Instance);

        Assert.False(all.IsHidden);
        Assert.False(favorites.IsHidden);
        Assert.False(viewsPull.IsHidden);
    }
}

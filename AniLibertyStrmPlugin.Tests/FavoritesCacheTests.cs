using AniLibertyStrmPlugin.Utils;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class FavoritesCacheTests
{
    private static readonly int[] InitialIds = [1, 2, 2, 3];
    private static readonly int[] ReplacementIds = [4];

    [Fact]
    public void Update_ReplacesCachedIds()
    {
        FavoritesCache.Update(InitialIds);

        Assert.True(FavoritesCache.Contains(1));
        Assert.True(FavoritesCache.Contains(2));
        Assert.True(FavoritesCache.Contains(3));
        Assert.False(FavoritesCache.Contains(4));

        FavoritesCache.Update(ReplacementIds);

        Assert.False(FavoritesCache.Contains(1));
        Assert.True(FavoritesCache.Contains(4));
    }
}

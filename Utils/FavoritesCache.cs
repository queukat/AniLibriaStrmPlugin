// ===== ADDED FILE: Utils/FavoritesCache.cs =====
//  in-memory-   ID,   
//      .

namespace AniLibertyStrmPlugin.Utils;

internal static class FavoritesCache
{
    private static readonly HashSet<int> _ids = new();

    /// <summary>   .</summary>
    public static void Update(IEnumerable<int> ids)
    {
        lock (_ids)
        {
            _ids.Clear();
            foreach (var id in ids)
                _ids.Add(id);
        }
    }

    /// <summary>  ID  .</summary>
    public static bool Contains(int id)
    {
        lock (_ids)
        {
            return _ids.Contains(id);
        }
    }
}
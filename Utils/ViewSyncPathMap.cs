namespace AniLibertyStrmPlugin.Utils;

internal static class ViewSyncPathMap
{
    public static Dictionary<string, List<string>> Build(Configuration.PluginConfiguration cfg)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetExistingRoots(cfg))
        {
            foreach (var sidecar in EnumerateSidecars(root))
                AddSidecar(result, sidecar);
        }

        return result;
    }

    private static IEnumerable<string> GetExistingRoots(Configuration.PluginConfiguration cfg)
    {
        return new[] { cfg.StrmAllPath, cfg.StrmFavoritesPath }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists);
    }

    private static IEnumerable<string> EnumerateSidecars(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.aniid", SearchOption.AllDirectories);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void AddSidecar(Dictionary<string, List<string>> result, string sidecar)
    {
        try
        {
            var episodeId = File.ReadAllText(sidecar).Trim();
            if (!Guid.TryParse(episodeId, out _))
                return;

            var strmPath = Path.ChangeExtension(sidecar, ".strm");
            if (!result.TryGetValue(episodeId, out var paths))
            {
                paths = new List<string>();
                result[episodeId] = paths;
            }

            if (!paths.Contains(strmPath, StringComparer.OrdinalIgnoreCase))
                paths.Add(strmPath);
        }
        catch
        {
            // ignore one broken sidecar
        }
    }

    public static T? ResolveFirst<T>(IEnumerable<string> candidatePaths, Func<string, T?> resolver)
        where T : class
    {
        foreach (var path in candidatePaths)
        {
            if (resolver(path) is { } resolved)
                return resolved;
        }

        return null;
    }
}

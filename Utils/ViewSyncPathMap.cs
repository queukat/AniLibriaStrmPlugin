namespace AniLibertyStrmPlugin.Utils;

internal static class ViewSyncPathMap
{
    public static Dictionary<string, List<string>> Build(Configuration.PluginConfiguration cfg)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var roots = new[] { cfg.StrmAllPath, cfg.StrmFavoritesPath }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.aniid", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (var sidecar in files)
            {
                try
                {
                    var txt = (File.ReadAllText(sidecar) ?? string.Empty).Trim();
                    if (!Guid.TryParse(txt, out _))
                        continue;

                    var strmPath = Path.ChangeExtension(sidecar, ".strm");
                    if (!result.TryGetValue(txt, out var paths))
                    {
                        paths = new List<string>();
                        result[txt] = paths;
                    }

                    if (!paths.Contains(strmPath, StringComparer.OrdinalIgnoreCase))
                        paths.Add(strmPath);
                }
                catch
                {
                    // ignore one broken sidecar
                }
            }
        }

        return result;
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

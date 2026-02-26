using System.Text.RegularExpressions;

namespace AniLibertyStrmPlugin.Utils;

internal static class AniLibertyEpisodeIdResolver
{
    private const string SidecarExt = ".aniid";
    private static readonly Regex UniqueIdRx = new(
        @"<uniqueid[^>]*aniliberty_episode_id[^>]*>\s*(?<id>[^<\s]+)\s*</uniqueid>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string GetSidecarPath(string strmPath)
        => Path.ChangeExtension(strmPath, SidecarExt);

    public static bool TryResolveFromStrmPath(string? strmPath, out string episodeId)
    {
        episodeId = string.Empty;

        if (string.IsNullOrWhiteSpace(strmPath))
            return false;

        var sidecar = GetSidecarPath(strmPath);
        if (File.Exists(sidecar))
        {
            var txt = (File.ReadAllText(sidecar) ?? string.Empty).Trim();
            if (Guid.TryParse(txt, out _))
            {
                episodeId = txt;
                return true;
            }
        }

        var nfoPath = Path.ChangeExtension(strmPath, ".nfo");
        if (!File.Exists(nfoPath))
            return false;

        var nfo = File.ReadAllText(nfoPath);
        var m = UniqueIdRx.Match(nfo);
        if (!m.Success)
            return false;

        var val = m.Groups["id"].Value.Trim();
        if (!Guid.TryParse(val, out _))
            return false;

        episodeId = val;
        return true;
    }
}

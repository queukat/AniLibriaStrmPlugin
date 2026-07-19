namespace AniLibertyStrmPlugin.Web;

internal static class AniLibertyPopularityWebUiInjector
{
    private const string StartMarker = "<!-- AniLibertyPopularityBadge:start -->";
    private const string EndMarker = "<!-- AniLibertyPopularityBadge:end -->";

    public static string TransformIndexHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        var cleaned = RemoveInjectedBlock(html);
        var block = BuildInjectionBlock();
        var bodyIndex = cleaned.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyIndex >= 0
            ? cleaned.Insert(bodyIndex, block)
            : cleaned + block;
    }

    internal static string RemoveInjectedBlock(string html)
    {
        var current = html;
        while (true)
        {
            var start = current.IndexOf(StartMarker, StringComparison.Ordinal);
            if (start < 0)
                return current;

            var end = current.IndexOf(EndMarker, start, StringComparison.Ordinal);
            if (end < 0)
                return current.Remove(start);

            var removeStart = ExtendRemovalStartToOwnLine(current, start);
            var removeEnd = ExtendRemovalEndToOwnLine(current, end + EndMarker.Length);
            current = current.Remove(removeStart, removeEnd - removeStart);
        }
    }

    internal static bool HasInjectedBlock(string html)
        => !string.IsNullOrEmpty(html)
           && html.Contains(StartMarker, StringComparison.Ordinal)
           && html.Contains(EndMarker, StringComparison.Ordinal);

    private static int ExtendRemovalStartToOwnLine(string html, int start)
    {
        var cursor = start;
        while (cursor > 0 && char.IsWhiteSpace(html[cursor - 1]))
            cursor--;

        return cursor;
    }

    private static int ExtendRemovalEndToOwnLine(string html, int end)
    {
        var cursor = end;
        while (cursor < html.Length && char.IsWhiteSpace(html[cursor]))
            cursor++;

        return cursor;
    }

    private static string BuildInjectionBlock()
        => """

           <!-- AniLibertyPopularityBadge:start -->
           <script defer="defer" id="aniliberty-popularity-badge-script" src="/AniLibertyMetadata/Assets/aniliberty-popularity.js?v=2.0.0.11.2"></script>
           <!-- AniLibertyPopularityBadge:end -->
           """;
}

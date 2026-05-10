using System.Text.RegularExpressions;

namespace AniLibertyStrmPlugin.Utils;

public static partial class PlaybackProxyHelper
{
    public const string ProxyRoute = "/AniLibertyPlayback/hls";
    private static readonly string[] AllowedHostSuffixes =
    {
        "anilibria.app",
        "libria.fun"
    };

    [GeneratedRegex("(?<prefix>\\bURI=\")(?<value>[^\"]+)(?<suffix>\")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UriAttributeRegex();

    public static bool IsAllowedUpstream(Uri uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        var host = uri.Host.Trim().TrimEnd('.').ToLowerInvariant();
        return AllowedHostSuffixes.Any(suffix =>
            host == suffix || host.EndsWith("." + suffix, StringComparison.Ordinal));
    }

    public static string BuildProxyUrl(string proxyEndpointUrl, string upstreamUrl)
    {
        if (string.IsNullOrWhiteSpace(proxyEndpointUrl))
            return upstreamUrl;

        return $"{proxyEndpointUrl.TrimEnd('/')}?url={Uri.EscapeDataString(upstreamUrl)}";
    }

    public static string BuildProxyEndpoint(string baseUrl, string route)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(route))
            route = ProxyRoute;

        if (Uri.TryCreate(route, UriKind.Absolute, out var absoluteRoute))
        {
            if (IsHttpEndpoint(absoluteRoute))
                return absoluteRoute.ToString();

            route = absoluteRoute.AbsolutePath;
        }

        if (string.IsNullOrWhiteSpace(route))
            route = ProxyRoute;

        if (!route.StartsWith("/", StringComparison.Ordinal))
            route = "/" + route;

        return baseUrl.TrimEnd('/') + route;
    }

    public static bool IsHlsPlaylist(Uri requestUri, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var mediaType = contentType.Split(';', 2)[0].Trim();
            if (mediaType.Equals("application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("application/x-mpegURL", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("audio/mpegurl", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return requestUri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    public static string RewritePlaylist(string playlistText, Uri playlistUri, string proxyEndpointUrl)
    {
        if (string.IsNullOrWhiteSpace(playlistText))
            return playlistText;

        var normalizedProxyEndpoint = proxyEndpointUrl.TrimEnd('/');
        var newline = playlistText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = playlistText.Replace("\r\n", "\n").Split('\n');
        var rewritten = lines.Select(line => RewritePlaylistLine(line, playlistUri, normalizedProxyEndpoint));
        return string.Join(newline, rewritten);
    }

    private static string RewritePlaylistLine(string line, Uri playlistUri, string proxyEndpointUrl)
    {
        if (string.IsNullOrWhiteSpace(line))
            return line;

        var trimmed = line.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
            return RewriteUriAttributes(line, playlistUri, proxyEndpointUrl);

        var resolved = ResolveUpstreamUri(trimmed, playlistUri);
        return resolved is null
            ? line
            : BuildProxyUrl(proxyEndpointUrl, resolved.ToString());
    }

    private static string RewriteUriAttributes(string line, Uri playlistUri, string proxyEndpointUrl)
    {
        return UriAttributeRegex().Replace(line, match =>
        {
            var rawValue = match.Groups["value"].Value;
            var resolved = ResolveUpstreamUri(rawValue, playlistUri);
            if (resolved is null)
                return match.Value;

            var proxied = BuildProxyUrl(proxyEndpointUrl, resolved.ToString());
            return $"{match.Groups["prefix"].Value}{proxied}{match.Groups["suffix"].Value}";
        });
    }

    private static Uri? ResolveUpstreamUri(string rawValue, Uri playlistUri)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        if (!Uri.TryCreate(playlistUri, rawValue.Trim(), out var absoluteUri))
            return null;

        return IsAllowedUpstream(absoluteUri) ? absoluteUri : null;
    }

    private static bool IsHttpEndpoint(Uri uri)
    {
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}

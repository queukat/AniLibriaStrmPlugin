using System.Reflection;

namespace AniLibertyStrmPlugin;

internal static class PluginIdentity
{
    public const string DisplayName = "AniLiberty STRM Plugin";
    public const string ProductToken = "AniLibertyStrmPlugin";
    private const string RepositoryHost = "github.com";
    private const string RepositoryPath = "queukat/AniLibriaStrmPlugin";

    public static string RepositoryUrl => new UriBuilder(Uri.UriSchemeHttps, RepositoryHost)
    {
        Path = RepositoryPath
    }.Uri.ToString().TrimEnd('/');

    public static string Version =>
        typeof(PluginIdentity).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+', 2)[0]
            ?? typeof(PluginIdentity).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

    public static string UserAgent => $"{ProductToken}/{Version} (Jellyfin; +{RepositoryUrl})";
}

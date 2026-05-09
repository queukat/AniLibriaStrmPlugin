using System.Reflection;

namespace AniLibertyStrmPlugin;

internal static class PluginIdentity
{
    public const string DisplayName = "AniLiberty STRM Plugin";
    public const string ProductToken = "AniLibertyStrmPlugin";
    public const string RepositoryUrl = "https://github.com/queukat/AniLibriaStrmPlugin";

    public static string Version =>
        typeof(PluginIdentity).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+', 2)[0]
            ?? typeof(PluginIdentity).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

    public static string UserAgent => $"{ProductToken}/{Version} (Jellyfin; +{RepositoryUrl})";
}

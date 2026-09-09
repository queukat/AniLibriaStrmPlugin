using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public partial class BuildConfigurationTests
{
    [GeneratedRegex(@"^AniLibertyStrmPlugin/\d+\.\d+\.\d+\.\d+ \(Jellyfin; \+https://github\.com/queukat/AniLibriaStrmPlugin\)$")]
    private static partial Regex UserAgentRegex();

    [GeneratedRegex("PluginIdentity\\.UserAgent")]
    private static partial Regex PluginIdentityUserAgentRegex();

    [Fact]
    public void JellyfinPackageReferences_ArePinnedToMinimumSupportedAbi()
    {
        var root = FindRepoRoot();
        var projects = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories);
        var jellyfinPackages = projects
            .SelectMany(path => XDocument.Load(path)
                .Descendants("PackageReference")
                .Select(x => new
                {
                    Include = x.Attribute("Include")?.Value,
                    Version = x.Attribute("Version")?.Value
                }))
            .Where(x => x.Include?.StartsWith("Jellyfin.", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(jellyfinPackages);
        Assert.All(jellyfinPackages, package =>
        {
            Assert.Equal("10.11.0", package.Version);
            Assert.DoesNotContain("*", package.Version, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void JellyfinLockFiles_ResolveToMinimumSupportedAbi()
    {
        var root = FindRepoRoot();
        var lockFiles = Directory.GetFiles(root, "packages.lock.json", SearchOption.AllDirectories);
        var jellyfinPackages = lockFiles
            .SelectMany(ReadLockedJellyfinPackages)
            .ToList();

        Assert.NotEmpty(jellyfinPackages);
        Assert.All(jellyfinPackages, package =>
            Assert.Equal("10.11.0", package.Resolved));
    }

    [Fact]
    public void PluginIdentity_UserAgent_IsCanonicalAniLibertyName()
    {
        Assert.Matches(
            UserAgentRegex(),
            PluginIdentity.UserAgent);
    }

    [Fact]
    public void NamedHttpClients_UseSharedPluginIdentityUserAgent()
    {
        var services = new ServiceCollection();
        ((IPluginServiceRegistrator)new AniLibertyServiceRegistrator()).RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(PluginIdentity.UserAgent, FormatUserAgent(factory.CreateClient("AniLiberty")));
        Assert.Equal(PluginIdentity.UserAgent, FormatUserAgent(factory.CreateClient("AniLibertyMediaProxy")));
    }

    [Fact]
    public void StaticHttpClients_UseSharedPluginIdentityUserAgent()
    {
        var root = FindRepoRoot();
        var serviceRegistration = File.ReadAllText(Path.Combine(root, "AniLibertyServiceRegistrator.cs"));
        var generator = File.ReadAllText(Path.Combine(root, "AniLibertyStrmGenerator.cs"));
        var allSource = serviceRegistration + Environment.NewLine + generator;

        Assert.DoesNotContain("Jellyfin-AniLibertyStrm", allSource, StringComparison.Ordinal);
        Assert.True(PluginIdentityUserAgentRegex().Matches(allSource).Count >= 3);
    }

    [Fact]
    public void PublicReadme_ExplainsProductAndCompatibilityWithoutInternalJargon()
    {
        var root = FindRepoRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        Assert.Contains("Browse and watch AniLiberty anime in Jellyfin", readme, StringComparison.Ordinal);
        Assert.Contains("does not download episodes for offline viewing", readme, StringComparison.Ordinal);
        Assert.Contains("tree/jellyfin-12", readme, StringComparison.Ordinal);
        Assert.Contains("tree/aniLiberty-v2", readme, StringComparison.Ordinal);
        Assert.Contains("not published releases", readme, StringComparison.Ordinal);
        foreach (var internalDetail in new[] { "Signal Acquisition Layer", "Operational Command Center", "C:\\Users\\", "local-install-20260908", "docs/audits/" })
            Assert.DoesNotContain(internalDetail, readme, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicReadme_DocumentsInstallAndProxyPathContracts()
    {
        var root = FindRepoRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var configPage = File.ReadAllText(Path.Combine(root, "Configuration", "configPage.html"));

        Assert.Contains("./scripts/Build-Plugin.ps1 -Version", readme, StringComparison.Ordinal);
        Assert.Contains("build/packages", readme, StringComparison.Ordinal);
        Assert.Contains("/config/plugins", readme, StringComparison.Ordinal);
        Assert.Contains("only one copy remains installed", readme, StringComparison.Ordinal);
        Assert.Contains("Keep the plugin configuration private", readme, StringComparison.Ordinal);
        Assert.Contains("Do not use `localhost`", readme, StringComparison.Ordinal);
        Assert.Contains("rerun each enabled generation task", readme, StringComparison.Ordinal);
        Assert.Contains("do not add /AniLibertyPlayback/hls", configPage, StringComparison.Ordinal);
        Assert.Contains("Do not use localhost for phones, TVs, or", configPage, StringComparison.Ordinal);

        Assert.DoesNotContain("<jellyfin>/plugins/aniliberty-strm-plugin/", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicReadme_DocumentsOperationalCapabilities()
    {
        var root = FindRepoRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var configPage = File.ReadAllText(Path.Combine(root, "Configuration", "configPage.html"));

        Assert.Contains("separate, non-overlapping output folders", readme, StringComparison.Ordinal);
        Assert.Contains("Both tasks require their own generation option to be enabled", readme, StringComparison.Ordinal);
        Assert.Contains("without deleting existing files", readme, StringComparison.Ordinal);
        Assert.Contains("preserving untracked, user-supplied metadata and artwork", readme, StringComparison.Ordinal);
        Assert.Contains("Incomplete API results do not authorize stale-file cleanup", readme, StringComparison.Ordinal);
        Assert.Contains("Sync playback progress to AniLiberty", readme, StringComparison.Ordinal);
        Assert.Contains("Sync AniLiberty watch progress to Jellyfin", readme, StringComparison.Ordinal);
        Assert.Contains("a server with one user", readme, StringComparison.Ordinal);
        Assert.Contains("Do not reuse the favorites path", configPage, StringComparison.Ordinal);
        Assert.Contains("When OFF, the task exits without updating Favorites STRM Path", configPage, StringComparison.Ordinal);
        Assert.Contains("When OFF, the task exits without updating All Titles STRM Path", configPage, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginPage_ExposesRawSupportBundleWithoutTokenDisclosure()
    {
        var root = FindRepoRoot();
        var configPage = File.ReadAllText(Path.Combine(root, "Configuration", "configPage.html"));

        Assert.Contains("btnShowRawLogs", configPage, StringComparison.Ordinal);
        Assert.Contains("btnCopySupportBundle", configPage, StringComparison.Ordinal);
        Assert.Contains("btnToggleSupportTrace", configPage, StringComparison.Ordinal);
        Assert.Contains("LastRawTaskLog", configPage, StringComparison.Ordinal);
        Assert.Contains("Support bundle copies sanitized settings plus captured trace", configPage, StringComparison.Ordinal);
        Assert.Contains("Huge support trace can become very large and noisy", configPage, StringComparison.Ordinal);
        Assert.Contains("enable Huge trace and rerun the failing task", configPage, StringComparison.Ordinal);
        Assert.Contains("tokens are never included", configPage, StringComparison.Ordinal);
        Assert.Contains("AniLibertyToken: \" + (cfg.AniLibertyToken ? \"present (\" + cfg.AniLibertyToken.length + \" chars)\" : \"empty\")", configPage, StringComparison.Ordinal);
        Assert.DoesNotContain("AniLibertyToken: \" + cfg.AniLibertyToken", configPage, StringComparison.Ordinal);
    }

    [Fact]
    public void PopularityBadgeWebInjection_UsesOnlyPublicMetadataEndpoints()
    {
        var root = FindRepoRoot();
        var webFiles = Directory.GetFiles(Path.Combine(root, "Web"), "*.cs", SearchOption.TopDirectoryOnly);
        var webSource = string.Join(Environment.NewLine, webFiles.Select(File.ReadAllText));

        Assert.Contains("AniLibertyMetadata/Popularity", webSource, StringComparison.Ordinal);
        Assert.Contains("AniLibertyMetadata/Assets/aniliberty-rating.png", webSource, StringComparison.Ordinal);
        Assert.Contains("AniLibertyMetadata/Assets/aniliberty-popularity.js", webSource, StringComparison.Ordinal);
        Assert.Contains("url.hash", webSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AniLibertyToken", webSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CommunityRating =", webSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CriticRating =", webSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportTrace_DetailedProgressDoesNotPolluteCompactInfoLog()
    {
        var root = FindRepoRoot();
        var client = File.ReadAllText(Path.Combine(root, "AniLibertyClient.cs"));
        var generator = File.ReadAllText(Path.Combine(root, "AniLibertyStrmGenerator.cs"));
        var manifest = File.ReadAllText(Path.Combine(root, "Utils", "ManagedLibraryManifest.cs"));

        Assert.Contains("log.Debug(\"ALL page {0}/{1}: requesting catalog releases", client, StringComparison.Ordinal);
        Assert.Contains("log.Debug(\"ALL page {0}: response received", client, StringComparison.Ordinal);
        Assert.Contains("log.Debug(\"ALL page {0}: parsed {1} titles", client, StringComparison.Ordinal);
        Assert.DoesNotContain("log.Info(\"ALL page {0}/{1}: requesting catalog releases", client, StringComparison.Ordinal);
        Assert.Contains("else if (supportTrace)", generator, StringComparison.Ordinal);
        Assert.Contains("log.Debug(\"({0}/{1}) \\\"{2}\\\"", generator, StringComparison.Ordinal);
        Assert.Contains("log.Debug(\"[MIRROR] Dry-run stale: {0}\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("log.Info(\"[MIRROR] Dry-run stale: {0}\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportTrace_CapturesFullExceptionDetailsWithoutPollutingCompactLog()
    {
        var root = FindRepoRoot();
        var plugin = File.ReadAllText(Path.Combine(root, "Plugin.cs"));
        var logHelper = File.ReadAllText(Path.Combine(root, "Utils", "LogHelper.cs"));

        Assert.Contains("AppendRawSupportLog", plugin, StringComparison.Ordinal);
        Assert.Contains("AppendRawExceptionDetailSafe", logHelper, StringComparison.Ordinal);
        Assert.Contains("Exception detail for", logHelper, StringComparison.Ordinal);
        Assert.Contains("ex.ToString()", logHelper, StringComparison.Ordinal);
        Assert.Contains("plugin.AppendRawSupportLog(msg)", logHelper, StringComparison.Ordinal);
        Assert.Contains("AppendTaskLogSafe(LogLevel.Error, \"ERROR\", msg, ex)", logHelper, StringComparison.Ordinal);
    }

    [Fact]
    public void ScheduledTasks_RunWritableOutputPreflightBeforeFetchingApi()
    {
        var root = FindRepoRoot();
        var allTask = File.ReadAllText(Path.Combine(root, "Tasks", "AniLibertyAllTask.cs"));
        var favoritesTask = File.ReadAllText(Path.Combine(root, "Tasks", "AniLibertyFavoritesTask.cs"));
        var preflight = File.ReadAllText(Path.Combine(root, "Utils", "OutputRootPreflight.cs"));

        Assert.Contains("OutputRootPreflight.EnsureWritableAsync", allTask, StringComparison.Ordinal);
        Assert.Contains("OutputRootPreflight.EnsureWritableAsync", favoritesTask, StringComparison.Ordinal);
        Assert.True(
            allTask.IndexOf("OutputRootPreflight.EnsureWritableAsync", StringComparison.Ordinal)
            < allTask.IndexOf("FetchAllTitlesAsync", StringComparison.Ordinal));
        Assert.True(
            favoritesTask.IndexOf("OutputRootPreflight.EnsureWritableAsync", StringComparison.Ordinal)
            < favoritesTask.IndexOf("FetchFavoritesAsync", StringComparison.Ordinal));
        Assert.Contains("Check Docker volume mapping, host directory ownership, and PUID/PGID permissions", preflight, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_UsesCuratedReleaseNotesWhenPresent()
    {
        var root = FindRepoRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        var notes = File.ReadAllText(Path.Combine(root, ".github", "release-notes.md"));
        var buildManifest = File.ReadAllText(Path.Combine(root, "build.yaml"));

        Assert.Contains(".github/release-notes.md", workflow, StringComparison.Ordinal);
        Assert.Contains(".release_notes_body.md", workflow, StringComparison.Ordinal);
        Assert.Contains("cat .release_notes_body.md", workflow, StringComparison.Ordinal);
        Assert.Contains(".manifest_changelog.txt", workflow, StringComparison.Ordinal);
        Assert.Contains("MANIFEST_NOTES=", workflow, StringComparison.Ordinal);
        Assert.Contains(".changelog = $notes", workflow, StringComparison.Ordinal);
        Assert.Contains("AniLiberty STRM", notes, StringComparison.Ordinal);
        Assert.Contains("### ", notes, StringComparison.Ordinal);
        Assert.Contains("- ", notes, StringComparison.Ordinal);
        Assert.Contains("AniLiberty STRM", buildManifest, StringComparison.Ordinal);
        Assert.DoesNotContain("release: fix locked Jellyfin restore", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("release: harden AniLiberty STRM system", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("release: fix locked Jellyfin restore", buildManifest, StringComparison.Ordinal);
        Assert.DoesNotContain("release: harden AniLiberty STRM system", buildManifest, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AniLibertyStrmPlugin.csproj")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static IEnumerable<(string LockFile, string Name, string? Resolved)> ReadLockedJellyfinPackages(string lockFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(lockFile));
        if (!document.RootElement.TryGetProperty("dependencies", out var targetFrameworks))
            yield break;

        foreach (var targetFramework in targetFrameworks.EnumerateObject())
        {
            foreach (var package in targetFramework.Value.EnumerateObject())
            {
                if (!package.Name.StartsWith("Jellyfin.", StringComparison.Ordinal))
                    continue;

                var resolved = package.Value.TryGetProperty("resolved", out var value)
                    ? value.GetString()
                    : null;
                yield return (lockFile, package.Name, resolved);
            }
        }
    }

    private static string FormatUserAgent(HttpClient client)
    {
        return string.Join(" ", client.DefaultRequestHeaders.UserAgent.Select(x => x.ToString()));
    }
}

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

public class BuildConfigurationTests
{
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
            new Regex(@"^AniLibertyStrmPlugin/\d+\.\d+\.\d+\.\d+ \(Jellyfin; \+https://github\.com/queukat/AniLibriaStrmPlugin\)$"),
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
        Assert.True(Regex.Matches(allSource, "PluginIdentity\\.UserAgent").Count >= 3);
    }

    [Fact]
    public void PublicReadme_UsesSystemPresentationLanguage()
    {
        var root = FindRepoRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var lower = readme.ToLowerInvariant();

        Assert.Contains("Signal Acquisition Layer", readme, StringComparison.Ordinal);
        Assert.Contains("Reconstruction Core", readme, StringComparison.Ordinal);
        Assert.Contains("Mirror Governance Layer", readme, StringComparison.Ordinal);
        Assert.Contains("Operational Command Center", readme, StringComparison.Ordinal);
        Assert.Contains("Quality Gates", readme, StringComparison.Ordinal);
        Assert.Contains("Resources/readme/hero-reconstruction-core.png", readme, StringComparison.Ordinal);
        Assert.Contains("Resources/readme/system-capability-layers.png", readme, StringComparison.Ordinal);
        Assert.Contains("Resources/readme/operational-command-center.png", readme, StringComparison.Ordinal);

        foreach (var banned in new[] { "small script", "small helper", "quick tool", "wrapper", "utility", "auth helper" })
            Assert.DoesNotContain(banned, lower, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Resources", "readme", "hero-reconstruction-core.png")));
        Assert.True(File.Exists(Path.Combine(root, "Resources", "readme", "system-capability-layers.png")));
        Assert.True(File.Exists(Path.Combine(root, "Resources", "readme", "operational-command-center.png")));
    }

    [Fact]
    public void PublicReadme_DocumentsInstallAndProxyPathContracts()
    {
        var root = FindRepoRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var configPage = File.ReadAllText(Path.Combine(root, "Configuration", "configPage.html"));

        Assert.Contains("aniliberty-strm-plugin_*.zip", readme, StringComparison.Ordinal);
        Assert.Contains("C:\\ProgramData\\Jellyfin\\Server\\plugins\\AniLiberty STRM Plugin_", readme, StringComparison.Ordinal);
        Assert.Contains("/config/plugins/AniLiberty STRM Plugin_", readme, StringComparison.Ordinal);
        Assert.Contains("C:\\ProgramData\\Jellyfin\\Server\\plugins\\configurations\\AniLibertyStrmPlugin.xml", readme, StringComparison.Ordinal);
        Assert.Contains("/config/plugins/configurations/AniLibertyStrmPlugin.xml", readme, StringComparison.Ordinal);
        Assert.Contains("Do not use `localhost` for another device", readme, StringComparison.Ordinal);
        Assert.Contains("After changing this field, run **Generate AniLiberty STRM library** again", readme, StringComparison.Ordinal);
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

        Assert.Contains("Append picked folder", readme, StringComparison.Ordinal);
        Assert.Contains("Full Catalog and Favorites Are Separate Rails", readme, StringComparison.Ordinal);
        Assert.Contains("Do not point **All Titles STRM Path** and **Favorites STRM Path** at the same directory", readme, StringComparison.Ordinal);
        Assert.Contains("Each output", readme, StringComparison.Ordinal);
        Assert.Contains("Running a scheduled task is not enough by itself", readme, StringComparison.Ordinal);
        Assert.Contains("If the matching enable flag is off", readme, StringComparison.Ordinal);
        Assert.Contains("Requires **Generate full catalog library** to be enabled", readme, StringComparison.Ordinal);
        Assert.Contains("Requires **Generate favorites library** to be enabled", readme, StringComparison.Ordinal);
        Assert.Contains("Mirror Governance, plain rule", readme, StringComparison.Ordinal);
        Assert.Contains("does not delete anything", readme, StringComparison.Ordinal);
        Assert.Contains("Existing `.nfo` files and artwork that were not generated by this plugin are preserved", readme, StringComparison.Ordinal);
        Assert.Contains("Max UI log lines", readme, StringComparison.Ordinal);
        Assert.Contains("Watch-State Exchange", readme, StringComparison.Ordinal);
        Assert.Contains("Enabled by **Sync playback progress to AniLiberty**", readme, StringComparison.Ordinal);
        Assert.Contains("Run manually through **Sync AniLiberty watch progress to Jellyfin**", readme, StringComparison.Ordinal);
        Assert.Contains("If the server has exactly one Jellyfin user", readme, StringComparison.Ordinal);
        Assert.Contains("Sync step (seconds)", readme, StringComparison.Ordinal);
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
        Assert.Contains("AniLiberty STRM", notes, StringComparison.Ordinal);
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

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

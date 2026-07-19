using System;
using System.IO;
using AniLibertyStrmPlugin.Web;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class AniLibertyWebUiIndexHtmlPatcherTests
{
    [Fact]
    public void WebUiScript_UsesPublicEndpointsAndHashRouteItemIds()
    {
        var script = AniLibertyPopularityWebUiScript.Build();

        Assert.Contains("AniLibertyMetadata/Popularity", script, StringComparison.Ordinal);
        Assert.Contains("AniLibertyMetadata/Assets/aniliberty-rating.png", script, StringComparison.Ordinal);
        Assert.Contains("url.hash", script, StringComparison.Ordinal);
        Assert.Contains("URLSearchParams", script, StringComparison.Ordinal);
        Assert.Contains("data-aniliberty-loaded", script, StringComparison.Ordinal);
        Assert.DoesNotContain("AniLibertyToken", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_CreatesBackupAndInjectsPopularityBadge()
    {
        using var output = TempOutput.Create();
        var indexPath = output.WriteIndexHtml("<html><head></head><body><main id=\"root\"></main></body></html>");

        var result = AniLibertyWebUiIndexHtmlPatcher.Apply(indexPath);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        var patched = File.ReadAllText(indexPath);
        Assert.Contains("AniLibertyPopularityBadge:start", patched, StringComparison.Ordinal);
        Assert.Contains("id=\"aniliberty-popularity-badge-script\"", patched, StringComparison.Ordinal);
        Assert.Contains("/AniLibertyMetadata/Assets/aniliberty-popularity.js", patched, StringComparison.Ordinal);
        Assert.DoesNotContain("window.__anilibertyPopularityBadgeLoaded", patched, StringComparison.Ordinal);
        Assert.True(File.Exists(AniLibertyWebUiIndexHtmlPatcher.GetBackupPath(indexPath)));
        Assert.Equal("<html><head></head><body><main id=\"root\"></main></body></html>", File.ReadAllText(AniLibertyWebUiIndexHtmlPatcher.GetBackupPath(indexPath)));
    }

    [Fact]
    public void Apply_IsIdempotentAndDoesNotDuplicatePopularityBadge()
    {
        using var output = TempOutput.Create();
        var indexPath = output.WriteIndexHtml("<html><body><main></main></body></html>");

        var first = AniLibertyWebUiIndexHtmlPatcher.Apply(indexPath);
        var second = AniLibertyWebUiIndexHtmlPatcher.Apply(indexPath);

        Assert.True(first.Success);
        Assert.True(first.Changed);
        Assert.True(second.Success);
        Assert.False(second.Changed);

        var patched = File.ReadAllText(indexPath);
        Assert.Equal(1, CountOccurrences(patched, "AniLibertyPopularityBadge:start"));
        Assert.Equal(1, CountOccurrences(patched, "aniliberty-popularity-badge-script"));
    }

    [Fact]
    public void Restore_RemovesManagedPopularityBadge()
    {
        using var output = TempOutput.Create();
        var indexPath = output.WriteIndexHtml("<html><body><main data-user=\"kept\"></main></body></html>");

        AniLibertyWebUiIndexHtmlPatcher.Apply(indexPath);
        var result = AniLibertyWebUiIndexHtmlPatcher.Restore(indexPath);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        var restored = File.ReadAllText(indexPath);
        Assert.DoesNotContain("AniLibertyPopularityBadge:start", restored, StringComparison.Ordinal);
        Assert.DoesNotContain("aniliberty-popularity-badge-script", restored, StringComparison.Ordinal);
        Assert.Contains("<main data-user=\"kept\"></main>", restored, StringComparison.Ordinal);
        Assert.False(File.Exists(AniLibertyWebUiIndexHtmlPatcher.GetBackupPath(indexPath)));
    }

    [Fact]
    public void Apply_RejectsFileWithoutBodyTag()
    {
        using var output = TempOutput.Create();
        var content = "{\"not\":\"html\"}";
        var indexPath = output.WriteIndexHtml(content);

        var result = AniLibertyWebUiIndexHtmlPatcher.Apply(indexPath);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Equal(content, File.ReadAllText(indexPath));
        Assert.False(File.Exists(AniLibertyWebUiIndexHtmlPatcher.GetBackupPath(indexPath)));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while (true)
        {
            index = text.IndexOf(value, index, StringComparison.Ordinal);
            if (index < 0)
                return count;

            count++;
            index += value.Length;
        }
    }

    private sealed class TempOutput : IDisposable
    {
        private TempOutput(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempOutput Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "alib-web-patch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempOutput(path);
        }

        public string WriteIndexHtml(string content)
        {
            var webRoot = System.IO.Path.Combine(Path, "jellyfin-web");
            Directory.CreateDirectory(webRoot);
            var indexPath = System.IO.Path.Combine(webRoot, "index.html");
            File.WriteAllText(indexPath, content);
            return indexPath;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for Windows file handles.
            }
        }
    }
}

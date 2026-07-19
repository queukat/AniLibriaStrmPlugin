using Microsoft.Extensions.Logging;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class PluginTests
{
    [Fact]
    public void Constructor_ExposesIdentityPagesAndThumb()
    {
        using var host = PluginTestHost.Create();
        var plugin = host.Plugin;

        Assert.Same(plugin, Plugin.Instance);
        Assert.Equal("AniLiberty STRM Plugin", plugin.Name);
        Assert.Equal(Guid.Parse("cce0798d-c8b7-4265-b08c-dc9e7bd3fc0f"), plugin.Id);
        Assert.Equal(MediaBrowser.Model.Drawing.ImageFormat.Png, plugin.ThumbImageFormat);

        var page = Assert.Single(plugin.GetPages());
        Assert.Equal("AniLibertyStrm", page.Name);
        Assert.Equal("AniLibertyStrmPlugin.Configuration.configPage.html", page.EmbeddedResourcePath);

        using var thumb = plugin.GetThumbImage();
        Assert.NotNull(thumb);
        Assert.True(thumb.Length > 0);
    }

    [Fact]
    public void AppendTaskLog_FiltersUiLogAndKeepsRawSupportLog()
    {
        using var host = PluginTestHost.Create(cfg =>
        {
            cfg.EnableRawSupportLogs = true;
            cfg.EnableDebugLogs = false;
            cfg.UiMinLogLevel = LogLevel.Information;
            cfg.LastLogMaxLines = 2;
        });
        var plugin = host.Plugin;

        plugin.AppendTaskLog("debug-line", LogLevel.Debug);
        plugin.AppendTaskLog("info-line", LogLevel.Information);
        plugin.AppendTaskLog("warn-line", LogLevel.Warning);
        plugin.AppendTaskLog("error-line", LogLevel.Error);

        Assert.DoesNotContain("debug-line", plugin.Configuration.LastTaskLog);
        Assert.Contains("info-line", plugin.Configuration.LastTaskLog);
        Assert.Contains("warn-line", plugin.Configuration.LastTaskLog);
        Assert.Contains("error-line", plugin.Configuration.LastTaskLog);

        Assert.Contains("debug-line", plugin.Configuration.LastRawTaskLog);
        Assert.Contains("info-line", plugin.Configuration.LastRawTaskLog);
    }

    [Fact]
    public void AppendRawSupportLog_RespectsToggleAndClearFlushPersistConfiguration()
    {
        using var host = PluginTestHost.Create(cfg => cfg.EnableRawSupportLogs = false);
        var plugin = host.Plugin;

        plugin.AppendRawSupportLog("hidden");
        Assert.Empty(plugin.Configuration.LastRawTaskLog);

        var cfg = plugin.Configuration;
        cfg.EnableRawSupportLogs = true;
        plugin.UpdateConfiguration(cfg);

        plugin.AppendRawSupportLog("visible");
        Assert.Contains("visible", plugin.Configuration.LastRawTaskLog);

        plugin.FlushLog();
        Assert.Same(plugin.Configuration, host.Xml.LastSerialized);
        Assert.NotNull(host.Xml.LastSerializedPath);

        plugin.ClearTaskLog();
        Assert.Empty(plugin.Configuration.LastTaskLog);
        Assert.Empty(plugin.Configuration.LastRawTaskLog);
    }
}

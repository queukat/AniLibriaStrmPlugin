using System.Text;
using AniLibertyStrmPlugin.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin;

/// <summary>
///     Main plugin class and owner of configuration and task-log buffer.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ImageFormat _thumbImageFormat = ImageFormat.Png;
    private readonly object _logSync = new();

    public Plugin(IApplicationPaths paths, IXmlSerializer xml)
        : base(paths, xml)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => PluginIdentity.DisplayName;
    public override Guid Id => Guid.Parse("cce0798d-c8b7-4265-b08c-dc9e7bd3fc0f");

    public ImageFormat ThumbImageFormat => _thumbImageFormat;

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "AniLibertyStrm",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }

    public void AppendTaskLog(string line, LogLevel level = LogLevel.Information)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        var ln = $"[{ts}] {line}";

        lock (_logSync)
        {
            var cfg = Configuration;
            if (cfg is null)
                return;

            if (cfg.EnableRawSupportLogs)
                cfg.LastRawTaskLog = AppendAndTrim(cfg.LastRawTaskLog, ln, Math.Max(200, cfg.LastRawLogMaxLines));

            if (!ShouldShowInUi(cfg, level))
                return;

            cfg.LastTaskLog = AppendAndTrim(cfg.LastTaskLog, ln, Math.Max(50, cfg.LastLogMaxLines));
            // Do not flush to disk on every line; flush in existing task-finally points.
        }
    }

    public void AppendRawSupportLog(string line)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        var ln = $"[{ts}] {line}";

        lock (_logSync)
        {
            var cfg = Configuration;
            if (cfg is null || !cfg.EnableRawSupportLogs)
                return;

            cfg.LastRawTaskLog = AppendAndTrim(cfg.LastRawTaskLog, ln, Math.Max(200, cfg.LastRawLogMaxLines));
        }
    }

    private static bool ShouldShowInUi(PluginConfiguration cfg, LogLevel level)
    {
        try
        {
            if (!cfg.EnableDebugLogs && (level == LogLevel.Debug || level == LogLevel.Trace))
                return false;

            return level >= cfg.UiMinLogLevel;
        }
        catch
        {
            // Logging must never break main logic.
            return true;
        }
    }

    private static string AppendAndTrim(string? existing, string line, int maxLines)
    {
        var joined = string.IsNullOrEmpty(existing) ? line : existing + "\n" + line;
        var arr = joined.Split('\n');
        return arr.Length > maxLines
            ? string.Join('\n', arr.Skip(arr.Length - maxLines))
            : joined;
    }

    public void FlushLog()
    {
        lock (_logSync)
        {
            var cfg = Configuration;
            UpdateConfiguration(cfg);
        }
    }

    public void ClearTaskLog()
    {
        lock (_logSync)
        {
            var cfg = Configuration;
            cfg.LastTaskLog = string.Empty;
            cfg.LastRawTaskLog = string.Empty;
            UpdateConfiguration(cfg);
        }
    }

    internal void UpdateConfiguration(PluginConfiguration newConfig)
    {
        Configuration = newConfig;
        SaveConfiguration();
    }

    public Stream GetThumbImage()
    {
        const string res = "AniLibertyStrmPlugin.Resources.icon.png";
        return GetType().Assembly.GetManifestResourceStream(res)!;
    }
}

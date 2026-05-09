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
    private readonly object _logSync = new();

    public Plugin(IApplicationPaths paths, IXmlSerializer xml)
        : base(paths, xml)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => PluginIdentity.DisplayName;
    public override Guid Id => Guid.Parse("cce0798d-c8b7-4265-b08c-dc9e7bd3fc0f");

    public ImageFormat ThumbImageFormat => ImageFormat.Png;

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
        // UI filter (configuration-based)
        try
        {
            // By default, do NOT write Debug/Trace to UI log (too noisy).
            if (Configuration != null &&
                !Configuration.EnableDebugLogs &&
                (level == LogLevel.Debug || level == LogLevel.Trace))
                return;

            if (Configuration != null && level < Configuration.UiMinLogLevel)
                return;
        }
        catch
        {
            // Logging must never break main logic.
        }

        var ts = DateTime.Now.ToString("HH:mm:ss");
        var ln = $"[{ts}] {line}";

        lock (_logSync)
        {
            var cfg = Configuration;
            if (cfg is null)
                return;

            var max = Math.Max(50, cfg.LastLogMaxLines);
            var existing = cfg.LastTaskLog ?? string.Empty;

            // Append quickly and trim old lines.
            var joined = string.IsNullOrEmpty(existing) ? ln : existing + "\n" + ln;
            var arr = joined.Split('\n');
            if (arr.Length > max)
                joined = string.Join('\n', arr.Skip(arr.Length - max));

            cfg.LastTaskLog = joined;
            // Do not flush to disk on every line; flush in existing task-finally points.
        }
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AniLibertyStrmPlugin.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace AniLibertyStrmPlugin
{
    /// <summary>
    ///   плагина + хранитель конфигурации и буфера логов.
    /// </summary>
    public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths paths, IXmlSerializer xml)
            : base(paths, xml)
        {
            Instance = this;
        }
        private readonly object _logSync = new();

        public static Plugin Instance { get; private set; }

        public override string Name => "AniLiberty STRM Plugin";
        public override Guid   Id   => Guid.Parse("cce0798d-c8b7-4265-b08c-dc9e7bd3fc0f");

        // ─────────────────────  - ──────────────────────
        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                Name = "AniLibertyStrm",                                   
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            };
        }


        // ──────────────────────────   ───────────────────────────────
        private readonly StringBuilder _logBuffer = new();

        public void AppendTaskLog(string line)
        {
            var ts = DateTime.Now.ToString("HH:mm:ss");
            var ln = $"[{ts}] {line}";

            lock (_logSync)
            {
                var max = Math.Max(50, Configuration?.LastLogMaxLines ?? 800);
                var existing = Configuration?.LastTaskLog ?? string.Empty;

                // Быстро добавить и усечь хвост
                var joined = string.IsNullOrEmpty(existing) ? ln : existing + "\n" + ln;
                var arr = joined.Split('\n');
                if (arr.Length > max)
                    joined = string.Join('\n', arr.Skip(arr.Length - max));

                Configuration.LastTaskLog = joined;
                // Не пишем на диск на каждый чих — дисковый flush делай там, где уже делал (например, в finally задач)
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

        // ──────────────────────────  ─────────────────────────────────
        public Stream GetThumbImage()
        {
            const string res = "AniLibertyStrmPlugin.Resources.icon.png";
            return GetType().Assembly.GetManifestResourceStream(res)!;
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Png;
    }
}

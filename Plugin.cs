using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AniLibriaStrmPlugin.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace AniLibriaStrmPlugin
{
    /// <summary>
    /// Точка входа плагина + хранитель конфигурации и буфера логов.
    /// </summary>
    public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths paths, IXmlSerializer xml)
            : base(paths, xml)
        {
            Instance = this;
        }

        public static Plugin Instance { get; private set; }

        public override string Name => "AniLibria STRM Plugin";
        public override Guid   Id   => Guid.Parse("cce0798d-c8b7-4265-b08c-dc9e7bd3fc0f");

        // ─────────────────────  - ──────────────────────
        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                Name = "AniLibriaStrm",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            };
        }


        // ──────────────────────────   ───────────────────────────────
        private readonly StringBuilder _logBuffer = new();

        public void AppendTaskLog(string message)
        {
            lock (_logBuffer)
            {
                _logBuffer.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");

                // Пишем в конфиг сразу – так «Show Logs» всегда увидит актуальный текст.
                var cfg               = Configuration;
                cfg.LastTaskLog       = _logBuffer.ToString();
                UpdateConfiguration(cfg);                 // включает SaveConfiguration()
            }
        }

        public void FlushLog()
        {
            lock (_logBuffer)
            {
                var cfg         = Configuration;
                cfg.LastTaskLog = _logBuffer.ToString();
                UpdateConfiguration(cfg);
            }
        }

        public void ClearTaskLog()
        {
            lock (_logBuffer)
            {
                _logBuffer.Clear();
                var cfg         = Configuration;
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
            const string res = "AniLibriaStrmPlugin.Resources.icon.png";
            return GetType().Assembly.GetManifestResourceStream(res)!;
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Png;
    }
}

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
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            
        }

        public override string Name => "AniLibria STRM Plugin";
        public override Guid Id => Guid.Parse("cce0798d-c8b7-4265-b08c-dc9e7bd3fc0f");

        public static Plugin Instance { get; private set; }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                Name = "AniLibriaStrm",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            };
        }
        private readonly StringBuilder _logBuffer = new();

        //       (LastTaskLog).
        public void AppendTaskLog(string message)
        {
            lock (_logBuffer)
            {
                _logBuffer.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        
                //      100 
                if (_logBuffer.Length > 0 && _logBuffer.ToString().Split('\n').Length % 100 == 0)
                    FlushLog();
            }
        }
        
        public void FlushLog()
        {
            var cfg = Configuration;
            cfg.LastTaskLog = _logBuffer.ToString();
            UpdateConfiguration(cfg);
        }

        public void ClearTaskLog()
        {
            var config = Configuration;
            config.LastTaskLog = string.Empty;
            UpdateConfiguration(config);
        }

        internal void UpdateConfiguration(PluginConfiguration newConfig)
        {
            Configuration = newConfig;
            SaveConfiguration();
        }

        // ======================= :  icon =======================
        // Provide the image data
        public Stream GetThumbImage()
        {
            // "icon.png" is embedded as a resource in your .csproj:
            //   <EmbeddedResource Include="Resources\icon.png" />
            var asm = GetType().Assembly;
            var resourceName = $"{GetType().Namespace}.Resources.icon.png";
            const string Res = "AniLibriaStrmPlugin.Resources.icon.png";
            return GetType().Assembly.GetManifestResourceStream(Res)!;

        }

        // Indicate it is a PNG
        public ImageFormat ThumbImageFormat => ImageFormat.Png;
    }
}

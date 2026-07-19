using System.Reflection;
using AniLibertyStrmPlugin.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;

namespace AniLibertyStrmPlugin.Tests;

internal sealed class PluginTestHost : IDisposable
{
    private PluginTestHost(string root, FakeXmlSerializer xml, Plugin plugin)
    {
        Root = root;
        Xml = xml;
        Plugin = plugin;
    }

    public string Root { get; }
    public FakeXmlSerializer Xml { get; }
    public Plugin Plugin { get; }

    public static PluginTestHost Create(Action<PluginConfiguration>? configure = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "alib-plugin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xml = new FakeXmlSerializer();
        var plugin = new Plugin(new FakeApplicationPaths(root), xml);
        var cfg = new PluginConfiguration
        {
            UseJellyfinPlaybackProxy = false,
            StrmAllPath = Path.Combine(root, "all"),
            StrmFavoritesPath = Path.Combine(root, "favorites")
        };

        configure?.Invoke(cfg);
        plugin.UpdateConfiguration(cfg);

        return new PluginTestHost(root, xml, plugin);
    }

    public void Dispose()
    {
        typeof(Plugin)
            .GetProperty(nameof(Plugin.Instance), BindingFlags.Public | BindingFlags.Static)
            ?.GetSetMethod(nonPublic: true)
            ?.Invoke(null, [null]);

        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }

    internal sealed class FakeXmlSerializer : IXmlSerializer
    {
        public object? LastSerialized { get; private set; }
        public string? LastSerializedPath { get; private set; }

        public object DeserializeFromStream(Type type, Stream stream) => Activator.CreateInstance(type)!;

        public void SerializeToStream(object obj, Stream stream)
        {
            LastSerialized = obj;
        }

        public void SerializeToFile(object obj, string file)
        {
            LastSerialized = obj;
            LastSerializedPath = file;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        }

        public object DeserializeFromFile(Type type, string file) => Activator.CreateInstance(type)!;

        public object DeserializeFromBytes(Type type, byte[] buffer) => Activator.CreateInstance(type)!;
    }

    private sealed class FakeApplicationPaths(string root) : IApplicationPaths
    {
        public string ProgramDataPath => Path.Combine(root, "program-data");
        public string WebPath => Path.Combine(root, "web");
        public string ProgramSystemPath => Path.Combine(root, "system");
        public string DataPath => Path.Combine(root, "data");
        public string ImageCachePath => Path.Combine(root, "image-cache");
        public string PluginsPath => Path.Combine(root, "plugins");
        public string PluginConfigurationsPath => Path.Combine(root, "plugin-config");
        public string LogDirectoryPath => Path.Combine(root, "logs");
        public string ConfigurationDirectoryPath => Path.Combine(root, "config");
        public string SystemConfigurationFilePath => Path.Combine(ConfigurationDirectoryPath, "system.xml");
        public string CachePath => Path.Combine(root, "cache");
        public string TempDirectory => Path.Combine(root, "temp");
        public string VirtualDataPath => Path.Combine(root, "virtual-data");
        public string TrickplayPath => Path.Combine(root, "trickplay");
        public string BackupPath => Path.Combine(root, "backup");

        public void MakeSanityCheckOrThrow()
        {
        }

        public void CreateAndCheckMarker(string path, string marker, bool recursive)
        {
            Directory.CreateDirectory(path);
        }
    }
}

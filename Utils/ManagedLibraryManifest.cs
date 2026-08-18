using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AniLibertyStrmPlugin.Configuration;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Utils;

internal sealed class ManagedLibraryManifest
{
    public const string StateDirectoryName = ".aniliberty-strm-plugin";
    public const string ManifestFileName = "manifest.json";
    public const string GeneratedXmlMarker = "generated-by AniLibertyStrmPlugin";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Dictionary<string, ManagedFileEntry> _previous;
    private readonly Dictionary<string, ManagedFileEntry> _current;

    private ManagedLibraryManifest(string rootPath, ManifestDocument document, bool isFirstRun)
    {
        RootPath = Path.GetFullPath(rootPath);
        IsFirstRun = isFirstRun;
        _previous = document.Files
            .Where(x => !string.IsNullOrWhiteSpace(x.RelativePath))
            .GroupBy(x => NormalizeRelativePath(x.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);
        _current = new Dictionary<string, ManagedFileEntry>(StringComparer.OrdinalIgnoreCase);
    }

    public string RootPath { get; }
    public bool IsFirstRun { get; }

    private string ManifestPath => Path.Combine(RootPath, StateDirectoryName, ManifestFileName);

    public static async Task<ManagedLibraryManifest> LoadAsync(string rootPath, CancellationToken ct)
    {
        var root = Path.GetFullPath(rootPath);
        var path = Path.Combine(root, StateDirectoryName, ManifestFileName);
        if (!File.Exists(path))
            return new ManagedLibraryManifest(root, new ManifestDocument(), isFirstRun: true);

        try
        {
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<ManifestDocument>(stream, JsonOptions, ct)
                           ?? new ManifestDocument();
            return new ManagedLibraryManifest(root, document, isFirstRun: false);
        }
        catch
        {
            return new ManagedLibraryManifest(root, new ManifestDocument(), isFirstRun: true);
        }
    }

    public bool IsManaged(string path)
    {
        var rel = GetRelativePath(path);
        return _current.ContainsKey(rel) || _previous.ContainsKey(rel);
    }

    public static bool HasGeneratedXmlMarker(string text)
    {
        return text.Contains(GeneratedXmlMarker, StringComparison.Ordinal);
    }

    public void TrackText(
        string path,
        string kind,
        string content,
        Encoding? encoding,
        int? releaseId,
        string? episodeId,
        string? source)
    {
        var bytes = (encoding ?? new UTF8Encoding(false)).GetBytes(content);
        Track(path, kind, Sha256(bytes), releaseId, episodeId, source);
    }

    public void TrackBytes(
        string path,
        string kind,
        byte[] bytes,
        int? releaseId,
        string? episodeId,
        string? source)
    {
        Track(path, kind, Sha256(bytes), releaseId, episodeId, source);
    }

    public void TrackExisting(
        string path,
        string kind,
        int? releaseId,
        string? episodeId,
        string? source)
    {
        Track(path, kind, contentHash: null, releaseId, episodeId, source);
    }

    public Task ApplyCleanupAsync(StaleCleanupMode mode, ILogger log, CancellationToken ct)
    {
        if (IsFirstRun)
        {
            if (_current.Count > 0)
                log.Info("[MIRROR] Managed manifest initialized with {0} files. Pruning is skipped on first run.", _current.Count);
            return Task.CompletedTask;
        }

        var stale = _previous
            .Where(x => !_current.ContainsKey(x.Key))
            .Where(x => File.Exists(Path.Combine(RootPath, x.Key.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();

        if (stale.Count == 0)
            return Task.CompletedTask;

        if (mode == StaleCleanupMode.Off)
        {
            log.Info("[MIRROR] Stale cleanup is disabled. Stale managed files kept: {0}", stale.Count);
            return Task.CompletedTask;
        }

        if (mode == StaleCleanupMode.DryRun)
        {
            log.Warn("[MIRROR] Dry-run: {0} stale managed file(s) would be removed. Switch cleanup mode to Delete to remove them.", stale.Count);
            foreach (var entry in stale.Take(20))
                log.Debug("[MIRROR] Dry-run stale: {0}", entry.Key);
            if (stale.Count > 20)
                log.Debug("[MIRROR] Dry-run stale: ...and {0} more", stale.Count - 20);
            return Task.CompletedTask;
        }

        var deleted = new List<string>();
        foreach (var (relativePath, _) in stale)
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                File.Delete(fullPath);
                deleted.Add(relativePath);
                log.Debug("[MIRROR] Removed stale managed file: {0}", relativePath);
            }
            catch (Exception ex)
            {
                log.Warn(ex, "[MIRROR] Failed to remove stale managed file: {0}", relativePath);
            }
        }

        if (deleted.Count > 0)
            log.Info("[MIRROR] Removed stale managed files: {0}", deleted.Count);

        foreach (var dir in deleted
                     .Select(x => Path.GetDirectoryName(Path.Combine(RootPath, x.Replace('/', Path.DirectorySeparatorChar))))
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(x => x!.Length))
        {
            DeleteEmptyParents(dir!, log);
        }

        return Task.CompletedTask;
    }

    public async Task SaveAsync(StaleCleanupMode mode, CancellationToken ct)
    {
        if (mode == StaleCleanupMode.Delete)
        {
            foreach (var key in _previous.Keys.ToList())
            {
                if (!_current.ContainsKey(key))
                {
                    var fullPath = Path.Combine(RootPath, key.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(fullPath))
                        _previous.Remove(key);
                }
            }
        }

        var document = new ManifestDocument
        {
            SchemaVersion = 1,
            Product = PluginIdentity.ProductToken,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Files = _previous.Values
                .Concat(_current.Values)
                .OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        await using var stream = File.Create(ManifestPath);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, ct);
    }

    private void Track(
        string path,
        string kind,
        string? contentHash,
        int? releaseId,
        string? episodeId,
        string? source)
    {
        var rel = GetRelativePath(path);
        _current[rel] = new ManagedFileEntry
        {
            RelativePath = rel,
            Kind = kind,
            ReleaseId = releaseId,
            ReleaseEpisodeId = string.IsNullOrWhiteSpace(episodeId) ? null : episodeId,
            Source = string.IsNullOrWhiteSpace(source) ? null : source,
            ContentSha256 = contentHash,
            LastSeenUtc = DateTimeOffset.UtcNow
        };
        _previous.Remove(rel);
    }

    private string GetRelativePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var rootWithSeparator = RootPath.EndsWith(Path.DirectorySeparatorChar)
            ? RootPath
            : RootPath + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Managed file path is outside root: {path}");

        return NormalizeRelativePath(Path.GetRelativePath(RootPath, fullPath));
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private void DeleteEmptyParents(string startDir, ILogger log)
    {
        var root = Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar);
        var current = Path.GetFullPath(startDir);

        while (!string.Equals(current.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(Path.GetFileName(current), StateDirectoryName, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any())
                    return;

                Directory.Delete(current);
                log.Info("[MIRROR] Removed empty generated directory: {0}", NormalizeRelativePath(Path.GetRelativePath(RootPath, current)));
            }
            catch
            {
                return;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent))
                return;
            current = parent;
        }
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class ManifestDocument
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("product")] public string Product { get; set; } = PluginIdentity.ProductToken;
        [JsonPropertyName("generatedAtUtc")] public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        [JsonPropertyName("files")] public List<ManagedFileEntry> Files { get; set; } = new();
    }

    private sealed class ManagedFileEntry
    {
        [JsonPropertyName("relativePath")] public string RelativePath { get; set; } = string.Empty;
        [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
        [JsonPropertyName("releaseId")] public int? ReleaseId { get; set; }
        [JsonPropertyName("releaseEpisodeId")] public string? ReleaseEpisodeId { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("contentSha256")] public string? ContentSha256 { get; set; }
        [JsonPropertyName("lastSeenUtc")] public DateTimeOffset LastSeenUtc { get; set; }
    }
}

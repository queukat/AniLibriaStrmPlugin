using System.Text.Json;
using System.Text.Json.Serialization;

namespace AniLibertyStrmPlugin.Utils;

internal sealed class AniLibertyMediaSegmentState
{
    public const int SchemaVersion = 1;
    public const string MediaSegmentsFileName = "media-segments.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly Dictionary<string, AniLibertyMediaSegmentEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public static event Action<string, int>? Saved;

    public AniLibertyMediaSegmentState(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }

    public string StatePath => Path.Combine(
        RootPath,
        ManagedLibraryManifest.StateDirectoryName,
        MediaSegmentsFileName);

    public void Track(
        string strmPath,
        int releaseId,
        string? episodeId,
        IReadOnlyCollection<AniLibertyMediaSegment> segments)
    {
        if (segments.Count == 0)
            return;

        var normalizedSegments = segments
            .Where(x => x.StartTicks >= 0 && x.EndTicks > x.StartTicks && !string.IsNullOrWhiteSpace(x.Type))
            .Select(x => new AniLibertyMediaSegment
            {
                Type = x.Type,
                StartTicks = x.StartTicks,
                EndTicks = x.EndTicks
            })
            .ToArray();
        if (normalizedSegments.Length == 0)
            return;

        var relativePath = GetRelativePath(RootPath, strmPath);
        _entries[relativePath] = new AniLibertyMediaSegmentEntry
        {
            RelativePath = relativePath,
            ReleaseId = releaseId,
            ReleaseEpisodeId = string.IsNullOrWhiteSpace(episodeId) ? null : episodeId,
            Segments = normalizedSegments,
            LastSeenUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var document = new AniLibertyMediaSegmentDocument
        {
            SchemaVersion = SchemaVersion,
            Product = PluginIdentity.ProductToken,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Entries = _entries.Values
                .OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        var json = JsonSerializer.Serialize(document, JsonOptions);
        await WriteTextAtomicallyAsync(StatePath, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        Saved?.Invoke(RootPath, document.Entries.Length);
    }

    public static async Task<AniLibertyMediaSegmentEntry?> TryReadEntryForPathAsync(
        string episodeFilePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(episodeFilePath))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(episodeFilePath);
            if (!TryResolveRootForPath(fullPath, out var rootPath))
                return null;

            var statePath = Path.Combine(
                rootPath,
                ManagedLibraryManifest.StateDirectoryName,
                MediaSegmentsFileName);
            if (!File.Exists(statePath))
                return null;

            var json = await File.ReadAllTextAsync(statePath, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<AniLibertyMediaSegmentDocument>(json, JsonOptions);
            if (document?.SchemaVersion != SchemaVersion ||
                document.Entries.Length == 0 ||
                !string.Equals(document.Product, PluginIdentity.ProductToken, StringComparison.Ordinal))
            {
                return null;
            }

            var relativePath = GetRelativePath(rootPath, fullPath);
            return document.Entries.FirstOrDefault(entry =>
                string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase) &&
                entry.Segments.Length > 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public static bool IsConfiguredAniLibertyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            return EnumerateConfiguredRoots().Any(root => IsUnderRoot(root, fullPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public static bool StateFileExistsForPath(string? episodeFilePath)
    {
        if (string.IsNullOrWhiteSpace(episodeFilePath))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(episodeFilePath);
            if (!TryResolveRootForPath(fullPath, out var rootPath))
                return false;

            return File.Exists(Path.Combine(
                rootPath,
                ManagedLibraryManifest.StateDirectoryName,
                MediaSegmentsFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryResolveRootForPath(string fullPath, out string rootPath)
    {
        rootPath = EnumerateConfiguredRoots()
            .Where(root => IsUnderRoot(root, fullPath))
            .OrderByDescending(root => root.Length)
            .FirstOrDefault() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(rootPath))
            return true;

        var directory = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var statePath = Path.Combine(
                directory,
                ManagedLibraryManifest.StateDirectoryName,
                MediaSegmentsFileName);
            if (File.Exists(statePath))
            {
                rootPath = directory;
                return true;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return false;
    }

    private static IEnumerable<string> EnumerateConfiguredRoots()
    {
        var cfg = Plugin.Instance?.Configuration;
        foreach (var root in new[] { cfg?.StrmAllPath, cfg?.StrmFavoritesPath })
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            yield return fullRoot;
        }
    }

    private static bool IsUnderRoot(string rootPath, string fullPath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(fullPath);

        return normalizedPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRelativePath(string rootPath, string path)
    {
        var root = Path.GetFullPath(rootPath);
        var fullPath = Path.GetFullPath(path);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Media segment path is outside root: {path}");

        return NormalizeRelativePath(Path.GetRelativePath(root, fullPath));
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static async Task WriteTextAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException($"Failed to determine directory for path '{path}'.");

        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, Path.GetFileName(path) + ".tmp." + Guid.NewGuid().ToString("N"));

        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Best-effort cleanup for interrupted atomic writes.
                }
            }
        }
    }
}

internal sealed class AniLibertyMediaSegmentDocument
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = AniLibertyMediaSegmentState.SchemaVersion;
    [JsonPropertyName("product")] public string Product { get; set; } = PluginIdentity.ProductToken;
    [JsonPropertyName("generatedAtUtc")] public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("entries")] public AniLibertyMediaSegmentEntry[] Entries { get; set; } = Array.Empty<AniLibertyMediaSegmentEntry>();
}

internal sealed class AniLibertyMediaSegmentEntry
{
    [JsonPropertyName("relativePath")] public string RelativePath { get; set; } = string.Empty;
    [JsonPropertyName("releaseId")] public int ReleaseId { get; set; }
    [JsonPropertyName("releaseEpisodeId")] public string? ReleaseEpisodeId { get; set; }
    [JsonPropertyName("segments")] public AniLibertyMediaSegment[] Segments { get; set; } = Array.Empty<AniLibertyMediaSegment>();
    [JsonPropertyName("lastSeenUtc")] public DateTimeOffset LastSeenUtc { get; set; }
}

internal sealed class AniLibertyMediaSegment
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("startTicks")] public long StartTicks { get; set; }
    [JsonPropertyName("endTicks")] public long EndTicks { get; set; }
}

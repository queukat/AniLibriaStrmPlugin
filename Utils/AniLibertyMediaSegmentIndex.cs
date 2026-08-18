using System.Collections.Concurrent;
using System.Text.Json;

namespace AniLibertyStrmPlugin.Utils;

/// <summary>
/// Provides a process-wide, bounded cache of immutable media-segment snapshots.
/// Warmup and the Jellyfin provider share this instance so a state file is parsed once
/// and individual episode lookups remain O(1).
/// </summary>
public sealed class AniLibertyMediaSegmentIndex
{
    private const int MaxCachedRoots = 4;
    private static readonly TimeSpan CacheIdleLifetime = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private int _loadCount;

    internal int LoadCount => Volatile.Read(ref _loadCount);

    internal async Task<AniLibertyMediaSegmentSnapshot?> GetSnapshotForPathAsync(
        string episodeFilePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(episodeFilePath))
            return null;

        string rootPath;
        string statePath;
        try
        {
            var fullPath = Path.GetFullPath(episodeFilePath);
            if (!AniLibertyMediaSegmentState.TryResolveStatePath(fullPath, out rootPath, out statePath))
                return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        var fingerprint = TryGetFingerprint(statePath);
        if (fingerprint is null)
            return null;

        var nowTicks = DateTime.UtcNow.Ticks;
        TrimCache(nowTicks);

        var candidate = new CacheEntry(
            fingerprint.Value,
            new Lazy<Task<AniLibertyMediaSegmentSnapshot?>>(
                () => LoadSnapshotAsync(rootPath, statePath),
                LazyThreadSafetyMode.ExecutionAndPublication),
            nowTicks);

        var selected = _cache.AddOrUpdate(
            statePath,
            candidate,
            (_, existing) => existing.Fingerprint == fingerprint.Value ? existing : candidate);
        selected.Touch(nowTicks);
        TrimCache(nowTicks, statePath);

        try
        {
            return await selected.Snapshot.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            RemoveIfCurrent(statePath, selected);
            throw;
        }
    }

    internal async Task<AniLibertyMediaSegmentEntry?> TryGetEntryForPathAsync(
        string episodeFilePath,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotForPathAsync(episodeFilePath, cancellationToken).ConfigureAwait(false);
        return snapshot?.TryGetEntry(episodeFilePath, out var entry) == true ? entry : null;
    }

    internal void InvalidateRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return;

        try
        {
            var statePath = Path.Combine(
                Path.GetFullPath(rootPath),
                ManagedLibraryManifest.StateDirectoryName,
                AniLibertyMediaSegmentState.MediaSegmentsFileName);
            _cache.TryRemove(statePath, out _);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Invalid roots cannot have a usable cached snapshot.
        }
    }

    private async Task<AniLibertyMediaSegmentSnapshot?> LoadSnapshotAsync(string rootPath, string statePath)
    {
        Interlocked.Increment(ref _loadCount);

        try
        {
            await using var stream = new FileStream(
                statePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<AniLibertyMediaSegmentDocument>(
                    stream,
                    AniLibertyMediaSegmentState.SerializerOptions,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (document?.SchemaVersion != AniLibertyMediaSegmentState.SchemaVersion ||
                !string.Equals(document.Product, PluginIdentity.ProductToken, StringComparison.Ordinal))
            {
                return null;
            }

            var entries = new Dictionary<string, AniLibertyMediaSegmentEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in document.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.RelativePath) || entry.Segments.Length == 0)
                    continue;

                entries[AniLibertyMediaSegmentState.NormalizeRelativePath(entry.RelativePath)] = entry;
            }

            return new AniLibertyMediaSegmentSnapshot(rootPath, entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private void TrimCache(long nowTicks, string? protectedStatePath = null)
    {
        var idleCutoff = nowTicks - CacheIdleLifetime.Ticks;
        foreach (var pair in _cache)
        {
            if (!string.Equals(pair.Key, protectedStatePath, StringComparison.OrdinalIgnoreCase) &&
                pair.Value.LastAccessTicks < idleCutoff)
                RemoveIfCurrent(pair.Key, pair.Value);
        }

        var overflow = _cache.Count - MaxCachedRoots;
        if (overflow <= 0)
            return;

        foreach (var pair in _cache
                     .Where(x => !string.Equals(x.Key, protectedStatePath, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => x.Value.LastAccessTicks)
                     .Take(overflow))
            RemoveIfCurrent(pair.Key, pair.Value);
    }

    private void RemoveIfCurrent(string statePath, CacheEntry entry)
    {
        ((ICollection<KeyValuePair<string, CacheEntry>>)_cache)
            .Remove(new KeyValuePair<string, CacheEntry>(statePath, entry));
    }

    private static FileFingerprint? TryGetFingerprint(string statePath)
    {
        try
        {
            var info = new FileInfo(statePath);
            return info.Exists
                ? new FileFingerprint(info.Length, info.LastWriteTimeUtc.Ticks)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private sealed class CacheEntry(
        FileFingerprint fingerprint,
        Lazy<Task<AniLibertyMediaSegmentSnapshot?>> snapshot,
        long lastAccessTicks)
    {
        private long _lastAccessTicks = lastAccessTicks;

        public FileFingerprint Fingerprint { get; } = fingerprint;
        public Lazy<Task<AniLibertyMediaSegmentSnapshot?>> Snapshot { get; } = snapshot;
        public long LastAccessTicks => Volatile.Read(ref _lastAccessTicks);

        public void Touch(long ticks) => Interlocked.Exchange(ref _lastAccessTicks, ticks);
    }

    private readonly record struct FileFingerprint(long Length, long LastWriteUtcTicks);
}

internal sealed class AniLibertyMediaSegmentSnapshot(
    string rootPath,
    IReadOnlyDictionary<string, AniLibertyMediaSegmentEntry> entries)
{
    public string RootPath { get; } = Path.GetFullPath(rootPath);
    public int Count => entries.Count;

    public bool TryGetEntry(string episodeFilePath, out AniLibertyMediaSegmentEntry entry)
    {
        entry = null!;
        try
        {
            var relativePath = AniLibertyMediaSegmentState.GetRelativePath(RootPath, episodeFilePath);
            return entries.TryGetValue(relativePath, out entry!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}

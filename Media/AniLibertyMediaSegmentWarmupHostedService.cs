using System.Collections.Concurrent;
using System.Reflection;
using AniLibertyStrmPlugin.Utils;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Media;

/// <summary>
/// Runs the AniLiberty segment provider shortly after STRM items appear, instead of waiting
/// for Jellyfin's next global media-segment scheduled task.
/// </summary>
public sealed class AniLibertyMediaSegmentWarmupHostedService : IHostedService, IDisposable
{
    private static readonly TimeSpan ItemChangedDebounce = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ScanCompletedDebounce = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ScanPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StartupDebounce = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MissingSegmentPollInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MissingSegmentRetryDelay = TimeSpan.FromHours(2);
    private const int MaxMissingItemsPerFullPass = 200;

    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSegmentManager _mediaSegmentManager;
    private readonly AniLibertyMediaSegmentIndex _mediaSegmentIndex;
    private readonly ILogger<AniLibertyMediaSegmentWarmupHostedService> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _pendingItemIds = new();
    private readonly ConcurrentDictionary<Guid, RetryState> _nextRetryUtcByItemId = new();
    private readonly SemaphoreSlim _workerLock = new(1, 1);
    private readonly CancellationTokenSource _stopCts = new();
    private readonly object _timerLock = new();
    private readonly object _fullPassLock = new();

    private Timer? _debounceTimer;
    private Timer? _scanPollTimer;
    private Timer? _missingSegmentsTimer;
    private EventInfo? _scanCompletedEvent;
    private Delegate? _scanCompletedHandler;
    private volatile bool _fullPassRequested;
    private volatile bool _stopping;
    private int _fullPassStartIndex;
    private int _fullPassVersion;
    private string? _fullPassRootPath;
    private bool _fullPassInProgress;
    private bool _wasScanRunning;
    private string _lastReason = "Startup";

    public AniLibertyMediaSegmentWarmupHostedService(
        ILibraryManager libraryManager,
        IMediaSegmentManager mediaSegmentManager,
        AniLibertyMediaSegmentIndex mediaSegmentIndex,
        ILogger<AniLibertyMediaSegmentWarmupHostedService> logger)
    {
        _libraryManager = libraryManager;
        _mediaSegmentManager = mediaSegmentManager;
        _mediaSegmentIndex = mediaSegmentIndex;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[AniLiberty] Media segment warmup service starting.");

        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;
        AniLibertyMediaSegmentState.Saved += OnMediaSegmentStateSaved;

        TryHookScanCompletedEvent();

        _wasScanRunning = SafeIsScanRunning();
        if (_scanCompletedEvent is null)
            _scanPollTimer = new Timer(_ => ScanPollTick(), null, ScanPollInterval, ScanPollInterval);
        _missingSegmentsTimer = new Timer(
            _ => RequestFullPass(TimeSpan.Zero, "PeriodicMissingSegments"),
            null,
            MissingSegmentPollInterval,
            MissingSegmentPollInterval);

        RequestFullPass(StartupDebounce, "Startup");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[AniLiberty] Media segment warmup service stopping.");

        _stopping = true;
        _stopCts.Cancel();

        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
        AniLibertyMediaSegmentState.Saved -= OnMediaSegmentStateSaved;

        UnhookScanCompletedEvent();

        _debounceTimer?.Dispose();
        _scanPollTimer?.Dispose();
        _missingSegmentsTimer?.Dispose();

        _debounceTimer = null;
        _scanPollTimer = null;
        _missingSegmentsTimer = null;

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { _debounceTimer?.Dispose(); } catch { /* ignore */ }
        try { _scanPollTimer?.Dispose(); } catch { /* ignore */ }
        try { _missingSegmentsTimer?.Dispose(); } catch { /* ignore */ }
        try { _stopCts.Cancel(); } catch { /* ignore */ }

        _workerLock.Dispose();
        _stopCts.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (_stopping)
            return;

        try
        {
            if (!IsEligibleEpisode(e.Item))
                return;

            _pendingItemIds[e.Item.Id] = 0;
            ScheduleWorker(ItemChangedDebounce, "ItemChanged");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[AniLiberty] Media segment warmup item handler failure ignored.");
        }
    }

    private void OnScanCompleted(object? sender, EventArgs e)
    {
        RequestFullPass(ScanCompletedDebounce, "ScanCompleted");
    }

    private void OnScanCompletedGeneric<T>(object? sender, T e) where T : EventArgs
    {
        OnScanCompleted(sender, e);
    }

    private void OnMediaSegmentStateSaved(string rootPath, int entryCount)
    {
        if (_stopping)
            return;

        _mediaSegmentIndex.InvalidateRoot(rootPath);
        foreach (var retry in _nextRetryUtcByItemId)
        {
            if (string.Equals(retry.Value.RootPath, rootPath, StringComparison.OrdinalIgnoreCase))
                _nextRetryUtcByItemId.TryRemove(retry.Key, out _);
        }
        if (entryCount <= 0)
            return;
        _logger.LogInformation(
            "[AniLiberty] Media segment state saved. Queuing warmup. root={RootPath} entries={EntryCount}",
            rootPath,
            entryCount);
        RequestFullPass(TimeSpan.FromSeconds(5), "MediaSegmentStateSaved", rootPath);
    }

    private void ScanPollTick()
    {
        if (_stopping)
            return;

        bool nowRunning;
        try
        {
            nowRunning = SafeIsScanRunning();
        }
        catch
        {
            return;
        }

        if (_wasScanRunning && !nowRunning)
            RequestFullPass(ScanCompletedDebounce, "ScanCompleted(poll)");

        _wasScanRunning = nowRunning;
    }

    private bool SafeIsScanRunning()
    {
        try
        {
            return _libraryManager.IsScanRunning;
        }
        catch
        {
            return false;
        }
    }

    private void RequestFullPass(TimeSpan delay, string reason, string? rootPath = null)
    {
        if (_stopping)
            return;

        lock (_fullPassLock)
        {
            _fullPassRootPath = (_fullPassRequested || _fullPassInProgress) &&
                                !string.Equals(_fullPassRootPath, rootPath, StringComparison.OrdinalIgnoreCase)
                ? null
                : rootPath;
            _fullPassStartIndex = 0;
            _fullPassVersion++;
            _fullPassRequested = true;
        }

        ScheduleWorker(delay, reason);
    }

    private void ScheduleWorker(TimeSpan delay, string reason)
    {
        if (_stopping)
            return;

        lock (_timerLock)
        {
            _lastReason = reason;

            if (_debounceTimer == null)
            {
                _debounceTimer = new Timer(
                    _ => WarmupWorkerAsync().ConfigureAwait(false),
                    null,
                    delay,
                    Timeout.InfiniteTimeSpan);
            }
            else
            {
                _debounceTimer.Change(delay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private async Task WarmupWorkerAsync()
    {
        if (_stopping)
            return;

        if (!await _workerLock.WaitAsync(0).ConfigureAwait(false))
            return;

        var reason = _lastReason;

        try
        {
            while (!_stopping)
            {
                var request = TryBeginFullPass();
                var batch = request is null
                    ? default
                    : await EnqueueMissingSegmentItemsAsync(reason, request.Value, _stopCts.Token).ConfigureAwait(false);

                var result = await ProcessPendingItemsAsync(reason, _stopCts.Token).ConfigureAwait(false);
                if (request is null)
                    break;

                var continueFullPass =
                    !batch.ReachedEnd &&
                    batch.EnqueuedCount >= MaxMissingItemsPerFullPass &&
                    result.SkippedNoState == 0;
                CompleteFullPassBatch(request.Value, batch, continueFullPass);
                if (!continueFullPass)
                    break;

                if (!reason.EndsWith("(next-batch)", StringComparison.Ordinal))
                    reason += "(next-batch)";
            }
        }
        catch (OperationCanceledException) when (_stopping)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AniLiberty] Media segment warmup failed. reason={Reason}", reason);
        }
        finally
        {
            lock (_fullPassLock)
                _fullPassInProgress = false;
            _workerLock.Release();
        }

        if ((_fullPassRequested || !_pendingItemIds.IsEmpty) && !_stopping)
            ScheduleWorker(TimeSpan.FromSeconds(5), "PendingMediaSegments");
    }

    private FullPassRequest? TryBeginFullPass()
    {
        lock (_fullPassLock)
        {
            if (!_fullPassRequested)
                return null;

            _fullPassRequested = false;
            _fullPassInProgress = true;
            return new FullPassRequest(_fullPassVersion, _fullPassStartIndex, _fullPassRootPath);
        }
    }

    private void CompleteFullPassBatch(
        FullPassRequest request,
        FullPassBatch batch,
        bool continueFullPass)
    {
        lock (_fullPassLock)
        {
            _fullPassInProgress = false;
            if (request.Version != _fullPassVersion)
                return;

            _fullPassStartIndex = batch.ReachedEnd ? 0 : batch.NextStartIndex;
            if (continueFullPass)
                _fullPassRequested = true;
        }
    }

    private async Task<FullPassBatch> EnqueueMissingSegmentItemsAsync(
        string reason,
        FullPassRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var nextStartIndex = Math.Max(0, request.StartIndex);
        var enqueuedCount = 0;
        var reachedEnd = false;
        var roots = request.RootPath is null
            ? AniLibertyMediaSegmentState.EnumerateConfiguredRoots().Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : new[] { request.RootPath };
        var snapshots = new Dictionary<string, AniLibertyMediaSegmentSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var snapshot = await _mediaSegmentIndex.GetSnapshotForRootAsync(root, cancellationToken).ConfigureAwait(false);
            if (snapshot is { Count: > 0 })
                snapshots[root] = snapshot;
        }
        if (snapshots.Count == 0)
            return new FullPassBatch(0, 0, true);

        var ancestorIds = ResolveRootAncestorIds(snapshots.Keys.ToArray());

        while (!_stopping && enqueuedCount < MaxMissingItemsPerFullPass)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = _libraryManager.GetItemList(CreateMissingItemsQuery(nextStartIndex, ancestorIds));
            if (page.Count == 0)
            {
                reachedEnd = true;
                break;
            }

            foreach (var item in page)
            {
                nextStartIndex++;
                if (item is not Episode episode ||
                    !IsEligibleEpisode(episode) ||
                    !CanRetryMissingItem(episode.Id, now))
                {
                    continue;
                }

                if (!AniLibertyMediaSegmentState.TryResolveStatePath(episode.Path, out var root, out _) ||
                    !snapshots.TryGetValue(root, out var snapshot) || !snapshot.TryGetEntry(episode.Path, out _) ||
                    _mediaSegmentManager.HasSegments(episode.Id))
                    continue;

                _pendingItemIds[episode.Id] = 0;
                enqueuedCount++;
                if (enqueuedCount >= MaxMissingItemsPerFullPass)
                    break;
            }

            if (page.Count < MaxMissingItemsPerFullPass && enqueuedCount < MaxMissingItemsPerFullPass)
            {
                reachedEnd = true;
                break;
            }
        }

        if (enqueuedCount > 0)
        {
            _logger.LogInformation(
                "[AniLiberty] Queued media segment warmup for {Count} episode item(s). reason={Reason}",
                enqueuedCount,
                reason);
        }

        return new FullPassBatch(enqueuedCount, nextStartIndex, reachedEnd);
    }

    private Guid[] ResolveRootAncestorIds(string[] roots)
    {
        var ids = new List<Guid>();
        foreach (var root in roots)
        {
            var folders = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Path = root,
                IsFolder = true,
                EnableTotalRecordCount = false
            });
            // A configured path may not itself be an imported Folder (e.g. merged libraries).
            // Retain the existing repair traversal in that case, still filtering source entries
            // before any segment database access.
            if (folders.Count == 0)
                return Array.Empty<Guid>();
            ids.AddRange(folders.Select(x => x.Id));
        }
        return ids.Distinct().ToArray();
    }

    internal static InternalItemsQuery CreateMissingItemsQuery(int startIndex, Guid[]? ancestorIds = null)
    {
        return new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            Recursive = true,
            AncestorIds = ancestorIds ?? Array.Empty<Guid>(),
            StartIndex = Math.Max(0, startIndex),
            Limit = MaxMissingItemsPerFullPass,
            EnableTotalRecordCount = false,
            OrderBy = new[]
            {
                (ItemSortBy.SortName, SortOrder.Ascending),
                (ItemSortBy.DateCreated, SortOrder.Ascending)
            }
        };
    }

    private async Task<WarmupResult> ProcessPendingItemsAsync(string reason, CancellationToken cancellationToken)
    {
        var ids = _pendingItemIds.Keys.ToArray();
        if (ids.Length == 0)
            return default;

        var options = BuildAniLibertySegmentOptions();
        var processed = 0;
        var created = 0;
        var skippedNoState = 0;
        var skippedNoEntry = 0;

        foreach (var itemId in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_pendingItemIds.TryRemove(itemId, out _))
                continue;

            if (!CanRetryMissingItem(itemId, DateTime.UtcNow))
                continue;

            var item = _libraryManager.GetItemById(itemId);
            if (item is not Episode episode || !IsEligibleEpisode(episode))
                continue;

            var snapshot = await _mediaSegmentIndex
                .GetSnapshotForPathAsync(episode.Path, cancellationToken)
                .ConfigureAwait(false);
            if (snapshot is null)
            {
                skippedNoState++;
                continue;
            }

            if (!snapshot.TryGetEntry(episode.Path, out var entry) || entry.Segments.Length == 0)
            {
                skippedNoEntry++;
                continue;
            }

            if (_mediaSegmentManager.HasSegments(itemId))
            {
                _nextRetryUtcByItemId.TryRemove(itemId, out _);
                continue;
            }

            processed++;

            await _mediaSegmentManager
                .RunSegmentPluginProviders(episode, options, forceOverwrite: false, cancellationToken)
                .ConfigureAwait(false);

            if (_mediaSegmentManager.HasSegments(itemId))
            {
                _nextRetryUtcByItemId.TryRemove(itemId, out _);
                created++;
            }
            else
            {
                _nextRetryUtcByItemId[itemId] = new RetryState(
                    DateTime.UtcNow.Add(MissingSegmentRetryDelay), snapshot.RootPath);
            }
        }

        if (processed > 0 || skippedNoState > 0 || skippedNoEntry > 0)
        {
            _logger.LogInformation(
                "[AniLiberty] Media segment warmup finished. processed={Processed} created={Created} skippedNoState={SkippedNoState} skippedNoEntry={SkippedNoEntry} reason={Reason}",
                processed,
                created,
                skippedNoState,
                skippedNoEntry,
                reason);
        }

        return new WarmupResult(processed, created, skippedNoState, skippedNoEntry);
    }

    private static LibraryOptions BuildAniLibertySegmentOptions()
    {
        return new LibraryOptions
        {
            MediaSegmentProviderOrder = new[] { AniLibertyMediaSegmentProvider.ProviderName },
            DisabledMediaSegmentProviders = new[] { "Intro Skipper" }
        };
    }

    private bool CanRetryMissingItem(Guid itemId, DateTime nowUtc)
    {
        return !_nextRetryUtcByItemId.TryGetValue(itemId, out var retry) || retry.NextRetryUtc <= nowUtc;
    }

    private static bool IsEligibleEpisode(BaseItem? item)
    {
        return item is Episode ep &&
               !string.IsNullOrWhiteSpace(ep.Path) &&
               ep.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase) &&
               AniLibertyMediaSegmentState.IsConfiguredAniLibertyPath(ep.Path);
    }

    private void TryHookScanCompletedEvent()
    {
        var names = new[] { "ScanCompleted", "LibraryScanCompleted" };
        var type = _libraryManager.GetType();

        foreach (var name in names)
        {
            try
            {
                var ev = type.GetEvent(name, BindingFlags.Instance | BindingFlags.Public);
                var handlerType = ev?.EventHandlerType;
                if (ev == null || handlerType == null)
                    continue;

                var invoke = handlerType.GetMethod("Invoke");
                var parameters = invoke?.GetParameters();
                if (parameters == null ||
                    parameters.Length != 2 ||
                    !typeof(EventArgs).IsAssignableFrom(parameters[1].ParameterType))
                {
                    continue;
                }

                MethodInfo method;
                if (parameters[1].ParameterType == typeof(EventArgs))
                {
                    method = GetType().GetMethod(nameof(OnScanCompleted), BindingFlags.Instance | BindingFlags.NonPublic)
                             ?? throw new MissingMethodException(nameof(OnScanCompleted));
                }
                else
                {
                    var generic = GetType().GetMethod(nameof(OnScanCompletedGeneric), BindingFlags.Instance | BindingFlags.NonPublic)
                                  ?? throw new MissingMethodException(nameof(OnScanCompletedGeneric));
                    method = generic.MakeGenericMethod(parameters[1].ParameterType);
                }

                var del = Delegate.CreateDelegate(handlerType, this, method);
                ev.AddEventHandler(_libraryManager, del);

                _scanCompletedEvent = ev;
                _scanCompletedHandler = del;
                _logger.LogInformation("[AniLiberty] Hooked library event for media segment warmup: {EventName}", name);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[AniLiberty] Failed to hook {EventName} for media segment warmup.", name);
            }
        }

        _logger.LogInformation("[AniLiberty] ScanCompleted event not found for media segment warmup; using polling fallback.");
    }

    private void UnhookScanCompletedEvent()
    {
        try
        {
            if (_scanCompletedEvent != null && _scanCompletedHandler != null)
            {
                _scanCompletedEvent.RemoveEventHandler(_libraryManager, _scanCompletedHandler);
                _logger.LogInformation("[AniLiberty] Unhooked library ScanCompleted event for media segment warmup.");
            }
        }
        catch
        {
            // Ignore shutdown cleanup failures.
        }
        finally
        {
            _scanCompletedEvent = null;
            _scanCompletedHandler = null;
        }
    }

    private readonly record struct WarmupResult(
        int Processed,
        int Created,
        int SkippedNoState,
        int SkippedNoEntry);

    private readonly record struct RetryState(DateTime NextRetryUtc, string RootPath);

    private readonly record struct FullPassRequest(int Version, int StartIndex, string? RootPath);

    private readonly record struct FullPassBatch(
        int EnqueuedCount,
        int NextStartIndex,
        bool ReachedEnd);
}

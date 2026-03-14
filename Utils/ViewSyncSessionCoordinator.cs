using System.Collections.Concurrent;

namespace AniLibertyStrmPlugin.Utils;

internal sealed class ViewSyncSessionCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _lastSentBySession =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();

    public bool IsStopping => _shutdown.IsCancellationRequested;

    public async Task<bool> TrySendAsync(
        string sessionKey,
        long positionSeconds,
        int minDelta,
        bool isStopEvent,
        Func<CancellationToken, Task<bool>> sendAsync)
    {
        var gate = _gates.GetOrAdd(sessionKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(_shutdown.Token);

        try
        {
            if (!isStopEvent &&
                _lastSentBySession.TryGetValue(sessionKey, out var lastSeconds) &&
                positionSeconds < lastSeconds + minDelta)
                return false;

            var ok = await sendAsync(_shutdown.Token);
            if (!ok)
                return false;

            _lastSentBySession[sessionKey] = positionSeconds;
            if (isStopEvent)
                _lastSentBySession.TryRemove(sessionKey, out _);

            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public void CancelPending()
    {
        if (!_shutdown.IsCancellationRequested)
            _shutdown.Cancel();
    }
}

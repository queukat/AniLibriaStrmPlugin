using System.Collections.Concurrent;
using AniLibertyStrmPlugin.Models;
using AniLibertyStrmPlugin.Utils;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin;

internal sealed class AniLibertyViewSyncHostedService : IHostedService, IDisposable
{
    private readonly IAniLibertyClient _client;
    private readonly ILogger<AniLibertyViewSyncHostedService> _log;
    private readonly ISessionManager _sessionManager;
    private readonly ViewSyncSessionCoordinator _sessionCoordinator = new();
    private int _isSubscribed;

    public AniLibertyViewSyncHostedService(
        ISessionManager sessionManager,
        IAniLibertyClient client,
        ILogger<AniLibertyViewSyncHostedService> log)
    {
        _sessionManager = sessionManager;
        _client = client;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Subscribe();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        _sessionCoordinator.CancelPending();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Unsubscribe();
        _sessionCoordinator.CancelPending();
    }

    private void Subscribe()
    {
        if (Interlocked.Exchange(ref _isSubscribed, 1) == 1)
            return;

        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _log.Info("[WATCH-SYNC] Playback event listeners attached.");
    }

    private void Unsubscribe()
    {
        if (Interlocked.Exchange(ref _isSubscribed, 0) == 0)
            return;

        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _log.Info("[WATCH-SYNC] Playback event listeners detached.");
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs args)
    {
        if (args.IsPaused)
            return;

        _ = HandleSyncAsync(args, playedToCompletion: false, isStopEvent: false);
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs args)
    {
        _ = HandleSyncAsync(args, args.PlayedToCompletion, isStopEvent: true);
    }

    private async Task HandleSyncAsync(PlaybackProgressEventArgs args, bool playedToCompletion, bool isStopEvent)
    {
        try
        {
            var cfg = Plugin.Instance?.Configuration;
            if (cfg is null || !cfg.EnableAniLibertyViewSync)
                return;

            var token = cfg.AniLibertyToken;
            if (string.IsNullOrWhiteSpace(token))
                return;

            if (isStopEvent && !cfg.AniLibertyViewSyncOnStop)
                return;

            var path = args.Item?.Path;
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                return;

            if (!AniLibertyEpisodeIdResolver.TryResolveFromStrmPath(path, out var releaseEpisodeId))
                return;

            var positionSeconds = (long)Math.Max(0,
                Math.Round((args.PlaybackPositionTicks ?? 0) / (double)TimeSpan.TicksPerSecond));

            if (!isStopEvent && positionSeconds < 5)
                return;

            var sessionKey = $"{releaseEpisodeId}|{args.PlaySessionId ?? "-"}";
            var minDelta = Math.Clamp(cfg.AniLibertyViewSyncMinDeltaSeconds, 5, 600);
            var isWatched = playedToCompletion;
            if (!isWatched && args.Item?.RunTimeTicks is long runtimeTicks && runtimeTicks > 0)
            {
                var runtimeSec = runtimeTicks / (double)TimeSpan.TicksPerSecond;
                if (runtimeSec > 0 && positionSeconds >= runtimeSec * 0.92)
                    isWatched = true;
            }

            var payload = new[]
            {
                new ViewTimecodeUpdateItem
                {
                    ReleaseEpisodeId = releaseEpisodeId,
                    Time = positionSeconds,
                    IsWatched = isWatched
                }
            };

            var sent = await _sessionCoordinator.TrySendAsync(
                sessionKey,
                positionSeconds,
                minDelta,
                isStopEvent,
                ct => _client.UpdateViewTimecodesAsync(token, payload, ct));
            if (!sent)
                return;

            _log.Debug("[WATCH-SYNC] Sent: epId={0}, time={1}s, watched={2}, stop={3}",
                releaseEpisodeId,
                positionSeconds,
                isWatched ? "yes" : "no",
                isStopEvent ? "yes" : "no");
        }
        catch (OperationCanceledException) when (_sessionCoordinator.IsStopping)
        {
            // Service is shutting down; suppress noisy warnings from canceled in-flight sync.
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[WATCH-SYNC] Failed to push playback progress.");
        }
    }
}

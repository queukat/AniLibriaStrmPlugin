using System.Collections.Concurrent;
using AniLibertyStrmPlugin.Configuration;
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
    private readonly IAniLibertyAuthNotificationService _authNotifications;
    private readonly ILogger<AniLibertyViewSyncHostedService> _log;
    private readonly ISessionManager _sessionManager;
    private readonly ViewSyncSessionCoordinator _sessionCoordinator = new();
    private int _isSubscribed;

    public AniLibertyViewSyncHostedService(
        ISessionManager sessionManager,
        IAniLibertyClient client,
        ILogger<AniLibertyViewSyncHostedService> log,
        IAniLibertyAuthNotificationService? authNotifications = null)
    {
        _sessionManager = sessionManager;
        _client = client;
        _log = log;
        _authNotifications = authNotifications ?? NullAniLibertyAuthNotificationService.Instance;
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
        _sessionCoordinator.Dispose();
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
        => await HandleSyncAsync(
            args,
            Plugin.Instance?.Configuration,
            playedToCompletion,
            isStopEvent);

    internal async Task HandleSyncAsync(
        PlaybackProgressEventArgs args,
        PluginConfiguration? cfg,
        bool playedToCompletion,
        bool isStopEvent)
    {
        try
        {
            if (!TryCreateUpdate(args, cfg, playedToCompletion, isStopEvent, out var update))
                return;

            var payload = new[]
            {
                new ViewTimecodeUpdateItem
                {
                    ReleaseEpisodeId = update.ReleaseEpisodeId,
                    Time = update.PositionSeconds,
                    IsWatched = update.Watched
                }
            };

            var sent = await _sessionCoordinator.TrySendAsync(
                update.SessionKey,
                update.PositionSeconds,
                update.MinDeltaSeconds,
                update.IsStopEvent,
                ct => _client.UpdateViewTimecodesAsync(update.Token, payload, ct));
            if (!sent)
                return;

            _log.Debug("[WATCH-SYNC] Sent: epId={0}, time={1}s, watched={2}, stop={3}",
                update.ReleaseEpisodeId,
                update.PositionSeconds,
                update.Watched ? "yes" : "no",
                update.IsStopEvent ? "yes" : "no");
        }
        catch (OperationCanceledException) when (_sessionCoordinator.IsStopping)
        {
            // Service is shutting down; suppress noisy warnings from canceled in-flight sync.
        }
        catch (AniLibertyAuthExpiredException ex)
        {
            _log.Warn(ex, "[WATCH-SYNC] AniLiberty authorization expired while pushing playback progress.");
            await _authNotifications.NotifyAuthExpiredAsync("Watch-progress push sync", ex, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[WATCH-SYNC] Failed to push playback progress.");
        }
    }

    internal static bool TryCreateUpdate(
        PlaybackProgressEventArgs args,
        PluginConfiguration? cfg,
        bool playedToCompletion,
        bool isStopEvent,
        out ViewSyncUpdate update)
    {
        update = default;
        if (cfg is null || !cfg.EnableAniLibertyViewSync)
            return false;

        var token = cfg.AniLibertyToken;
        if (string.IsNullOrWhiteSpace(token) || isStopEvent && !cfg.AniLibertyViewSyncOnStop)
            return false;

        if (!TryResolveEpisode(args, out var releaseEpisodeId))
            return false;

        var positionSeconds = GetPositionSeconds(args);
        if (!isStopEvent && positionSeconds < 5)
            return false;

        update = new ViewSyncUpdate(
            token,
            releaseEpisodeId,
            $"{releaseEpisodeId}|{args.PlaySessionId ?? "-"}",
            positionSeconds,
            Math.Clamp(cfg.AniLibertyViewSyncMinDeltaSeconds, 5, 600),
            IsWatched(args, playedToCompletion, positionSeconds),
            isStopEvent);
        return true;
    }

    internal static bool TryResolveEpisode(PlaybackProgressEventArgs args, out string releaseEpisodeId)
    {
        releaseEpisodeId = string.Empty;
        var path = args.Item?.Path;
        return !string.IsNullOrWhiteSpace(path) &&
               path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase) &&
               AniLibertyEpisodeIdResolver.TryResolveFromStrmPath(path, out releaseEpisodeId);
    }

    internal static long GetPositionSeconds(PlaybackProgressEventArgs args)
    {
        return (long)Math.Max(0,
            Math.Round((args.PlaybackPositionTicks ?? 0) / (double)TimeSpan.TicksPerSecond));
    }

    internal static bool IsWatched(PlaybackProgressEventArgs args, bool playedToCompletion, long positionSeconds)
    {
        if (playedToCompletion)
            return true;

        if (args.Item?.RunTimeTicks is not long runtimeTicks || runtimeTicks <= 0)
            return false;

        var runtimeSec = runtimeTicks / (double)TimeSpan.TicksPerSecond;
        return runtimeSec > 0 && positionSeconds >= runtimeSec * 0.92;
    }

    internal readonly record struct ViewSyncUpdate(
        string Token,
        string ReleaseEpisodeId,
        string SessionKey,
        long PositionSeconds,
        int MinDeltaSeconds,
        bool Watched,
        bool IsStopEvent);
}

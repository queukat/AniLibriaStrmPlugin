using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AniLibertyStrmPlugin.Configuration;
using AniLibertyStrmPlugin.Models;
using AniLibertyStrmPlugin.Utils;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class AniLibertyViewSyncHostedServiceTests
{
    [Fact]
    public async Task HandleSyncAsync_SendsPreparedUpdateThroughClient()
    {
        using var files = TempPlaybackFiles.CreateWithSidecar("41414141-4141-4141-4141-414141414141");
        var client = new RecordingClient();
        var service = new AniLibertyViewSyncHostedService(
            sessionManager: null!,
            client,
            NullLogger<AniLibertyViewSyncHostedService>.Instance);

        await service.HandleSyncAsync(
            PlaybackArgs(files.StrmPath, TimeSpan.FromSeconds(45).Ticks, TimeSpan.FromMinutes(20).Ticks),
            EnabledConfig(),
            playedToCompletion: false,
            isStopEvent: false);
        service.Dispose();

        Assert.Equal("token-value", client.Token);
        var update = Assert.Single(client.Updates);
        Assert.Equal("41414141-4141-4141-4141-414141414141", update.ReleaseEpisodeId);
        Assert.Equal(45, update.Time);
        Assert.False(update.IsWatched);
    }

    [Fact]
    public async Task HandleSyncAsync_NotifiesWhenAniLibertyAuthExpired()
    {
        using var files = TempPlaybackFiles.CreateWithSidecar("42424242-4242-4242-4242-424242424242");
        var client = new RecordingClient { ThrowAuthExpired = true };
        var notifications = new RecordingAuthNotifications();
        var service = new AniLibertyViewSyncHostedService(
            sessionManager: null!,
            client,
            NullLogger<AniLibertyViewSyncHostedService>.Instance,
            notifications);

        await service.HandleSyncAsync(
            PlaybackArgs(files.StrmPath, TimeSpan.FromSeconds(45).Ticks, TimeSpan.FromMinutes(20).Ticks),
            EnabledConfig(),
            playedToCompletion: false,
            isStopEvent: false);
        service.Dispose();

        var notification = Assert.Single(notifications.Notifications);
        Assert.Equal("Watch-progress push sync", notification.Source);
    }


    [Fact]
    public void TryCreateUpdate_BuildsPayloadReadyUpdateForValidStrmProgress()
    {
        using var files = TempPlaybackFiles.CreateWithSidecar("44444444-4444-4444-4444-444444444444");
        var cfg = EnabledConfig();
        cfg.AniLibertyViewSyncMinDeltaSeconds = 1;
        var args = PlaybackArgs(files.StrmPath, TimeSpan.FromSeconds(552).Ticks, TimeSpan.FromMinutes(10).Ticks, playSessionId: null);

        var ok = AniLibertyViewSyncHostedService.TryCreateUpdate(
            args,
            cfg,
            playedToCompletion: false,
            isStopEvent: false,
            out var update);

        Assert.True(ok);
        Assert.Equal("token-value", update.Token);
        Assert.Equal("44444444-4444-4444-4444-444444444444", update.ReleaseEpisodeId);
        Assert.Equal("44444444-4444-4444-4444-444444444444|-", update.SessionKey);
        Assert.Equal(552, update.PositionSeconds);
        Assert.Equal(5, update.MinDeltaSeconds);
        Assert.True(update.Watched);
        Assert.False(update.IsStopEvent);
    }

    [Fact]
    public void TryCreateUpdate_RejectsDisabledInvalidOrTooEarlyProgress()
    {
        using var files = TempPlaybackFiles.CreateWithSidecar("55555555-5555-5555-5555-555555555555");

        Assert.False(AniLibertyViewSyncHostedService.TryCreateUpdate(
            PlaybackArgs(files.StrmPath, TimeSpan.FromSeconds(10).Ticks, TimeSpan.FromMinutes(20).Ticks),
            cfg: null,
            playedToCompletion: false,
            isStopEvent: false,
            out _));

        var disabled = EnabledConfig();
        disabled.EnableAniLibertyViewSync = false;
        Assert.False(AniLibertyViewSyncHostedService.TryCreateUpdate(
            PlaybackArgs(files.StrmPath, TimeSpan.FromSeconds(10).Ticks, TimeSpan.FromMinutes(20).Ticks),
            disabled,
            playedToCompletion: false,
            isStopEvent: false,
            out _));

        var blankToken = EnabledConfig();
        blankToken.AniLibertyToken = " ";
        Assert.False(AniLibertyViewSyncHostedService.TryCreateUpdate(
            PlaybackArgs(files.StrmPath, TimeSpan.FromSeconds(10).Ticks, TimeSpan.FromMinutes(20).Ticks),
            blankToken,
            playedToCompletion: false,
            isStopEvent: false,
            out _));

        var stopDisabled = EnabledConfig();
        stopDisabled.AniLibertyViewSyncOnStop = false;
        Assert.False(AniLibertyViewSyncHostedService.TryCreateUpdate(
            PlaybackArgs(files.StrmPath, TimeSpan.FromSeconds(10).Ticks, TimeSpan.FromMinutes(20).Ticks),
            stopDisabled,
            playedToCompletion: false,
            isStopEvent: true,
            out _));

        Assert.False(AniLibertyViewSyncHostedService.TryCreateUpdate(
            PlaybackArgs(Path.ChangeExtension(files.StrmPath, ".mkv"), TimeSpan.FromSeconds(10).Ticks, TimeSpan.FromMinutes(20).Ticks),
            EnabledConfig(),
            playedToCompletion: false,
            isStopEvent: false,
            out _));

        Assert.False(AniLibertyViewSyncHostedService.TryCreateUpdate(
            PlaybackArgs(files.StrmPath, TimeSpan.FromSeconds(4).Ticks, TimeSpan.FromMinutes(20).Ticks),
            EnabledConfig(),
            playedToCompletion: false,
            isStopEvent: false,
            out _));
    }

    [Fact]
    public void TryResolveEpisodeAndWatchMath_HandleNfoFallbackAndBoundaryCases()
    {
        using var files = TempPlaybackFiles.CreateWithNfo("66666666-6666-6666-6666-666666666666");
        var args = PlaybackArgs(files.StrmPath, TimeSpan.FromSeconds(-2).Ticks, TimeSpan.FromMinutes(10).Ticks);

        Assert.True(AniLibertyViewSyncHostedService.TryResolveEpisode(args, out var episodeId));
        Assert.Equal("66666666-6666-6666-6666-666666666666", episodeId);
        Assert.Equal(0, AniLibertyViewSyncHostedService.GetPositionSeconds(args));
        Assert.True(AniLibertyViewSyncHostedService.IsWatched(args, playedToCompletion: true, positionSeconds: 0));
        Assert.False(AniLibertyViewSyncHostedService.IsWatched(
            PlaybackArgs(files.StrmPath, null, runtimeTicks: null),
            playedToCompletion: false,
            positionSeconds: 999));
    }

    private static PluginConfiguration EnabledConfig()
        => new()
        {
            EnableAniLibertyViewSync = true,
            AniLibertyToken = "token-value"
        };

    private static PlaybackProgressEventArgs PlaybackArgs(
        string path,
        long? positionTicks,
        long? runtimeTicks,
        string? playSessionId = "play-session")
        => new()
        {
            Item = new Video
            {
                Path = path,
                RunTimeTicks = runtimeTicks
            },
            PlaybackPositionTicks = positionTicks,
            PlaySessionId = playSessionId
        };

    private sealed class TempPlaybackFiles : IDisposable
    {
        private TempPlaybackFiles(string directory, string strmPath)
        {
            Directory = directory;
            StrmPath = strmPath;
        }

        public string Directory { get; }
        public string StrmPath { get; }

        public static TempPlaybackFiles CreateWithSidecar(string episodeId)
        {
            var files = Create();
            File.WriteAllText(AniLibertyEpisodeIdResolver.GetSidecarPath(files.StrmPath), episodeId);
            return files;
        }

        public static TempPlaybackFiles CreateWithNfo(string episodeId)
        {
            var files = Create();
            File.WriteAllText(Path.ChangeExtension(files.StrmPath, ".nfo"),
                $"""<episodedetails><uniqueid type="aniliberty_episode_id">{episodeId}</uniqueid></episodedetails>""");
            return files;
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }

        private static TempPlaybackFiles Create()
        {
            var dir = Path.Combine(Path.GetTempPath(), "alib-view-sync-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            var strmPath = Path.Combine(dir, "episode.strm");
            File.WriteAllText(strmPath, "https://example.test/master.m3u8");
            return new TempPlaybackFiles(dir, strmPath);
        }
    }

    private sealed class RecordingClient : IAniLibertyClient
    {
        public string Token { get; private set; } = string.Empty;
        public bool ThrowAuthExpired { get; init; }
        public List<ViewTimecodeUpdateItem> Updates { get; } = [];

        public Task<string> GetStringWithLoggingAsync(string url, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<string> GetStringAuthAsync(string url, string bearer, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<ReleaseResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<ReleaseResponse>> FetchFavoritesAsync(string bearerToken, int pageSize, int maxPages, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> UpdateViewTimecodesAsync(string bearerToken, IEnumerable<ViewTimecodeUpdateItem> updates, CancellationToken ct)
        {
            if (ThrowAuthExpired)
                throw new AniLibertyAuthExpiredException(System.Net.HttpStatusCode.Forbidden, "https://api.test/views", "{}");

            Token = bearerToken;
            Updates.AddRange(updates);
            return Task.FromResult(true);
        }

        public Task<List<ViewTimecodeEntry>> FetchViewTimecodesAsync(string bearerToken, DateTimeOffset? since, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<ReleaseResponse?> FetchReleaseByIdAsync(int id, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<FranchiseInfo>?> FetchFranchisesForReleaseAsync(int releaseId, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class RecordingAuthNotifications : IAniLibertyAuthNotificationService
    {
        public List<(string Source, AniLibertyAuthExpiredException Exception)> Notifications { get; } = [];

        public Task NotifyAuthExpiredAsync(
            string source,
            AniLibertyAuthExpiredException exception,
            CancellationToken cancellationToken)
        {
            Notifications.Add((source, exception));
            return Task.CompletedTask;
        }
    }
}

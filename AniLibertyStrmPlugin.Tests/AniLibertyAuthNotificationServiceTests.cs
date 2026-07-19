using System.Net;
using AniLibertyStrmPlugin.Configuration;
using Jellyfin.Data.Events;
using Jellyfin.Data.Queries;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class AniLibertyAuthNotificationServiceTests
{
    [Fact]
    public async Task NotifyAuthExpiredAsync_CreatesWarningActivityAndStoresCooldown()
    {
        using var host = PluginTestHost.Create();
        var activityManager = new RecordingActivityManager();
        var service = new AniLibertyAuthNotificationService(
            activityManager,
            NullLogger<AniLibertyAuthNotificationService>.Instance);
        var ex = new AniLibertyAuthExpiredException(HttpStatusCode.Forbidden, "https://api.test/me", """{"error":"expired"}""");

        await service.NotifyAuthExpiredAsync("Favorites catalogue update", ex, CancellationToken.None);

        var entry = Assert.Single(activityManager.Entries);
        Assert.Equal("AniLiberty authorization expired", entry.Name);
        Assert.Equal(AniLibertyAuthNotificationService.ActivityType, entry.Type);
        Assert.Equal(LogLevel.Warning, entry.LogSeverity);
        Assert.Contains("HTTP 403", entry.Overview, StringComparison.Ordinal);
        Assert.Contains("sign in again", entry.Overview, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(DateTime.MinValue, host.Plugin.Configuration.LastAniLibertyAuthExpiredNotificationUtc);
    }

    [Fact]
    public async Task NotifyAuthExpiredAsync_RespectsDisabledConfigAndCooldown()
    {
        using var disabledHost = PluginTestHost.Create(cfg => cfg.EnableAniLibertyAuthNotifications = false);
        var disabledActivityManager = new RecordingActivityManager();
        var disabledService = new AniLibertyAuthNotificationService(
            disabledActivityManager,
            NullLogger<AniLibertyAuthNotificationService>.Instance);
        var ex = new AniLibertyAuthExpiredException(HttpStatusCode.Unauthorized, "https://api.test/me", "{}");

        await disabledService.NotifyAuthExpiredAsync("Favorites", ex, CancellationToken.None);
        Assert.Empty(disabledActivityManager.Entries);

        using var cooldownHost = PluginTestHost.Create(cfg =>
        {
            cfg.EnableAniLibertyAuthNotifications = true;
            cfg.LastAniLibertyAuthExpiredNotificationUtc = DateTime.UtcNow;
        });
        var cooldownActivityManager = new RecordingActivityManager();
        var cooldownService = new AniLibertyAuthNotificationService(
            cooldownActivityManager,
            NullLogger<AniLibertyAuthNotificationService>.Instance);

        await cooldownService.NotifyAuthExpiredAsync("Favorites", ex, CancellationToken.None);
        Assert.Empty(cooldownActivityManager.Entries);
    }

    [Fact]
    public void ShouldSend_AllowsFirstAndPostCooldownNotifications()
    {
        var config = new PluginConfiguration();

        Assert.True(AniLibertyAuthNotificationService.ShouldSend(config, DateTime.UtcNow));

        config.LastAniLibertyAuthExpiredNotificationUtc = new DateTime(2026, 6, 17, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(AniLibertyAuthNotificationService.ShouldSend(config, new DateTime(2026, 6, 17, 6, 0, 0, DateTimeKind.Utc)));
        Assert.True(AniLibertyAuthNotificationService.ShouldSend(config, new DateTime(2026, 6, 17, 13, 0, 0, DateTimeKind.Utc)));
    }

    private sealed class RecordingActivityManager : IActivityManager
    {
        public event EventHandler<GenericEventArgs<MediaBrowser.Model.Activity.ActivityLogEntry>>? EntryCreated
        {
            add { }
            remove { }
        }

        public List<ActivityLog> Entries { get; } = [];

        public Task CreateAsync(ActivityLog entry)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<QueryResult<MediaBrowser.Model.Activity.ActivityLogEntry>> GetPagedResultAsync(ActivityLogQuery query)
            => Task.FromResult(new QueryResult<MediaBrowser.Model.Activity.ActivityLogEntry>());

        public Task CleanAsync(DateTime startDate)
            => Task.CompletedTask;
    }
}

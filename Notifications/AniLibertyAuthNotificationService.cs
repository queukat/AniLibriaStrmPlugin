using AniLibertyStrmPlugin.Configuration;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin;

public interface IAniLibertyAuthNotificationService
{
    Task NotifyAuthExpiredAsync(string source, AniLibertyAuthExpiredException exception, CancellationToken cancellationToken);
}

internal sealed class AniLibertyAuthNotificationService(
    IActivityManager activityManager,
    ILogger<AniLibertyAuthNotificationService> log)
    : IAniLibertyAuthNotificationService
{
    internal const string ActivityType = "AniLibertyAuthExpired";
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromHours(12);

    public async Task NotifyAuthExpiredAsync(
        string source,
        AniLibertyAuthExpiredException exception,
        CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        var cfg = plugin?.Configuration;
        if (plugin is null || cfg is null || !cfg.EnableAniLibertyAuthNotifications)
            return;

        var now = DateTime.UtcNow;
        if (!ShouldSend(cfg, now))
        {
            log.LogDebug("AniLiberty auth notification skipped due to cooldown.");
            return;
        }

        var entry = new ActivityLog(
            "AniLiberty authorization expired",
            ActivityType,
            Guid.Empty)
        {
            Overview = BuildOverview(source, exception),
            ShortOverview = "Open AniLiberty STRM Plugin settings and sign in again.",
            DateCreated = now,
            LogSeverity = LogLevel.Warning
        };

        await activityManager.CreateAsync(entry).ConfigureAwait(false);

        cfg.LastAniLibertyAuthExpiredNotificationUtc = now;
        plugin.UpdateConfiguration(cfg);
        plugin.AppendTaskLog("[AUTH] AniLiberty authorization expired. Sign in again in plugin settings.", LogLevel.Warning);
    }

    internal static bool ShouldSend(PluginConfiguration cfg, DateTime utcNow)
    {
        var last = cfg.LastAniLibertyAuthExpiredNotificationUtc;
        return last == DateTime.MinValue || utcNow - last.ToUniversalTime() >= DefaultCooldown;
    }

    private static string BuildOverview(string source, AniLibertyAuthExpiredException exception)
    {
        var safeSource = string.IsNullOrWhiteSpace(source) ? "AniLiberty API request" : source.Trim();
        return
            $"{safeSource} received HTTP {(int)exception.StatusCode} from AniLiberty. " +
            "The stored AniLiberty token is probably expired or revoked. " +
            "Open Dashboard -> Plugins -> AniLiberty STRM Plugin and sign in again.";
    }
}

internal sealed class NullAniLibertyAuthNotificationService : IAniLibertyAuthNotificationService
{
    public static NullAniLibertyAuthNotificationService Instance { get; } = new();

    private NullAniLibertyAuthNotificationService()
    {
    }

    public Task NotifyAuthExpiredAsync(
        string source,
        AniLibertyAuthExpiredException exception,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}

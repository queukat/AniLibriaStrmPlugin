using AniLibertyStrmPlugin.Utils;
using AniLibertyStrmPlugin.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Tasks;

public sealed class AniLibertyViewsPullTask(
    IAniLibertyClient client,
    ILibraryManager library,
    IUserDataManager userDataManager,
    IUserManager userManager,
    ILogger<AniLibertyViewsPullTask> log
) : IScheduledTask
{
    public bool IsHidden => false;
    public string Name => "Sync AniLiberty watch progress to Jellyfin";
    public string Category => "AniLiberty";
    public string Description => "Pulls AniLiberty view timecodes and imports them into Jellyfin user progress.";
    public string Key => "AniLibertyPullViewsToJellyfin";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => Array.Empty<TaskTriggerInfo>();

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken token)
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin instance is not initialized.");
        var cfg = plugin.Configuration;

        log.Info("=== AniLibertyViewsPullTask started ===");

        try
        {
            if (string.IsNullOrWhiteSpace(cfg.AniLibertyToken))
            {
                log.Warn("AniLiberty token is empty. Abort pull-sync.");
                return;
            }

            var user = ResolveTargetUser(cfg, userManager, log);
            if (user is null)
            {
                return;
            }

            var remote = await client.FetchViewTimecodesAsync(cfg.AniLibertyToken, since: null, token);
            if (remote.Count == 0)
            {
                log.Info("No remote timecodes returned.");
                return;
            }

            var map = ViewSyncPathMap.Build(cfg);
            log.Info("Remote timecodes: {0}; local episode map entries: {1}", remote.Count, map.Count);

            var applied = 0;
            var skippedNoMap = 0;
            var skippedNoItem = 0;
            var unchanged = 0;

            for (var i = 0; i < remote.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var row = remote[i];

                if (!map.TryGetValue(row.ReleaseEpisodeId, out var candidatePaths))
                {
                    skippedNoMap++;
                    progress.Report((i + 1) / (double)remote.Count * 100.0);
                    continue;
                }

                var item = ViewSyncPathMap.ResolveFirst(candidatePaths, path => library.FindByPath(path, false));
                if (item is null)
                {
                    skippedNoItem++;
                    progress.Report((i + 1) / (double)remote.Count * 100.0);
                    continue;
                }

                var userData = userDataManager.GetUserData(user, item);
                if (userData is null)
                {
                    skippedNoItem++;
                    progress.Report((i + 1) / (double)remote.Count * 100.0);
                    continue;
                }

                if (!ApplyTimecode(row, userData, item))
                {
                    unchanged++;
                    progress.Report((i + 1) / (double)remote.Count * 100.0);
                    continue;
                }

                userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.Import, token);
                applied++;

                progress.Report((i + 1) / (double)remote.Count * 100.0);
            }

            log.Info(
                "Pull-sync done: applied={0}, unchanged={1}, skippedNoMap={2}, skippedNoItem={3}",
                applied, unchanged, skippedNoMap, skippedNoItem);
        }
        catch (OperationCanceledException)
        {
            log.Warn("AniLibertyViewsPullTask canceled.");
            throw;
        }
        catch (Exception ex)
        {
            log.Err(ex, "AniLibertyViewsPullTask failed");
            throw;
        }
        finally
        {
            log.Info("=== AniLibertyViewsPullTask done ===");
            plugin.FlushLog();
        }
    }

    private static bool ApplyTimecode(ViewTimecodeEntry row, UserItemData userData, BaseItem item)
    {
        var changed = false;

        if (row.IsWatched)
        {
            if (!userData.Played)
                changed = true;
            if (userData.PlayCount < 1)
                changed = true;
            if (userData.PlaybackPositionTicks != 0)
                changed = true;

            userData.Played = true;
            userData.PlayCount = (byte)Math.Max(1, userData.PlayCount);
            userData.PlaybackPositionTicks = 0;
            userData.LastPlayedDate = DateTime.UtcNow;
            return changed;
        }

        // Do not downgrade watched -> unwatched.
        if (userData.Played)
            return false;

        var ticks = (long)Math.Max(0, Math.Round(row.Time * TimeSpan.TicksPerSecond));
        if (ticks <= userData.PlaybackPositionTicks)
            return false;

        if (userData.PlaybackPositionTicks != ticks)
        {
            userData.PlaybackPositionTicks = ticks;
            changed = true;
        }

        return changed;
    }

    private static Jellyfin.Database.Implementations.Entities.User? ResolveTargetUser(
        Configuration.PluginConfiguration cfg,
        IUserManager userManager,
        ILogger log)
    {
        if (!string.IsNullOrWhiteSpace(cfg.AniLibertyViewSyncJellyfinUserId))
        {
            if (!Guid.TryParse(cfg.AniLibertyViewSyncJellyfinUserId, out var configuredUserId))
            {
                log.Warn("AniLibertyViewSyncJellyfinUserId is invalid GUID.");
                return null;
            }

            var configuredUser = userManager.GetUserById(configuredUserId);
            if (configuredUser is null)
            {
                log.Warn("Jellyfin user not found by configured id={0}.", configuredUserId);
                return null;
            }

            return configuredUser;
        }

        var allUsers = userManager.Users?.ToList() ?? new List<Jellyfin.Database.Implementations.Entities.User>();
        if (allUsers.Count == 1)
        {
            var only = allUsers[0];
            log.Info("AniLibertyViewSyncJellyfinUserId is empty; auto-selected single Jellyfin user: {0} ({1})",
                only.Username, only.Id);
            return only;
        }

        if (allUsers.Count == 0)
        {
            log.Warn("No Jellyfin users found.");
            return null;
        }

        log.Warn(
            "AniLibertyViewSyncJellyfinUserId is empty and there are {0} Jellyfin users. Set explicit UserId in plugin settings.",
            allUsers.Count);
        return null;
    }
}

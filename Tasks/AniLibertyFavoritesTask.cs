// ===== File: AniLibertyFavoritesTask.cs (updated) =====

using AniLibertyStrmPlugin.Utils;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Tasks;

public sealed class AniLibertyFavoritesTask(
    IAniLibertyClient client,
    IAniLibertyStrmGenerator gen,
    ILogger<AniLibertyFavoritesTask> log,
    IAniLibertyAuthNotificationService? authNotifications = null
) : IScheduledTask
{
    private readonly bool _isHidden = false;
    private readonly IAniLibertyClient _client = client;
    private readonly IAniLibertyStrmGenerator _gen = gen;
    private readonly ILogger<AniLibertyFavoritesTask> _log = log;
    private readonly IAniLibertyAuthNotificationService _authNotifications =
        authNotifications ?? NullAniLibertyAuthNotificationService.Instance;

    public bool IsHidden => _isHidden;
    public string Name => "Generate AniLiberty STRM (Favorites Only)";
    public string Category => "AniLiberty";
    public string Description => "Fetches AniLiberty favorites and generates .strm + .nfo plus Jellyfin skip timings.";
    public string Key => "AniLibertyStrmFavoritesOnly";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>();
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin instance is not initialized.");
        var cfg = plugin.Configuration;

        _log.Info("=== AniLibertyFavoritesTask started ===");

        try
        {
            if (!cfg.EnableFavorites)
            {
                _log.Info("Favorites catalogue updates disabled — skipping task.");
                return;
            }

            if (string.IsNullOrWhiteSpace(cfg.AniLibertyToken))
            {
                const string userMessage = "No auth token – aborting.";

                _log.Warn(userMessage);
                throw new InvalidOperationException(userMessage);
            }

            if (string.IsNullOrWhiteSpace(cfg.StrmFavoritesPath))
            {
                _log.Info("StrmFavoritesPath is empty – nothing to do.");
                return;
            }

            await OutputRootPreflight.EnsureWritableAsync(
                cfg.StrmFavoritesPath,
                "Favorites STRM Path",
                _log,
                cancellationToken);

            _log.Info("Fetching favourites pageSize={0}, maxPages={1} …",
                cfg.FavoritesPageSize, cfg.FavoritesMaxPages);

            var titles = await _client.FetchFavoritesAsync(
                cfg.AniLibertyToken,
                cfg.FavoritesPageSize,
                cfg.FavoritesMaxPages,
                cancellationToken);

            _log.Info("Total favourites fetched: {0}", titles.Count);

            await _gen.GenerateTitlesAsync(
                titles,
                cfg.StrmFavoritesPath,
                cfg.PreferredResolution,
                progress,
                cancellationToken);

            FavoritesCache.Update(titles.Select(t => t.Id));
        }
        catch (OperationCanceledException)
        {
            _log.Warn("AniLibertyFavoritesTask canceled.");
            throw;
        }
        catch (AniLibertyAuthExpiredException ex)
        {
            _log.Warn(ex, "AniLiberty authorization expired while fetching favorites.");
            await _authNotifications.NotifyAuthExpiredAsync("Favorites catalogue update", ex, cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            _log.Err(ex, "AniLibertyFavoritesTask failed");
            throw;
        }
        finally
        {
            _log.Info("=== AniLibertyFavoritesTask done ===");
            plugin.FlushLog();
        }
    }
}

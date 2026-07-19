// ===== File: AniLibertyAllTask.cs =====

using AniLibertyStrmPlugin.Utils;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Tasks;

public sealed class AniLibertyAllTask : IScheduledTask
{
    private readonly bool _isHidden = false;
    private readonly IAniLibertyClient _client;
    private readonly IAniLibertyStrmGenerator _gen;
    private readonly ILogger<AniLibertyAllTask> _log;

    public AniLibertyAllTask(
        IAniLibertyClient client,
        IAniLibertyStrmGenerator gen,
        ILogger<AniLibertyAllTask> log)
    {
        _client = client;
        _gen = gen;
        _log = log;
    }

    public bool IsHidden => _isHidden;
    public string Name => "Generate AniLiberty STRM library";
    public string Category => "AniLiberty";
    public string Description => "Fetches *all* AniLiberty titles and generates .strm + .nfo plus Jellyfin skip timings.";
    public string Key => "AniLibertyStrmTask";

#if JF_10_10
// Jellyfin 10.10   TaskTriggerInfoType —   
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();
#else
    // Jellyfin 10.11+ uses the enum-based scheduler API.
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromDays(1).Ticks
        };
    }
#endif

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin instance is not initialized.");
        var cfg = plugin.Configuration;

        _log.Info("=== AniLibertyAllTask started ===");

        try
        {
            if (!cfg.EnableAll)
            {
                _log.Info("Global catalogue updates disabled — skipping AllTitles task.");
                return;
            }

            if (string.IsNullOrWhiteSpace(cfg.StrmAllPath))
            {
                _log.Info("StrmAllPath is empty – nothing to do.");
                return;
            }

            await OutputRootPreflight.EnsureWritableAsync(
                cfg.StrmAllPath,
                "All Titles STRM Path",
                _log,
                cancellationToken);

            _log.Info("Fetching full title list …");
            _log.Info("Full catalog fetch parameters: pageSize={0}, maxPages={1}",
                cfg.AllTitlesPageSize, cfg.AllTitlesMaxPages);

            var titles = await _client.FetchAllTitlesAsync(
                cfg.AllTitlesPageSize,
                cfg.AllTitlesMaxPages,
                cancellationToken);

            _log.Info("Titles fetched: {0}", titles.Count);

            await _gen.GenerateTitlesAsync(
                titles,
                cfg.StrmAllPath,
                cfg.PreferredResolution,
                progress,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _log.Warn("AniLibertyAllTask canceled.");
            throw;
        }
        catch (Exception ex)
        {
            _log.Err(ex, "AniLibertyAllTask failed");
            throw;
        }
        finally
        {
            _log.Info("=== AniLibertyAllTask done ===");
            plugin.FlushLog();
        }
    }
}

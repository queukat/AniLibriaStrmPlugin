// ===== File: AniLibriaAllTask.cs =====
using System.Text;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Tasks;


namespace AniLibriaStrmPlugin.Tasks
{
    public sealed class AniLibriaAllTask : IScheduledTask
    {
        private readonly IAniLibriaClient         _client;
        private readonly IAniLibriaStrmGenerator  _gen;
        private readonly ILogger<AniLibriaAllTask> _log;

        public AniLibriaAllTask(IAniLibriaClient client,
                                IAniLibriaStrmGenerator gen,
                                ILogger<AniLibriaAllTask> log)
        {
            _client = client;
            _gen    = gen;
            _log    = log;
        }

        public bool   IsHidden    => false;
        public string Name        => "Generate AniLibria STRM library";
        public string Category    => "AniLibria";
        public string Description => "Fetches *all* AniLibria titles and generates .strm + .edl + .nfo.";
        public string Key         => "AniLibriaStrmTask";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                
                IntervalTicks = TimeSpan.FromDays(1).Ticks
            };
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken token)
        {
            var cfg     = Plugin.Instance.Configuration;
            var logBuf  = new StringBuilder("=== AniLibriaAllTask started ===\n");

            try
            {
                if (cfg.TrackFavoritesOnly)
                {
                    logBuf.AppendLine("TrackFavoritesOnly=true  ➜  skipping AllTitles task.");
                    return;
                }

                if (string.IsNullOrEmpty(cfg.StrmAllPath))
                {
                    logBuf.AppendLine("StrmAllPath is empty ➜ nothing to do.");
                    return;
                }

                logBuf.AppendLine("Fetching full title list…");
                var titles = await _client.FetchAllTitlesAsync(cfg.AllTitlesPageSize,
                                                               cfg.AllTitlesMaxPages,
                                                               token);
                logBuf.AppendLine($"Titles fetched: {titles.Count}");

                await _gen.GenerateTitlesAsync(titles,
                                               cfg.StrmAllPath,
                                               cfg.PreferredResolution,
                                               progress,
                                               token);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "AniLibriaAllTask failed");
                logBuf.AppendLine("ERROR: " + ex);
            }
            finally
            {
                logBuf.AppendLine("=== AniLibriaAllTask done ===");
                FlushLog(logBuf.ToString());
            }
        }

        private static void FlushLog(string msg)
        {
            var cfg = Plugin.Instance.Configuration;
            cfg.LastTaskLog = msg;
            Plugin.Instance.UpdateConfiguration(cfg);
        }
    }
}

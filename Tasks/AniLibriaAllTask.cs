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
                Type          = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromDays(1).Ticks
            };
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken token)
        {
            var cfg    = Plugin.Instance.Configuration;
            _log.LogInformation("=== AniLibriaAllTask started ===");

            try
            {
                if (cfg.TrackFavoritesOnly)
                {
                    _log.LogInformation("TrackFavoritesOnly=true – skipping AllTitles task.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(cfg.StrmAllPath))
                {
                    _log.LogInformation("StrmAllPath is empty – nothing to do.");
                    return;
                }

                _log.LogInformation("Fetching full title list …");
                var titles = await _client.FetchAllTitlesAsync(cfg.AllTitlesPageSize,
                                                               cfg.AllTitlesMaxPages,
                                                               token);
                _log.LogInformation("Titles fetched: {Count}", titles.Count);

                await _gen.GenerateTitlesAsync(titles,
                                               cfg.StrmAllPath,
                                               cfg.PreferredResolution,
                                               progress,
                                               token);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "AniLibriaAllTask failed");
            }
            finally
            {
                _log.LogInformation("=== AniLibriaAllTask done ===");
                // Сохраняем накопленный буфер логов
                Plugin.Instance.FlushLog();
            }
        }
    }
}

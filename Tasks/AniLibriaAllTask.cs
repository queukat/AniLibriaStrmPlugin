// ===== File: AniLibriaAllTask.cs =====

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace AniLibriaStrmPlugin.Tasks
{
    public sealed class AniLibriaAllTask : IScheduledTask
    {
        private readonly IAniLibriaClient _client;
        private readonly IAniLibriaStrmGenerator _gen;
        private readonly ILogger<AniLibriaAllTask> _log;

        public AniLibriaAllTask(
            IAniLibriaClient client,
            IAniLibriaStrmGenerator gen,
            ILogger<AniLibriaAllTask> log)
        {
            _client = client;
            _gen = gen;
            _log = log;
        }

        public bool IsHidden => false;
        public string Name => "Generate AniLibria STRM library";
        public string Category => "AniLibria";
        public string Description => "Fetches *all* AniLibria titles and generates .strm + .edl + .nfo.";
        public string Key => "AniLibriaStrmTask";

#if JF_10_10
// Jellyfin 10.10 не поддерживает TaskTriggerInfoType — не возвращаем расписание
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();
#else
// Начиная с 10.11+ — возвращаем расписание с триггером раз в сутки
public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
{
    yield return new TaskTriggerInfo
    {
        Type = TaskTriggerInfoType.IntervalTrigger,
        IntervalTicks = TimeSpan.FromDays(1).Ticks
    };
}
#endif


        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken token)
        {
            var cfg = Plugin.Instance.Configuration;
            _log.LogInformation("=== AniLibriaAllTask started ===");

            try
            {
                if (!cfg.EnableAll)
                {
                    _log.LogInformation("Global catalogue updates disabled — skipping AllTitles task.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(cfg.StrmAllPath))
                {
                    _log.LogInformation("StrmAllPath is empty – nothing to do.");
                    return;
                }

                _log.LogInformation("Fetching full title list …");
                var titles = await _client.FetchAllTitlesAsync(
                    cfg.AllTitlesPageSize,
                    cfg.AllTitlesMaxPages,
                    token);

                _log.LogInformation("Titles fetched: {Count}", titles.Count);

                await _gen.GenerateTitlesAsync(
                    titles,
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
                Plugin.Instance.FlushLog();
            }
        }
    }
}
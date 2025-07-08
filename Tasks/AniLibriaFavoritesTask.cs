﻿// ===== File: AniLibriaFavoritesTask.cs =====

using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using AniLibriaStrmPlugin;               // ← ДОБАВЛЕНО
using AniLibriaStrmPlugin.Utils;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace AniLibriaStrmPlugin.Tasks
{
    public sealed class AniLibriaFavoritesTask : IScheduledTask
    {
        private readonly IAniLibriaClient _client;
        private readonly IAniLibriaStrmGenerator _gen;
        private readonly ILogger<AniLibriaFavoritesTask> _log;

        public AniLibriaFavoritesTask(
            IAniLibriaClient client,
            IAniLibriaStrmGenerator gen,
            ILogger<AniLibriaFavoritesTask> log)
        {
            _client = client;
            _gen    = gen;
            _log    = log;
        }

        public bool   IsHidden    => false;
        public string Name        => "Generate AniLibria STRM (Favorites Only)";
        public string Category    => "AniLibria";
        public string Description => "Fetches AniLibria favorites and generates .strm + .edl + .nfo.";
        public string Key         => "AniLibriaStrmFavoritesOnly";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken token)
        {
            var cfg = Plugin.Instance.Configuration;
            _log.Info("=== AniLibriaFavoritesTask started ===");

            try
            {
                if (!cfg.EnableFavorites)
                {
                    _log.LogInformation("Favorites catalogue updates disabled — skipping task.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(cfg.AniLibriaToken))
                {
                    _log.Warn("No auth token – aborting.");
                    return;
                }

                _log.Info("Fetching favourites pageSize={0} …", cfg.FavoritesPageSize);
                var titles = await _client.FetchFavoritesAsync(
                    cfg.AniLibriaToken,
                    cfg.FavoritesPageSize,
                    cfg.FavoritesMaxPages,
                    token);

                _log.Info("Total favourites fetched: {0}", titles.Count);

                await _gen.GenerateTitlesAsync(titles, cfg.StrmFavoritesPath,
                    cfg.PreferredResolution, progress, token);

                FavoritesCache.Update(titles.Select(t => t.Id));
            }
            catch (Exception ex)
            {
                _log.Err(ex, "AniLibriaFavoritesTask failed");
            }
            finally
            {
                _log.Info("=== AniLibriaFavoritesTask done ===");
                Plugin.Instance.FlushLog();
            }
        }
    }
}

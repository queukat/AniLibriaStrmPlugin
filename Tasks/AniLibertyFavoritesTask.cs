﻿// ===== File: AniLibertyFavoritesTask.cs =====

using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using AniLibertyStrmPlugin;               // ← 
using AniLibertyStrmPlugin.Utils;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Tasks
{
    public sealed class AniLibertyFavoritesTask : IScheduledTask
    {
        private readonly IAniLibertyClient _client;
        private readonly IAniLibertyStrmGenerator _gen;
        private readonly ILogger<AniLibertyFavoritesTask> _log;

        public AniLibertyFavoritesTask(
            IAniLibertyClient client,
            IAniLibertyStrmGenerator gen,
            ILogger<AniLibertyFavoritesTask> log)
        {
            _client = client;
            _gen    = gen;
            _log    = log;
        }

        public bool   IsHidden    => false;
        public string Name        => "Generate AniLiberty STRM (Favorites Only)";
        public string Category    => "AniLiberty";
        public string Description => "Fetches AniLiberty favorites and generates .strm + .edl + .nfo.";
        public string Key         => "AniLibertyStrmFavoritesOnly";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken token)
        {
            var cfg = Plugin.Instance.Configuration;
            _log.Info("=== AniLibertyFavoritesTask started ===");

            try
            {
                if (!cfg.EnableFavorites)
                {
                    _log.LogInformation("Favorites catalogue updates disabled — skipping task.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(cfg.AniLibertyToken))
                {
                    _log.Warn("No auth token – aborting.");
                    return;
                }

                _log.Info("Fetching favourites pageSize={0} …", cfg.FavoritesPageSize);
                var titles = await _client.FetchFavoritesAsync(
                    cfg.AniLibertyToken,
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
                _log.Err(ex, "AniLibertyFavoritesTask failed");
            }
            finally
            {
                _log.Info("=== AniLibertyFavoritesTask done ===");
                Plugin.Instance.FlushLog();
            }
        }
    }
}

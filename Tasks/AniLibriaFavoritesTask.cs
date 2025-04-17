// ===== File: AniLibriaFavoritesTask.cs =====
using System.Text;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace AniLibriaStrmPlugin.Tasks;

public sealed class AniLibriaFavoritesTask : IScheduledTask
{
    private readonly IAniLibriaClient        _client;
    private readonly IAniLibriaStrmGenerator _gen;
    private readonly ILogger<AniLibriaFavoritesTask> _log;

    public AniLibriaFavoritesTask(IAniLibriaClient client,
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

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() { yield break; }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken token)
    {
        var cfg = Plugin.Instance.Configuration;
        var sb  = new StringBuilder("=== AniLibriaFavoritesTask started ===\n");

        try
        {
            if (string.IsNullOrEmpty(cfg.AniLibriaSession) || string.IsNullOrEmpty(cfg.StrmFavoritesPath))
            {
                sb.AppendLine("No session or path => skip.");
                return;
            }

            sb.AppendLine("Fetching favorites …");
            var titles = await _client.FetchFavoritesAsync(cfg.AniLibriaSession,
                                                           cfg.FavoritesPageSize,
                                                           cfg.FavoritesMaxPages,
                                                           token);
            sb.AppendLine($"Favorites fetched: {titles.Count}");

            await _gen.GenerateTitlesAsync(titles, cfg.StrmFavoritesPath, cfg.PreferredResolution, progress, token);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AniLibriaFavoritesTask failed");
            sb.AppendLine("ERROR: " + ex);
        }
        finally
        {
            sb.AppendLine("=== AniLibriaFavoritesTask done ===");
            FlushLog(sb.ToString());
        }
    }

    private static void FlushLog(string msg)
    {
        var cfg = Plugin.Instance.Configuration;
        cfg.LastTaskLog = msg;
        Plugin.Instance.UpdateConfiguration(cfg);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AniLibriaStrmPlugin.Models;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace AniLibriaStrmPlugin
{
    // =====================================================================
    // 1)  AniLibriaRealtimeWatcher – exponential back‑off & injected client
    // =====================================================================
    public sealed class AniLibriaRealtimeWatcher : BackgroundService
    {
        private readonly ILogger<AniLibriaRealtimeWatcher> _log;
        private readonly IAniLibriaStrmGenerator _gen;
        private readonly IAniLibriaClient _client;
        private static readonly Random _rnd = new();

        public AniLibriaRealtimeWatcher(
            ILogger<AniLibriaRealtimeWatcher> log,
            IAniLibriaStrmGenerator gen,
            IAniLibriaClient client)
        {
            _log = log;
            _gen = gen;
            _client = client;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Plugin.Instance.Configuration.EnableRealtimeUpdates)
            {
                _log.LogInformation("Real‑time updates disabled.");
                return;
            }

            var uri = new Uri("wss://api.anilibria.tv/v3/ws/");
            var attempt = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    await ws.ConnectAsync(uri, stoppingToken);
                    _log.LogInformation("WS connected → {Uri}", uri);
                    attempt = 0; // reset back‑off

                    var buffer = new byte[16 * 1024];

                    while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                    {
                        var result = await ws.ReceiveAsync(buffer, stoppingToken);

                        if (result.Count == 0 && result.MessageType == WebSocketMessageType.Close)
                            break; // graceful close

                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                        if (json.Equals("ping", StringComparison.OrdinalIgnoreCase))
                        {
                            await ws.SendAsync(Encoding.UTF8.GetBytes("pong"), WebSocketMessageType.Text, true,
                                stoppingToken);
                            continue;
                        }

                        if (json.Contains("\"connection\":\"success\""))
                            continue; // ignore first service packet

                        _ = Task.Run(() => HandleMessage(json, stoppingToken), stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // normal shut‑down
                }
                catch (Exception ex)
                {
                    attempt++;
                    var delay = TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, attempt - 1), 300)) +
                                TimeSpan.FromMilliseconds(_rnd.Next(0, 1000)); // jitter
                    _log.LogWarning(ex, "WS loop error, reconnect in {Delay}s (attempt {Attempt}) …",
                        delay.TotalSeconds, attempt);
                    await Task.Delay(delay, stoppingToken);
                }
            }
        }

        // --------------------------------------------------------------
        private async Task HandleMessage(string json, CancellationToken ct)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("type", out var typeEl))
                {
                    _log.LogDebug("Unhandled WS payload: {Json}", json);
                    return;
                }

                var type = typeEl.GetString();
                if (type is not ("playlist_update" or "title_update"))
                    return;

                // playlist_update → id лежит прямо в data.id
                // title_update    → id лежит в data.title.id
                int id = type switch
                {
                    "playlist_update" => doc.RootElement
                        .GetProperty("data")
                        .GetProperty("id")
                        .GetInt32(),
                    "title_update" => doc.RootElement
                        .GetProperty("data")
                        .GetProperty("title")
                        .GetProperty("id")
                        .GetInt32(),
                    _ => 0
                };

                if (id == 0)
                {
                    _log.LogDebug("WS {Type} without id → ignore", type);
                    return;
                }

                _log.LogInformation("WS {Type} → titleId={Id}", type, id);

                var api = $"https://api.anilibria.tv/v3/title?id={id}";
                var raw = await _client.GetStringWithLoggingAsync(api, ct);

                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var title = JsonSerializer.Deserialize<TitleResponse>(raw, opts);
                if (title is null) return;

                var cfg = Plugin.Instance.Configuration;
                var pathsToUpdate = new List<string>();

                if (cfg.EnableAll)
                    pathsToUpdate.Add(cfg.StrmAllPath);

                if (cfg.EnableFavorites &&
                    FavoritesCache.IsFavorite(title.Id))
                    pathsToUpdate.Add(cfg.StrmFavoritesPath);

                foreach (var path in pathsToUpdate.Distinct())
                {
                    await _gen.GenerateTitlesAsync(new[] { title },
                        path,
                        cfg.PreferredResolution,
                        null, ct);
                    _log.LogInformation("STRM regenerated for {Code} at {Path}", title.Code, path);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "HandleMessage failed: {Json}", json[..Math.Min(json.Length, 300)]);
            }
        }
    }

    internal static class FavoritesCache
    {
        private static readonly HashSet<int> _ids = new();

        public static void Update(IEnumerable<int> ids)
        {
            lock (_ids)
            {
                _ids.Clear();
                foreach (var id in ids) _ids.Add(id);
            }
        }

        public static bool IsFavorite(int id)
        {
            lock (_ids) return _ids.Contains(id);
        }
    }
}
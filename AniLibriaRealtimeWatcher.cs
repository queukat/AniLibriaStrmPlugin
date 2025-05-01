// ========= File: AniLibriaStrmPlugin/AniLibriaRealtimeWatcher.cs =========
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AniLibriaStrmPlugin.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AniLibriaStrmPlugin;

/// <summary>
///    WebSocket- ,  title/playlist-update,
///     STRM.
/// </summary>
public sealed class AniLibriaRealtimeWatcher : BackgroundService
{
    private readonly ILogger<AniLibriaRealtimeWatcher> _log;
    private readonly IAniLibriaStrmGenerator _gen;

    public AniLibriaRealtimeWatcher(ILogger<AniLibriaRealtimeWatcher> log,
                                    IAniLibriaStrmGenerator gen)
    {
        _log = log;
        _gen = gen;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        //   ?
        if (!Plugin.Instance.Configuration.EnableRealtimeUpdates)
        {
            _log.LogInformation("Real-time updates disabled.");
            return;
        }

        var uri = new Uri("wss://api.anilibria.tv/v3/ws/");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(uri, stoppingToken);
                _log.LogInformation("Connected to {Uri}", uri);

                var buffer = new byte[16*1024];
                while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(buffer, stoppingToken);
                    if (result.MessageType != WebSocketMessageType.Text) continue;

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _ = Task.Run(()=>HandleMessage(json, stoppingToken), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "WebSocket loop error, reconnecting in 30 s…");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────
    private async Task HandleMessage(string json, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type is not ("playlist_update" or "title_update")) return;

            var id = root.GetProperty("data").GetProperty("id").GetInt32();
            _log.LogInformation("WS {Type} → titleId={Id}", type, id);

            //    /v3/title?id=…
            var api = $"https://api.anilibria.tv/v3/title?id={id}";
            var raw = await new HttpClient().GetStringAsync(api, ct);
            var title = JsonSerializer.Deserialize<TitleResponse>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (title == null) return;

            var cfg = Plugin.Instance.Configuration;
            var basePath =
                cfg.TrackFavoritesOnly ? cfg.StrmFavoritesPath : cfg.StrmAllPath;

            await _gen.GenerateTitlesAsync(new[] { title },
                                           basePath,
                                           cfg.PreferredResolution,
                                           null,
                                           ct);

            _log.LogInformation("STRM regenerated for {Code}", title.Code);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "HandleMessage failed: {Json}", json[..Math.Min(json.Length,300)]);
        }
    }
}


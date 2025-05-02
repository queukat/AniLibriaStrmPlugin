using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AniLibriaStrmPlugin.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AniLibriaStrmPlugin;

/// <summary>
///    WebSocket‑слушатель API AniLibria (title/playlist‑update & т.п.)
///    При получении события регенерирует нужные .strm.
/// </summary>
public sealed class AniLibriaRealtimeWatcher : BackgroundService
{
    private readonly ILogger<AniLibriaRealtimeWatcher> _log;
    private readonly IAniLibriaStrmGenerator           _gen;

    public AniLibriaRealtimeWatcher(ILogger<AniLibriaRealtimeWatcher> log,
                                    IAniLibriaStrmGenerator            gen)
    {
        _log = log;
        _gen = gen;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Plugin.Instance.Configuration.EnableRealtimeUpdates)
        {
            _log.LogInformation("Real‑time updates disabled.");
            return;
        }

        var uri = new Uri("wss://api.anilibria.tv/v3/ws/");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(uri, stoppingToken);
                _log.LogInformation("WS connected → {Uri}", uri);

                var buffer = new byte[16 * 1024];

                while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    var mem   = buffer.AsMemory();
                    var result = await ws.ReceiveAsync(mem, stoppingToken);

                    // Пустое сообщение – сервер graceful‑закрывает соединение
                    if (result.Count == 0 && result.MessageType == WebSocketMessageType.Close)
                        break;

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    // 1) ping → pong
                    if (json.Equals("ping", StringComparison.OrdinalIgnoreCase))
                    {
                        await ws.SendAsync(Encoding.UTF8.GetBytes("pong"),
                                           WebSocketMessageType.Text, true, stoppingToken);
                        continue;
                    }

                    // 2) connection:success (первый сервисный пакет)
                    if (json.Contains("\"connection\":\"success\""))
                        continue; // можно игнорировать

                    _ = Task.Run(()=>HandleMessage(json, stoppingToken), stoppingToken);
                }
            }
            catch (OperationCanceledException) { /* normal shutdown */ }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "WebSocket loop error, reconnect in 30 s …");
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
            if (!doc.RootElement.TryGetProperty("type", out var typeEl))
            {
                _log.LogDebug("Unhandled WS payload: {Json}", json);
                return; // неизвестный пакет
            }

            var type = typeEl.GetString();
            if (type is not ("playlist_update" or "title_update")) return;

            var id = doc.RootElement.GetProperty("data").GetProperty("id").GetInt32();
            _log.LogInformation("WS {Type} → titleId={Id}", type, id);

            //    /v3/title?id=…
            var api  = $"https://api.anilibria.tv/v3/title?id={id}";
            var raw  = await new HttpClient().GetStringAsync(api, ct);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var title = JsonSerializer.Deserialize<TitleResponse>(raw, opts);

            if (title is null) return;

            var cfg      = Plugin.Instance.Configuration;
            var basePath = cfg.TrackFavoritesOnly ? cfg.StrmFavoritesPath
                                                  : cfg.StrmAllPath;

            await _gen.GenerateTitlesAsync(new[] { title }, basePath,
                                           cfg.PreferredResolution,
                                           null, ct);

            _log.LogInformation("STRM regenerated for {Code}", title.Code);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "HandleMessage failed: {Json}", json[..Math.Min(json.Length, 300)]);
        }
    }
}

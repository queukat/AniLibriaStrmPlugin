// ===== File: AniLibriaClient.cs =====
using System.Text.Json;
using AniLibriaStrmPlugin.Models;
using Microsoft.Extensions.Logging;

namespace AniLibriaStrmPlugin;

public interface IAniLibriaClient
{
    Task<List<TitleResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken token);
    Task<List<TitleResponse>> FetchFavoritesAsync(string session, int pageSize, int maxPages, CancellationToken token);
}

public sealed class AniLibriaClient : IAniLibriaClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AniLibriaClient> _log;

    public AniLibriaClient(HttpClient http, ILogger<AniLibriaClient> log)
    {
        _http = http;
        _log  = log;
    }

    public async Task<List<TitleResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct)
    {
        var result = new List<TitleResponse>();

        for (var page = 1; page <= maxPages; page++)
        {
            var url = $"https://api.anilibria.tv/v3/title/changes?limit={pageSize}&page={page}";
            _log.LogDebug("GET {Url}", url);

            string raw;
            try { raw = await _http.GetStringAsync(url, ct); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Request error (ALL/page={Page})", page);
                break;
            }

            var parsed = JsonSerializer.Deserialize<FavoritesResponse>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed?.List is { Count: > 0 })
            {
                result.AddRange(parsed.List);
                if (parsed.List.Count < pageSize) break;   // данных больше нет
            }
            else break;
        }

        return result;
    }

    public async Task<List<TitleResponse>> FetchFavoritesAsync(string session, int pageSize, int maxPages, CancellationToken ct)
    {
        var result = new List<TitleResponse>();

        for (var page = 1; page <= maxPages; page++)
        {
            var url = $"https://api.anilibria.tv/v3/user/favorites?session={session}&page={page}&items_per_page={pageSize}";
            _log.LogDebug("GET {Url}", url);

            string raw;
            try { raw = await _http.GetStringAsync(url, ct); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Request error (FAV/page={Page})", page);
                break;
            }

            var parsed = JsonSerializer.Deserialize<FavoritesResponse>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed?.List is { Count: > 0 })
            {
                result.AddRange(parsed.List);
                if (parsed.List.Count < pageSize) break;
            }
            else break;
        }

        return result;
    }
}

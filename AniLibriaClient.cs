// ===== File: AniLibriaClient.cs =====

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AniLibriaStrmPlugin.Models;
using AniLibriaStrmPlugin.Utils;
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
        _log = log;
    }

    public async Task<List<TitleResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct)
    {
        var result = new List<TitleResponse>();

        for (var page = 1; page <= maxPages; page++)
        {
            var url = $"https://api.anilibria.tv/v3/title/changes?limit={pageSize}&page={page}";
            _log.LogDebug("GET {Url}", url);

            string raw;
            try
            {
                raw = await _http.GetStringAsync(url, ct);
            }
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
                if (parsed.List.Count < pageSize) break; //   
            }
            else break;
        }

        return result;
    }
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<TitleResponse>> FetchFavoritesAsync(
        string session, int pageSize, int maxPages, CancellationToken ct)
    {
        var result = new List<TitleResponse>();

        for (var page = 1; page <= maxPages; page++)
        {
            var url = $"https://api.anilibria.tv/v3/user/favorites" +
                      $"?session={session}&page={page}&items_per_page={pageSize}";

            var sw = Stopwatch.StartNew();
            try
            {
                var raw = await _http.GetStringAsync(url, ct);
                sw.Stop();

                var parsed = JsonSerializer.Deserialize<FavoritesResponse>(raw, _jsonOpts);


                var got = parsed?.List?.Count ?? 0;
                _log.Info("FAV page {0}: HTTP OK, {1} items, {2} ms", page, got, sw.ElapsedMilliseconds);

                if (got == 0)
                {
                    if (page == 1)
                        _log.Warn("API    —  sessionId!");
                    break; // ё,   
                }

                result.AddRange(parsed.List);
            }
            catch (Exception ex)
            {
                _log.Err(ex, "FAV page {0} failed after {1} ms", page, sw.ElapsedMilliseconds);
                break;
            }
        }

        return result;
    }
}
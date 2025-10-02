using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AniLibertyStrmPlugin.Models;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin
{
    public interface IAniLibertyClient
    {
        Task<string> GetStringWithLoggingAsync(string url, CancellationToken ct);
        Task<string> GetStringAuthAsync(string url, string bearer, CancellationToken ct);

        Task<List<ReleaseResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct);
        Task<List<ReleaseResponse>> FetchFavoritesAsync(string bearerToken, int pageSize, int maxPages, CancellationToken ct);

        // NEW:
        Task<ReleaseResponse?> FetchReleaseByIdAsync(int id, CancellationToken ct);

        // NEW (franchises):
        Task<List<FranchiseInfo>?> FetchFranchisesForReleaseAsync(int releaseId, CancellationToken ct);
    }

    public sealed record AniLibertyClient(HttpClient http, ILogger<AniLibertyClient> log) : IAniLibertyClient
    {
        private const string ApiBase = "https://api.anilibria.app/api/v1";

        public async Task<string> GetStringWithLoggingAsync(string url, CancellationToken ct)
        {
            var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(resp, ct);
                log.LogError("HTTP {Code} for \"{Url}\": {Body}", (int)resp.StatusCode, url, Truncate(body, 300));
            }
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        public async Task<string> GetStringAuthAsync(string url, string bearer, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(resp, ct);
                log.LogError("HTTP {Code} for \"{Url}\": {Body}", (int)resp.StatusCode, url, Truncate(body, 300));
            }
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
        };

        public async Task<List<ReleaseResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct)
        {
            var result = new List<ReleaseResponse>();

            for (var page = 1; page <= maxPages; page++)   // ← 1-based
            {
                var url = $"{ApiBase}/anime/catalog/releases?limit={pageSize}&page={page}";
                log.LogDebug("GET {Url}", url);
                string raw = string.Empty;

                try
                {
                    raw = await GetStringWithLoggingAsync(url, ct);

                    List<ReleaseResponse>? pageData = null;
                    try
                    {
                        pageData = JsonSerializer.Deserialize<List<ReleaseResponse>>(raw, _jsonOpts);
                    }
                    catch (JsonException)
                    {
                        var old = JsonSerializer.Deserialize<ReleasesApiResponse>(raw, _jsonOpts);
                        pageData = old?.Data;
                    }

                    if (pageData is { Count: > 0 })
                    {
                        result.AddRange(pageData);
                        if (pageData.Count < pageSize) break;
                    }
                    else break;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "❌ Deserialization failed (page {Page}). Raw length={Len}. First 300:\n{Raw}",
                        page, raw?.Length ?? 0, Truncate(raw, 300));
                    break;
                }
            }

            return result;
        }

        public async Task<List<ReleaseResponse>> FetchFavoritesAsync(string bearerToken, int pageSize, int maxPages, CancellationToken ct)
        {
            var result = new List<ReleaseResponse>();

            for (var page = 1; page <= maxPages; page++)
            {
                var url = $"{ApiBase}/accounts/users/me/favorites/releases?limit={pageSize}&page={page}";
                var sw = Stopwatch.StartNew();
                try
                {
                    var raw    = await GetStringAuthAsync(url, bearerToken, ct);
                    var parsed = JsonSerializer.Deserialize<ReleasesApiResponse>(raw, _jsonOpts);
                    var got = parsed?.Data?.Count ?? 0;

                    sw.Stop();
                    log.LogInformation("FAV page {Page}: OK, {Items} items, {Ms} ms", page, got, sw.ElapsedMilliseconds);

                    if (got == 0)
                    {
                        if (page == 1)
                            log.LogWarning("API вернуло 0 избранного — проверьте токен или наличие избранного.");
                        break;
                    }

                    result.AddRange(parsed!.Data);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    log.LogError(ex, "FAV page {Page} failed after {Ms} ms", page, sw.ElapsedMilliseconds);
                    break;
                }
            }

            return result;
        }

        // NEW: подробности релиза с эпизодами
        public async Task<ReleaseResponse?> FetchReleaseByIdAsync(int id, CancellationToken ct)
        {
            var url = $"{ApiBase}/anime/releases/{id}";
            log.LogDebug("GET {Url}", url);

            try
            {
                var raw = await GetStringWithLoggingAsync(url, ct);
                var full = JsonSerializer.Deserialize<ReleaseResponse>(raw, _jsonOpts);
                if (full == null)
                    log.LogWarning("Deserialize of release {Id} returned null", id);
                return full;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch release {Id}", id);
                return null;
            }
        }

        // NEW: франшизы по релизу
        public async Task<List<FranchiseInfo>?> FetchFranchisesForReleaseAsync(int releaseId, CancellationToken ct)
        {
            var url = $"{ApiBase}/anime/franchises/release/{releaseId}";
            log.LogDebug("GET {Url}", url);

            try
            {
                var raw = await GetStringWithLoggingAsync(url, ct);
                var data = JsonSerializer.Deserialize<List<FranchiseInfo>>(raw, _jsonOpts);
                return data;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch franchises for release {Id}", releaseId);
                return null;
            }
        }

        private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            try { return await resp.Content.ReadAsStringAsync(ct); }
            catch { return "<unable to read body>"; }
        }

        private static string Truncate(string? text, int max) =>
            string.IsNullOrEmpty(text) || text.Length <= max ? text ?? string.Empty : text[..max] + " …";
    }

    internal sealed class ReleasesApiResponse
    {
        [JsonPropertyName("data")] public List<ReleaseResponse> Data { get; set; } = new();
    }
}

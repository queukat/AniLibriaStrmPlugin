
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
    /// <summary>
    ///   HTTP‑обёртка над новым API AniLiberty v1 с логированием.
    ///   Документация: https://api.anilibria.app/api/docs/v1
    /// </summary>
    public interface IAniLibertyClient
    {
        Task<string> GetStringWithLoggingAsync(string url, CancellationToken ct);
        Task<string> GetStringAuthAsync(string url, string bearer, CancellationToken ct);

        Task<List<ReleaseResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct);

        Task<List<ReleaseResponse>> FetchFavoritesAsync(
            string bearerToken,
            int pageSize,
            int maxPages,
            CancellationToken ct);
    }

    public sealed class AniLibertyClient : IAniLibertyClient
    {
        private const string ApiBase = "https://api.anilibria.app/api/v1";

        private readonly HttpClient _http;
        private readonly ILogger<AniLibertyClient> _log;

        public AniLibertyClient(HttpClient http, ILogger<AniLibertyClient> log)
        {
            _http = http;
            _log  = log;
        }

        // ──────────────────────────────────────────────
        #region Low‑level GET helpers (with logging)

        public async Task<string> GetStringWithLoggingAsync(string url, CancellationToken ct)
        {
            var resp = await _http.GetAsync(url, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(resp, ct);
                _log.LogError("HTTP {Code} for {Url}: {Body}",
                    (int)resp.StatusCode,
                    url,
                    Truncate(body, 300));
            }

            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        public async Task<string> GetStringAuthAsync(string url, string bearer, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

            var resp = await _http.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(resp, ct);
                _log.LogError("HTTP {Code} for {Url}: {Body}",
                    (int)resp.StatusCode,
                    url,
                    Truncate(body, 300));
            }

            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        #endregion

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
        };

        // ──────────────────────────────────────────────
        #region /anime/releases/latest → *весь* каталог (разбито пагинацией)

        public async Task<List<ReleaseResponse>> FetchAllTitlesAsync(
            int pageSize,
            int maxPages,
            CancellationToken ct)
        {
            var result = new List<ReleaseResponse>();

            for (var page = 0; page < maxPages; page++) // ⚠️ 0‑based индекс!
            {
                var url =
                    $"{ApiBase}/anime/releases/latest?limit={pageSize}&page={page}";
                _log.LogDebug("GET {Url}", url);

                try
                {
                    var raw    = await GetStringWithLoggingAsync(url, ct);
                    var parsed =
                        JsonSerializer.Deserialize<ReleasesApiResponse>(raw, _jsonOpts);

                    if (parsed?.Data is { Count: > 0 })
                    {
                        result.AddRange(parsed.Data);
                        if (parsed.Data.Count < pageSize) break; // последняя страница
                    }
                    else break; // пусто – выходим
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Request error (ALL/page={Page})", page);
                    break;
                }
            }

            return result;
        }

        #endregion

        // ──────────────────────────────────────────────
        #region /accounts/users/me/favorites/releases

        public async Task<List<ReleaseResponse>> FetchFavoritesAsync(
            string bearerToken,
            int pageSize,
            int maxPages,
            CancellationToken ct)
        {
            var result = new List<ReleaseResponse>();

            for (var page = 1; page <= maxPages; page++) // ⚠️ favourites – 1‑based page
            {
                var url =
                    $"{ApiBase}/accounts/users/me/favorites/releases?limit={pageSize}&page={page}";

                var sw = Stopwatch.StartNew();
                try
                {
                    var raw    = await GetStringAuthAsync(url, bearerToken, ct);
                    var parsed =
                        JsonSerializer.Deserialize<ReleasesApiResponse>(raw, _jsonOpts);
                    var got = parsed?.Data?.Count ?? 0;

                    sw.Stop();
                    _log.LogInformation("FAV page {Page}: OK, {Items} items, {Ms} ms", page, got,
                        sw.ElapsedMilliseconds);

                    if (got == 0)
                    {
                        if (page == 1)
                            _log.LogWarning("API вернуло 0 избранного — проверьте токен или наличие избранного.");
                        break;
                    }

                    result.AddRange(parsed.Data);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    _log.LogError(ex, "FAV page {Page} failed after {Ms} ms", page,
                        sw.ElapsedMilliseconds);
                    break;
                }
            }

            return result;
        }

        #endregion

        // ──────────────────────────────────────────────
        #region helpers

        private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            try { return await resp.Content.ReadAsStringAsync(ct); }
            catch { return "<unable to read body>"; }
        }

        private static string Truncate(string? text, int max) =>
            string.IsNullOrEmpty(text) || text.Length <= max ? text ?? string.Empty : text[..max] + " …";

        #endregion
    }

    // ═════════════ DTO wrappers ════════════════

    internal sealed class ReleasesApiResponse
    {
        [JsonPropertyName("data")] public List<ReleaseResponse> Data { get; set; } = new();
    }
}

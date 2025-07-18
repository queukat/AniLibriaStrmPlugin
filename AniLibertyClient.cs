
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

    public sealed record AniLibertyClient(HttpClient http, ILogger<AniLibertyClient> log) : IAniLibertyClient
    {
        private const string ApiBase = "https://api.anilibria.app/api/v1";

        // ──────────────────────────────────────────────
        #region Low‑level GET helpers (with logging)

        public async Task<string> GetStringWithLoggingAsync(string url, CancellationToken ct)
        {
            var resp = await http.GetAsync(url, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(resp, ct);
                log.LogError("HTTP {Code} for {Url}: {Body}",
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

            var resp = await http.SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(resp, ct);
                log.LogError("HTTP {Code} for {Url}: {Body}",
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
                    $"{ApiBase}/anime/catalog/releases?limit={pageSize}&page={page}";

                log.LogDebug("GET {Url}", url);
                log.LogTrace("⌚ request started {Url}", url);
        
                // объявляем raw заранее, чтобы его видеть и в try, и в catch
                string raw = string.Empty;
        
                try
                {
                    // присваиваем в пределах try
                    raw = await GetStringWithLoggingAsync(url, ct);
        
                    List<ReleaseResponse>? pageData = null;
        
                    try
                    {
                        // 1) пытаемся десериализовать новый формат
                        pageData = JsonSerializer.Deserialize<List<ReleaseResponse>>(raw, _jsonOpts);
                    }
                    catch (JsonException)
                    {
                        // 2) если не массив — пробуем старый формат с объектом { data: […] }
                        var old = JsonSerializer.Deserialize<ReleasesApiResponse>(raw, _jsonOpts);
                        pageData = old?.Data;
                    }
        
                    if (pageData is { Count: > 0 })
                    {
                        result.AddRange(pageData);
                        if (pageData.Count < pageSize)
                            break; // последняя страница
                    }
                    else
                    {
                        break; // пусто — выходим
                    }
                }
                catch (Exception ex)
                {
                    // теперь raw доступен здесь
                    log.LogError(ex,
                        "❌ Deserialization failed (page {Page}). Raw length={Len}. First 300 bytes:\n{Raw}",
                        page,
                        raw?.Length ?? 0,
                        Truncate(raw, 300));
        
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
                    log.LogInformation("FAV page {Page}: OK, {Items} items, {Ms} ms", page, got,
                        sw.ElapsedMilliseconds);

                    if (got == 0)
                    {
                        if (page == 1)
                            log.LogWarning("API вернуло 0 избранного — проверьте токен или наличие избранного.");
                        break;
                    }

                    result.AddRange(parsed.Data);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    log.LogError(ex, "FAV page {Page} failed after {Ms} ms", page,
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

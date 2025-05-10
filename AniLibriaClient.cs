// ===== File: AniLibriaClient.cs =====

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AniLibriaStrmPlugin.Models;
using Microsoft.Extensions.Logging;

namespace AniLibriaStrmPlugin
{
    /// <summary>
    ///   HTTP‑обёртка над API AniLibria с логированием ошибок.
    /// </summary>
    public interface IAniLibriaClient
    {
        /// <summary> Универсальный GET c логированием тела при ошибке. </summary>
        Task<string> GetStringWithLoggingAsync(string url, CancellationToken ct);

        Task<List<TitleResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct);

        Task<List<TitleResponse>> FetchFavoritesAsync(
            string session,
            int pageSize,
            int maxPages,
            CancellationToken ct);
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

        // ──────────────────────────────────────────────────────────────

        #region universal GET with body‑aware error logging

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

        #endregion

        // ──────────────────────────────────────────────────────────────

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // --------------------------------------------------------------
        public async Task<List<TitleResponse>> FetchAllTitlesAsync(
            int pageSize, int maxPages, CancellationToken ct)
        {
            var result = new List<TitleResponse>();

            for (var page = 1; page <= maxPages; page++)
            {
                var url = $"https://api.anilibria.tv/v3/title/changes?limit={pageSize}&page={page}";
                _log.LogDebug("GET {Url}", url);

                try
                {
                    var raw = await GetStringWithLoggingAsync(url, ct);
                    var parsed = JsonSerializer.Deserialize<FavoritesResponse>(raw, _jsonOpts);

                    if (parsed?.List is { Count: > 0 })
                    {
                        result.AddRange(parsed.List);
                        if (parsed.List.Count < pageSize) break; // последних меньше чем limit
                    }
                    else break; // пусто → конец
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Request error (ALL/page={Page})", page);
                    break;
                }
            }

            return result;
        }

        // --------------------------------------------------------------
        public async Task<List<TitleResponse>> FetchFavoritesAsync(
            string session, int pageSize, int maxPages, CancellationToken ct)
        {
            var result = new List<TitleResponse>();

            for (var page = 1; page <= maxPages; page++)
            {
                var url =
                    $"https://api.anilibria.tv/v3/user/favorites?session={session}" +
                    $"&page={page}&items_per_page={pageSize}";

                var sw = Stopwatch.StartNew();
                try
                {
                    var raw = await GetStringWithLoggingAsync(url, ct);
                    sw.Stop();

                    var parsed = JsonSerializer.Deserialize<FavoritesResponse>(raw, _jsonOpts);
                    var got = parsed?.List?.Count ?? 0;

                    _log.LogInformation("FAV page {Page}: OK, {Items} items, {Ms} ms",
                        page, got, sw.ElapsedMilliseconds);

                    if (got == 0)
                    {
                        if (page == 1)
                            _log.LogWarning("API вернуло 0 избранного — проверьте sessionId");
                        break;
                    }

                    result.AddRange(parsed.List);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    _log.LogError(ex, "FAV page {Page} failed after {Ms} ms",
                        page, sw.ElapsedMilliseconds);
                    break;
                }
            }

            return result;
        }

        // ──────────────────────────────────────────────────────────────

        #region helpers

        private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            try
            {
                return await resp.Content.ReadAsStringAsync(ct);
            }
            catch
            {
                return "<unable to read body>";
            }
        }

        private static string Truncate(string? text, int max) =>
            string.IsNullOrEmpty(text) || text.Length <= max
                ? text ?? string.Empty
                : text[..max] + " …";

        #endregion
    }
}
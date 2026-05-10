using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AniLibertyStrmPlugin.Utils;
using AniLibertyStrmPlugin.Models;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin;

public interface IAniLibertyClient
{
    Task<string> GetStringWithLoggingAsync(string url, CancellationToken ct);
    Task<string> GetStringAuthAsync(string url, string bearer, CancellationToken ct);

    Task<List<ReleaseResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct);

    Task<List<ReleaseResponse>> FetchFavoritesAsync(string bearerToken, int pageSize, int maxPages,
        CancellationToken ct);

    Task<bool> UpdateViewTimecodesAsync(string bearerToken, IEnumerable<ViewTimecodeUpdateItem> updates,
        CancellationToken ct);
    Task<List<ViewTimecodeEntry>> FetchViewTimecodesAsync(string bearerToken, DateTimeOffset? since, CancellationToken ct);

    // NEW:
    Task<ReleaseResponse?> FetchReleaseByIdAsync(int id, CancellationToken ct);

    // NEW (franchises):
    Task<List<FranchiseInfo>?> FetchFranchisesForReleaseAsync(int releaseId, CancellationToken ct);
}

public sealed record AniLibertyClient(HttpClient http, ILogger<AniLibertyClient> log) : IAniLibertyClient
{
    private const string ApiBase = "https://api.anilibria.app/api/v1";

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<string> GetStringWithLoggingAsync(string url, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await SafeReadAsync(resp, ct);

        if (!resp.IsSuccessStatusCode)
            log.Warn("HTTP {0} for \"{1}\": {2}", (int)resp.StatusCode, url, Truncate(body, 300));

        resp.EnsureSuccessStatusCode();
        return body;
    }

    public async Task<string> GetStringAuthAsync(string url, string bearer, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await SafeReadAsync(resp, ct);

        if (!resp.IsSuccessStatusCode)
            log.Warn("HTTP {0} for \"{1}\": {2}", (int)resp.StatusCode, url, Truncate(body, 300));

        resp.EnsureSuccessStatusCode();
        return body;
    }

    public async Task<List<ReleaseResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct)
    {
        var result = new List<ReleaseResponse>();

        for (var page = 1; page <= maxPages; page++) // ← 1-based
        {
            var url = $"{ApiBase}/anime/catalog/releases?limit={pageSize}&page={page}";
            log.LogDebug("GET {Url}", url);
            var raw = string.Empty;

            try
            {
                log.Debug("ALL page {0}/{1}: requesting catalog releases, limit={2}", page, maxPages, pageSize);
                raw = await GetStringWithLoggingAsync(url, ct);
                log.Debug("ALL page {0}: response received, {1} bytes", page, raw.Length);

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
                    log.Debug("ALL page {0}: parsed {1} titles, total={2}", page, pageData.Count, result.Count);
                    if (pageData.Count < pageSize) break;
                }
                else
                {
                    log.Debug("ALL page {0}: no titles returned, stopping.", page);
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Err(ex, "ALL page {0}: fetch/parse failed. Raw length={1}. First 300: {2}",
                    page, raw?.Length ?? 0, Truncate(raw, 300));
                break;
            }
        }

        return result;
    }

    public async Task<List<ReleaseResponse>> FetchFavoritesAsync(string bearerToken, int pageSize, int maxPages,
        CancellationToken ct)
    {
        var result = new List<ReleaseResponse>();

        for (var page = 1; page <= maxPages; page++)
        {
            var url = $"{ApiBase}/accounts/users/me/favorites/releases?limit={pageSize}&page={page}";
            var sw = Stopwatch.StartNew();
            try
            {
                var raw = await GetStringAuthAsync(url, bearerToken, ct);
                var parsed = JsonSerializer.Deserialize<ReleasesApiResponse>(raw, _jsonOpts);
                var got = parsed?.Data?.Count ?? 0;

                sw.Stop();
                log.Debug("FAV page {0}: OK, {1} items, {2} ms", page, got, sw.ElapsedMilliseconds);

                if (got == 0)
                {
                    if (page == 1)
                        log.Warn("API returned 0 favorites — check token or favorites content.");
                    break;
                }

                result.AddRange(parsed!.Data);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                log.Err(ex, "FAV page {0} failed after {1} ms", page, sw.ElapsedMilliseconds);
                break;
            }
        }

        return result;
    }

    public async Task<bool> UpdateViewTimecodesAsync(string bearerToken, IEnumerable<ViewTimecodeUpdateItem> updates,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            return false;

        var payloadItems = updates?.Where(x => !string.IsNullOrWhiteSpace(x.ReleaseEpisodeId)).ToList()
                           ?? new List<ViewTimecodeUpdateItem>();
        if (payloadItems.Count == 0)
            return false;

        var url = $"{ApiBase}/accounts/users/me/views/timecodes";
        var json = JsonSerializer.Serialize(payloadItems, _jsonOpts);

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await SafeReadAsync(resp, ct);

        if (!resp.IsSuccessStatusCode)
        {
            log.Warn("UpdateViewTimecodes failed HTTP {0}: {1}",
                (int)resp.StatusCode, Truncate(body, 320));
            return false;
        }

        return true;
    }

    public async Task<List<ViewTimecodeEntry>> FetchViewTimecodesAsync(string bearerToken, DateTimeOffset? since, CancellationToken ct)
    {
        var result = new Dictionary<string, ViewTimecodeEntry>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(bearerToken))
            return new List<ViewTimecodeEntry>();

        var url = $"{ApiBase}/accounts/users/me/views/timecodes";
        if (since.HasValue)
            url += $"?since={Uri.EscapeDataString(since.Value.UtcDateTime.ToString("O"))}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var raw = await SafeReadAsync(resp, ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.Warn("FetchViewTimecodes failed HTTP {0}: {1}",
                (int)resp.StatusCode, Truncate(raw, 320));
            return new List<ViewTimecodeEntry>();
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var dataEl))
                root = dataEl;

            if (root.ValueKind != JsonValueKind.Array)
                return new List<ViewTimecodeEntry>();

            foreach (var el in root.EnumerateArray())
            {
                if (!TryParseViewEntry(el, out var entry))
                    continue;

                if (!result.TryGetValue(entry.ReleaseEpisodeId, out var existing))
                {
                    result[entry.ReleaseEpisodeId] = entry;
                    continue;
                }

                if (entry.Time >= existing.Time || entry.IsWatched && !existing.IsWatched)
                {
                    existing.Time = Math.Max(existing.Time, entry.Time);
                    existing.IsWatched = existing.IsWatched || entry.IsWatched;
                    result[entry.ReleaseEpisodeId] = existing;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Warn(ex, "FetchViewTimecodes parse failed.");
        }

        return result.Values.ToList();
    }

    // NEW: release details with episodes
    public async Task<ReleaseResponse?> FetchReleaseByIdAsync(int id, CancellationToken ct)
    {
        var url = $"{ApiBase}/anime/releases/{id}";
        log.LogDebug("GET {Url}", url);

        try
        {
            var raw = await GetStringWithLoggingAsync(url, ct);
            var full = JsonSerializer.Deserialize<ReleaseResponse>(raw, _jsonOpts);
            if (full == null)
                log.Warn("Deserialize of release {0} returned null", id);
            return full;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Err(ex, "Failed to fetch release {0}", id);
            return null;
        }
    }

    // NEW: franchises by release
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Err(ex, "Failed to fetch franchises for release {0}", releaseId);
            return null;
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return "<unable to read body>";
        }
    }

    private static string Truncate(string? text, int max)
    {
        return string.IsNullOrEmpty(text) || text.Length <= max ? text ?? string.Empty : text[..max] + " …";
    }

    private static bool TryParseViewEntry(JsonElement el, out ViewTimecodeEntry entry)
    {
        entry = new ViewTimecodeEntry();

        if (el.ValueKind == JsonValueKind.Object)
        {
            var okId = el.TryGetProperty("release_episode_id", out var idEl) && idEl.ValueKind == JsonValueKind.String;
            var okTime = el.TryGetProperty("time", out var timeEl) &&
                         (timeEl.ValueKind == JsonValueKind.Number || timeEl.ValueKind == JsonValueKind.String);
            var okWatched = el.TryGetProperty("is_watched", out var watchedEl) &&
                            (watchedEl.ValueKind == JsonValueKind.True ||
                             watchedEl.ValueKind == JsonValueKind.False ||
                             watchedEl.ValueKind == JsonValueKind.String);
            if (!okId || !okTime || !okWatched)
                return false;

            var id = idEl.GetString()?.Trim() ?? string.Empty;
            if (!Guid.TryParse(id, out _))
                return false;

            if (!TryGetDouble(timeEl, out var time))
                return false;
            if (!TryGetBool(watchedEl, out var watched))
                return false;

            entry.ReleaseEpisodeId = id;
            entry.Time = Math.Max(0, time);
            entry.IsWatched = watched;
            return true;
        }

        // Some API variants may return tuple-like arrays: [release_episode_id, time, is_watched]
        if (el.ValueKind == JsonValueKind.Array)
        {
            var arr = el.EnumerateArray().ToArray();
            if (arr.Length < 3)
                return false;

            if (arr[0].ValueKind != JsonValueKind.String)
                return false;

            var id = arr[0].GetString()?.Trim() ?? string.Empty;
            if (!Guid.TryParse(id, out _))
                return false;
            if (!TryGetDouble(arr[1], out var time))
                return false;
            if (!TryGetBool(arr[2], out var watched))
                return false;

            entry.ReleaseEpisodeId = id;
            entry.Time = Math.Max(0, time);
            entry.IsWatched = watched;
            return true;
        }

        return false;
    }

    private static bool TryGetDouble(JsonElement el, out double value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number)
            return el.TryGetDouble(out value);

        if (el.ValueKind == JsonValueKind.String)
        {
            var txt = el.GetString();
            return double.TryParse(txt, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        return false;
    }

    private static bool TryGetBool(JsonElement el, out bool value)
    {
        value = false;
        if (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False)
        {
            value = el.GetBoolean();
            return true;
        }

        if (el.ValueKind == JsonValueKind.String)
            return bool.TryParse(el.GetString(), out value);

        return false;
    }
}

internal sealed class ReleasesApiResponse
{
    [JsonPropertyName("data")] public List<ReleaseResponse> Data { get; set; } = new();
}

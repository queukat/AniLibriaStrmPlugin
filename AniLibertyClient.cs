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
        {
            log.Warn("HTTP {0} for \"{1}\": {2}", (int)resp.StatusCode, url, Truncate(body, 300));
        }

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
        {
            log.Warn("HTTP {0} for \"{1}\": {2}", (int)resp.StatusCode, url, Truncate(body, 300));
            if (AniLibertyAuthExpiredException.IsAuthFailure(resp.StatusCode))
                throw new AniLibertyAuthExpiredException(resp.StatusCode, url, body);
        }

        resp.EnsureSuccessStatusCode();
        return body;
    }

    public async Task<List<ReleaseResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPages);
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

                var pageData = ParseReleasePage(raw, allowArray: true);

                if (pageData is { Count: > 0 })
                {
                    result.AddRange(pageData);
                    log.Debug("ALL page {0}: parsed {1} titles, total={2}", page, pageData.Count, result.Count);
                    if (pageData.Count < pageSize) return result;
                }
                else
                {
                    log.Debug("ALL page {0}: no titles returned, stopping.", page);
                    return result;
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
                throw;
            }
        }

        throw new InvalidOperationException("Catalog page limit reached before completeness was established. Increase MaxPages; library cleanup was not started.");
    }

    public async Task<List<ReleaseResponse>> FetchFavoritesAsync(string bearerToken, int pageSize, int maxPages,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPages);
        var result = new List<ReleaseResponse>();

        for (var page = 1; page <= maxPages; page++)
        {
            var url = $"{ApiBase}/accounts/users/me/favorites/releases?limit={pageSize}&page={page}";
            var sw = Stopwatch.StartNew();
            try
            {
                var raw = await GetStringAuthAsync(url, bearerToken, ct);
                var data = ParseReleasePage(raw, allowArray: false);
                var got = data.Count;

                sw.Stop();
                log.Debug("FAV page {0}: OK, {1} items, {2} ms", page, got, sw.ElapsedMilliseconds);

                if (got == 0)
                {
                    if (page == 1)
                        log.Warn("API returned 0 favorites — check token or favorites content.");
                    return result;
                }

                result.AddRange(data);
                if (got < pageSize)
                    return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AniLibertyAuthExpiredException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                log.Err(ex, "FAV page {0} failed after {1} ms", page, sw.ElapsedMilliseconds);
                throw;
            }
        }

        throw new InvalidOperationException("Favorites page limit reached before completeness was established. Increase MaxPages; library cleanup was not started.");
    }

    private static List<ReleaseResponse> ParseReleasePage(string raw, bool allowArray)
    {
        using var document = JsonDocument.Parse(raw);
        var data = document.RootElement;
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("data", out var wrapped))
            data = wrapped;
        else if (!allowArray)
            throw new JsonException("Release response does not contain a data array.");

        if (data.ValueKind != JsonValueKind.Array)
            throw new JsonException("Release response does not contain a release array.");
        var releases = data.Deserialize<List<ReleaseResponse>>(_jsonOpts)!;
        if (releases.Any(release => release is null || release.Id <= 0))
            throw new JsonException("Release response contains an invalid release.");
        return releases;
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
            if (AniLibertyAuthExpiredException.IsAuthFailure(resp.StatusCode))
                throw new AniLibertyAuthExpiredException(resp.StatusCode, url, body);

            return false;
        }

        return true;
    }

    public async Task<List<ViewTimecodeEntry>> FetchViewTimecodesAsync(string bearerToken, DateTimeOffset? since, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            return new List<ViewTimecodeEntry>();

        var raw = await FetchViewTimecodesRawAsync(bearerToken, since, ct);
        return raw is null ? new List<ViewTimecodeEntry>() : ParseViewTimecodes(raw);
    }

    private async Task<string?> FetchViewTimecodesRawAsync(string bearerToken, DateTimeOffset? since, CancellationToken ct)
    {
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
            if (AniLibertyAuthExpiredException.IsAuthFailure(resp.StatusCode))
                throw new AniLibertyAuthExpiredException(resp.StatusCode, url, raw);

            return null;
        }

        return raw;
    }

    private List<ViewTimecodeEntry> ParseViewTimecodes(string raw)
    {
        var result = new Dictionary<string, ViewTimecodeEntry>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            foreach (var el in EnumerateViewTimecodeItems(doc.RootElement))
                MergeViewEntry(result, el);
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

    private static JsonElement[] EnumerateViewTimecodeItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var dataEl))
            root = dataEl;

        return root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();
    }

    private static void MergeViewEntry(Dictionary<string, ViewTimecodeEntry> result, JsonElement el)
    {
        if (!TryParseViewEntry(el, out var entry))
            return;

        if (!result.TryGetValue(entry.ReleaseEpisodeId, out var existing))
        {
            result[entry.ReleaseEpisodeId] = entry;
            return;
        }

        if (entry.Time < existing.Time && (!entry.IsWatched || existing.IsWatched))
            return;

        existing.Time = Math.Max(existing.Time, entry.Time);
        existing.IsWatched = existing.IsWatched || entry.IsWatched;
        result[entry.ReleaseEpisodeId] = existing;
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
            if (full is null || full.Id != id)
                throw new JsonException($"Release details do not match requested release {id}.");
            return full;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Err(ex, "Failed to fetch release {0}", id);
            throw;
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

        return el.ValueKind switch
        {
            JsonValueKind.Object => TryParseObjectViewEntry(el, out entry),
            JsonValueKind.Array => TryParseArrayViewEntry(el, out entry),
            _ => false
        };
    }

    private static bool TryParseObjectViewEntry(JsonElement el, out ViewTimecodeEntry entry)
    {
        entry = new ViewTimecodeEntry();
        if (!el.TryGetProperty("release_episode_id", out var idEl) ||
            !el.TryGetProperty("time", out var timeEl) ||
            !el.TryGetProperty("is_watched", out var watchedEl))
            return false;

        return TryCreateViewEntry(idEl, timeEl, watchedEl, out entry);
    }

    private static bool TryParseArrayViewEntry(JsonElement el, out ViewTimecodeEntry entry)
    {
        entry = new ViewTimecodeEntry();
        var arr = el.EnumerateArray().ToArray();
        return arr.Length >= 3 && TryCreateViewEntry(arr[0], arr[1], arr[2], out entry);
    }

    private static bool TryCreateViewEntry(
        JsonElement idEl,
        JsonElement timeEl,
        JsonElement watchedEl,
        out ViewTimecodeEntry entry)
    {
        entry = new ViewTimecodeEntry();
        if (idEl.ValueKind != JsonValueKind.String)
            return false;

        var id = idEl.GetString()?.Trim() ?? string.Empty;
        if (!Guid.TryParse(id, out _) ||
            !TryGetDouble(timeEl, out var time) ||
            !TryGetBool(watchedEl, out var watched))
            return false;

        entry.ReleaseEpisodeId = id;
        entry.Time = Math.Max(0, time);
        entry.IsWatched = watched;
        return true;
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

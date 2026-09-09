using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using AniLibertyStrmPlugin.Utils;

namespace AniLibertyStrmPlugin;

public sealed partial class AniLibertyStrmGenerator
{
    // Execution-only reuse: validators persist in the existing managed-file manifest.
    // Content without HTTP validators is refreshed normally, not assumed immutable by URL.
    internal sealed class ImageRefreshSession(HttpClient http)
    {
        private const int MaxCachedBytes = 8 * 1024 * 1024;
        private const int MaxCachedEntries = 64;
        private readonly Dictionary<string, RefreshedImage> _downloads = new(StringComparer.Ordinal);
        private int _cachedBytes;

        internal async Task<RefreshedImage?> GetAsync(
            string url, string path, ManagedLibraryManifest manifest, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_downloads.TryGetValue(url, out var cached))
                return cached;

            var validators = manifest.GetImageValidators(path, url);
            byte[]? existing = null;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (File.Exists(path) && validators.ContentHash is not null &&
                (validators.ETag is not null || validators.LastModified.HasValue))
            {
                existing = await File.ReadAllBytesAsync(path, ct);
                if (string.Equals(Convert.ToHexString(SHA256.HashData(existing)), validators.ContentHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (EntityTagHeaderValue.TryParse(validators.ETag, out var etag))
                        request.Headers.IfNoneMatch.Add(etag);
                    request.Headers.IfModifiedSince = validators.LastModified;
                }
                else
                    existing = null;
            }

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            RefreshedImage result;
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (existing is null || (request.Headers.IfNoneMatch.Count == 0 && !request.Headers.IfModifiedSince.HasValue))
                    throw new HttpRequestException("Image returned 304 without a validated local representation.");
                result = new RefreshedImage(existing,
                    response.Headers.ETag?.ToString() ?? validators.ETag,
                    response.Content.Headers.LastModified ?? validators.LastModified);
            }
            else
            {
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (!IsSupportedImage(bytes))
                    return null;
                result = new RefreshedImage(bytes, response.Headers.ETag?.ToString(), response.Content.Headers.LastModified);
            }

            if (result.Bytes.Length <= MaxCachedBytes)
            {
                if (_downloads.Count >= MaxCachedEntries || _cachedBytes + result.Bytes.Length > MaxCachedBytes)
                {
                    _downloads.Clear();
                    _cachedBytes = 0;
                }
                _downloads.Add(url, result);
                _cachedBytes += result.Bytes.Length;
            }
            return result;
        }
    }

    internal sealed record RefreshedImage(byte[] Bytes, string? ETag, DateTimeOffset? LastModified);
}

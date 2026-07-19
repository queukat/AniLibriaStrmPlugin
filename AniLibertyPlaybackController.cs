using AniLibertyStrmPlugin.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin;

[ApiController]
[Route("AniLibertyPlayback")]
public sealed class AniLibertyPlaybackController(
    IHttpClientFactory httpClientFactory,
    ILogger<AniLibertyPlaybackController> log)
    : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("hls")]
    public async Task<IActionResult> Hls([FromQuery] string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return BadRequest("Missing url.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var upstreamUri) || !PlaybackProxyHelper.IsAllowedUpstream(upstreamUri))
            return BadRequest("Unsupported upstream url.");

        try
        {
            var http = httpClientFactory.CreateClient("AniLibertyMediaProxy");
            using var upstream = await http.GetAsync(upstreamUri, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!upstream.IsSuccessStatusCode)
                return StatusCode((int)upstream.StatusCode);

            var effectiveUri = upstream.RequestMessage?.RequestUri ?? upstreamUri;
            if (!PlaybackProxyHelper.IsAllowedUpstream(effectiveUri))
                return BadRequest("Unsupported upstream redirect.");

            var contentType = upstream.Content.Headers.ContentType?.ToString();

            if (PlaybackProxyHelper.IsHlsPlaylist(effectiveUri, contentType))
            {
                var playlistText = await upstream.Content.ReadAsStringAsync(ct);
                var proxyEndpointUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}";
                var rewritten = PlaybackProxyHelper.RewritePlaylist(playlistText, effectiveUri, proxyEndpointUrl);
                return Content(rewritten, "application/vnd.apple.mpegurl");
            }

            var fileContentType = contentType ?? "application/octet-stream";
            Response.ContentType = fileContentType;
            if (upstream.Content.Headers.ContentLength is long contentLength)
                Response.ContentLength = contentLength;

            await upstream.Content.CopyToAsync(Response.Body, ct);
            return new EmptyResult();
        }
        catch (OperationCanceledException) when (Response.HasStarted)
        {
            return new EmptyResult();
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex) when (Response.HasStarted)
        {
            log.LogDebug(ex, "AniLiberty playback proxy failed after response started for {Url}", upstreamUri);
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "AniLiberty playback proxy failed for {Url}", upstreamUri);
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}

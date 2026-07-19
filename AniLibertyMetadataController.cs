using System.Text.Json;
using AniLibertyStrmPlugin.Models;
using AniLibertyStrmPlugin.Web;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin;

[ApiController]
[Route("AniLibertyMetadata")]
public sealed class AniLibertyMetadataController(
    ILibraryManager libraryManager,
    ILogger<AniLibertyMetadataController> log)
    : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [HttpGet("Popularity")]
    public IActionResult Popularity([FromQuery] Guid itemId)
    {
        if (itemId == Guid.Empty)
            return BadRequest("Missing itemId.");

        var item = libraryManager.GetItemById(itemId);
        if (item is null || string.IsNullOrWhiteSpace(item.Path))
            return NotFound();

        try
        {
            var response = TryResolvePopularityForPath(item.Path);
            return response is null
                ? NotFound()
                : Ok(response);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to resolve AniLiberty popularity metadata. itemId={ItemId}", itemId);
            return NotFound();
        }
    }

    [AllowAnonymous]
    [HttpGet("Assets/{fileName}")]
    public IActionResult Asset(string fileName)
    {
        if (string.Equals(fileName, "aniliberty-popularity.js", StringComparison.OrdinalIgnoreCase))
        {
            return Content(
                AniLibertyPopularityWebUiScript.Build(),
                "application/javascript; charset=utf-8");
        }

        if (!string.Equals(fileName, "aniliberty-rating.png", StringComparison.OrdinalIgnoreCase))
            return NotFound();

        var stream = typeof(Plugin).Assembly.GetManifestResourceStream("AniLibertyStrmPlugin.Resources.icon.png");
        return stream is null
            ? NotFound()
            : File(stream, "image/png");
    }

    internal static string? FindPopularitySidecarPath(string? itemPath)
    {
        if (string.IsNullOrWhiteSpace(itemPath))
            return null;

        var current = ResolveStartingDirectory(itemPath);
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(current, AniLibertyPopularityDocument.FileName);
            if (System.IO.File.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;

            current = parent;
        }

        return null;
    }

    internal static AniLibertyPopularityResponse? TryResolvePopularityForPath(string? itemPath)
    {
        var sidecarPath = FindPopularitySidecarPath(itemPath);
        if (string.IsNullOrWhiteSpace(sidecarPath))
            return null;

        var document = ReadPopularityDocument(sidecarPath);
        return document is null
            ? null
            : AniLibertyPopularityResponse.FromDocument(document);
    }

    internal static AniLibertyPopularityDocument? ReadPopularityDocument(string? sidecarPath)
    {
        if (string.IsNullOrWhiteSpace(sidecarPath) || !System.IO.File.Exists(sidecarPath))
            return null;

        try
        {
            using var stream = System.IO.File.OpenRead(sidecarPath);
            var document = JsonSerializer.Deserialize<AniLibertyPopularityDocument>(stream, JsonOptions);
            return document is { } && document.IsPluginGenerated() && document.HasAnyPopularityCount()
                ? document
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveStartingDirectory(string itemPath)
    {
        var fullPath = Path.GetFullPath(itemPath);
        if (Directory.Exists(fullPath))
            return fullPath;

        return Path.GetDirectoryName(fullPath);
    }
}

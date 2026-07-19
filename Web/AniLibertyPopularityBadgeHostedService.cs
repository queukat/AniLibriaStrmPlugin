using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Web;

internal sealed class AniLibertyPopularityBadgeHostedService(
    ILogger<AniLibertyPopularityBadgeHostedService> logger)
    : IHostedService, IDisposable
{
    private string? _patchedIndexHtmlPath;
    private bool _disposed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        try
        {
            var plugin = Plugin.Instance;
            if (plugin is null)
            {
                logger.LogDebug("AniLiberty popularity badge skipped because the plugin instance is not available.");
                return Task.CompletedTask;
            }

            if (!plugin.Configuration.EnableAniLibertyPopularityBadge)
            {
                logger.LogDebug("AniLiberty popularity badge is disabled in plugin configuration.");
                var restoreResult = AniLibertyWebUiIndexHtmlPatcher.RestoreDiscovered(logger);
                if (restoreResult.Success && restoreResult.Changed)
                {
                    logger.LogInformation(
                        "AniLiberty popularity badge removed its Jellyfin Web patch from {IndexHtmlPath}.",
                        restoreResult.IndexHtmlPath);
                }

                return Task.CompletedTask;
            }

            var result = AniLibertyWebUiIndexHtmlPatcher.ApplyDiscovered(logger);
            if (!result.Success)
            {
                logger.LogWarning(
                    "AniLiberty popularity badge could not patch Jellyfin Web index.html. {Message}",
                    result.Message);
                return Task.CompletedTask;
            }

            _patchedIndexHtmlPath = result.IndexHtmlPath;
            if (result.Changed)
            {
                logger.LogInformation(
                    "AniLiberty popularity badge patched Jellyfin Web index.html at {IndexHtmlPath}. Browser refresh may be required.",
                    result.IndexHtmlPath);
            }
            else
            {
                logger.LogDebug(
                    "AniLiberty popularity badge Jellyfin Web patch is already present at {IndexHtmlPath}.",
                    result.IndexHtmlPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AniLiberty popularity badge failed to initialize.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Unregister();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Unregister();
    }

    private void Unregister()
    {
        if (string.IsNullOrWhiteSpace(_patchedIndexHtmlPath))
            return;

        try
        {
            var result = AniLibertyWebUiIndexHtmlPatcher.Restore(_patchedIndexHtmlPath, logger);
            if (result.Success && result.Changed)
            {
                logger.LogInformation(
                    "AniLiberty popularity badge restored Jellyfin Web index.html at {IndexHtmlPath}.",
                    result.IndexHtmlPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "AniLiberty popularity badge failed to unregister.");
        }
        finally
        {
            _patchedIndexHtmlPath = null;
        }
    }
}

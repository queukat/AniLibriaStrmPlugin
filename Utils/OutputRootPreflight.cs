using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Utils;

internal static class OutputRootPreflight
{
    public static async Task EnsureWritableAsync(
        string? path,
        string label,
        ILogger log,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"{label} is empty.");

        var normalizedPath = path.Trim();
        var fullPath = Path.GetFullPath(normalizedPath);

        try
        {
            Directory.CreateDirectory(fullPath);

            var statePath = Path.Combine(fullPath, ManagedLibraryManifest.StateDirectoryName);
            Directory.CreateDirectory(statePath);

            var probePath = Path.Combine(
                statePath,
                $".write-test-{Guid.NewGuid():N}.tmp");

            await File.WriteAllTextAsync(
                probePath,
                "AniLiberty STRM Plugin write preflight",
                token);

            File.Delete(probePath);

            log.Info("Output root preflight OK: {0}=\"{1}\".", label, fullPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                   or IOException
                                   or ArgumentException
                                   or NotSupportedException
                                   or System.Security.SecurityException)
        {
            log.Err(ex, "Output root preflight failed: {0}=\"{1}\"", label, fullPath);
            throw new InvalidOperationException(
                $"{label} is not writable by the Jellyfin server process: {fullPath}. " +
                "Check Docker volume mapping, host directory ownership, and PUID/PGID permissions.",
                ex);
        }
    }
}

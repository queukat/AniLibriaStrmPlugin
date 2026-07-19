using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Web;

internal static class AniLibertyWebUiIndexHtmlPatcher
{
    internal const string BackupSuffix = ".aniliberty-popularity-badge.bak";

    public static AniLibertyWebUiPatchResult ApplyDiscovered(ILogger logger)
    {
        var candidates = GetCandidateIndexHtmlPaths();
        AniLibertyWebUiPatchResult? firstFailure = null;
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;

            var result = Apply(candidate, logger);
            if (result.Success)
                return result;

            firstFailure ??= result;
        }

        return firstFailure ?? AniLibertyWebUiPatchResult.Failure(
            null,
            "Jellyfin Web index.html was not found. Tried: " + string.Join("; ", candidates));
    }

    public static AniLibertyWebUiPatchResult RestoreDiscovered(ILogger logger)
    {
        var candidates = GetCandidateIndexHtmlPaths();
        AniLibertyWebUiPatchResult? firstFailure = null;
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;

            var result = Restore(candidate, logger);
            if (result.Success)
                return result;

            firstFailure ??= result;
        }

        return firstFailure ?? AniLibertyWebUiPatchResult.Failure(
            null,
            "Jellyfin Web index.html was not found. Tried: " + string.Join("; ", candidates));
    }

    internal static AniLibertyWebUiPatchResult Apply(string indexHtmlPath, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(indexHtmlPath))
            return AniLibertyWebUiPatchResult.Failure(indexHtmlPath, "Jellyfin Web index.html path is empty.");

        try
        {
            if (!File.Exists(indexHtmlPath))
                return AniLibertyWebUiPatchResult.Failure(indexHtmlPath, "Jellyfin Web index.html does not exist.");

            var html = ReadIndexHtml(indexHtmlPath);
            if (!LooksLikeIndexHtml(html))
            {
                return AniLibertyWebUiPatchResult.Failure(
                    indexHtmlPath,
                    "Jellyfin Web index.html did not look like an HTML document with a </body> tag.");
            }

            var transformed = AniLibertyPopularityWebUiInjector.TransformIndexHtml(html);
            if (string.Equals(html, transformed, StringComparison.Ordinal))
            {
                return AniLibertyWebUiPatchResult.SuccessUnchanged(
                    indexHtmlPath,
                    "AniLiberty popularity badge is already present in Jellyfin Web index.html.");
            }

            EnsureBackup(indexHtmlPath, logger);
            WriteIndexHtml(indexHtmlPath, transformed);
            return AniLibertyWebUiPatchResult.SuccessChanged(
                indexHtmlPath,
                "AniLiberty popularity badge was injected into Jellyfin Web index.html.");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "AniLiberty popularity badge could not patch Jellyfin Web index.html at {IndexHtmlPath}.", indexHtmlPath);
            return AniLibertyWebUiPatchResult.Failure(indexHtmlPath, ex.Message);
        }
    }

    internal static AniLibertyWebUiPatchResult Restore(string indexHtmlPath, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(indexHtmlPath))
            return AniLibertyWebUiPatchResult.Failure(indexHtmlPath, "Jellyfin Web index.html path is empty.");

        try
        {
            if (!File.Exists(indexHtmlPath))
                return AniLibertyWebUiPatchResult.Failure(indexHtmlPath, "Jellyfin Web index.html does not exist.");

            var html = ReadIndexHtml(indexHtmlPath);
            if (!AniLibertyPopularityWebUiInjector.HasInjectedBlock(html))
            {
                TryDeleteBackup(indexHtmlPath, logger);
                return AniLibertyWebUiPatchResult.SuccessUnchanged(
                    indexHtmlPath,
                    "AniLiberty popularity badge was not present in Jellyfin Web index.html.");
            }

            var restored = AniLibertyPopularityWebUiInjector.RemoveInjectedBlock(html);
            WriteIndexHtml(indexHtmlPath, restored);
            TryDeleteBackup(indexHtmlPath, logger);
            return AniLibertyWebUiPatchResult.SuccessChanged(
                indexHtmlPath,
                "AniLiberty popularity badge was removed from Jellyfin Web index.html.");
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "AniLiberty popularity badge could not restore Jellyfin Web index.html at {IndexHtmlPath}.", indexHtmlPath);
            return AniLibertyWebUiPatchResult.Failure(indexHtmlPath, ex.Message);
        }
    }

    internal static IReadOnlyList<string> GetCandidateIndexHtmlPaths()
    {
        var candidates = new List<string>();

        AddConfiguredWebPath(candidates, Environment.GetEnvironmentVariable("JELLYFIN_WEB_INDEX_HTML"), isIndexHtml: true);
        AddConfiguredWebPath(candidates, Environment.GetEnvironmentVariable("JELLYFIN_WEB_DIR"), isIndexHtml: false);

        AddRelativeCandidate(candidates, AppContext.BaseDirectory);

        try
        {
            var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(processPath))
                AddRelativeCandidate(candidates, Path.GetDirectoryName(processPath));
        }
        catch
        {
            // Process.MainModule can be unavailable under restricted service accounts.
        }

        AddConfiguredWebPath(
            candidates,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Jellyfin", "Server", "jellyfin-web"),
            isIndexHtml: false);
        AddConfiguredWebPath(
            candidates,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Jellyfin", "Server", "jellyfin-web"),
            isIndexHtml: false);

        AddConfiguredWebPath(candidates, "/usr/share/jellyfin/web", isIndexHtml: false);
        AddConfiguredWebPath(candidates, "/usr/lib/jellyfin/bin/jellyfin-web", isIndexHtml: false);
        AddConfiguredWebPath(candidates, "/opt/jellyfin/jellyfin-web", isIndexHtml: false);

        return candidates
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string GetBackupPath(string indexHtmlPath)
        => indexHtmlPath + BackupSuffix;

    private static void AddRelativeCandidate(ICollection<string> candidates, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return;

        AddConfiguredWebPath(candidates, Path.Combine(baseDirectory, "jellyfin-web"), isIndexHtml: false);
        AddConfiguredWebPath(candidates, Path.Combine(baseDirectory, "..", "jellyfin-web"), isIndexHtml: false);
        AddConfiguredWebPath(candidates, Path.Combine(baseDirectory, "..", "..", "jellyfin-web"), isIndexHtml: false);
    }

    private static void AddConfiguredWebPath(ICollection<string> candidates, string? path, bool isIndexHtml)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        candidates.Add(isIndexHtml ? path : Path.Combine(path, "index.html"));
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static string ReadIndexHtml(string indexHtmlPath)
    {
        using var stream = File.Open(indexHtmlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void WriteIndexHtml(string indexHtmlPath, string html)
    {
        var tempPath = Path.Combine(
            Path.GetDirectoryName(indexHtmlPath) ?? ".",
            Path.GetFileName(indexHtmlPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            File.WriteAllText(tempPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, indexHtmlPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static void EnsureBackup(string indexHtmlPath, ILogger? logger)
    {
        var backupPath = GetBackupPath(indexHtmlPath);
        if (File.Exists(backupPath))
            return;

        try
        {
            File.Copy(indexHtmlPath, backupPath, overwrite: false);
        }
        catch (IOException)
        {
            // Another startup thread may have created it between File.Exists and File.Copy.
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "AniLiberty popularity badge could not create backup file {BackupPath}.", backupPath);
        }
    }

    private static void TryDeleteBackup(string indexHtmlPath, ILogger? logger)
    {
        var backupPath = GetBackupPath(indexHtmlPath);
        try
        {
            TryDeleteFile(backupPath);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "AniLiberty popularity badge could not delete backup file {BackupPath}.", backupPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static bool LooksLikeIndexHtml(string html)
        => !string.IsNullOrWhiteSpace(html)
           && html.Contains("</body>", StringComparison.OrdinalIgnoreCase);
}

internal readonly record struct AniLibertyWebUiPatchResult(
    bool Success,
    bool Changed,
    string? IndexHtmlPath,
    string Message)
{
    public static AniLibertyWebUiPatchResult SuccessChanged(string? indexHtmlPath, string message)
        => new(true, true, indexHtmlPath, message);

    public static AniLibertyWebUiPatchResult SuccessUnchanged(string? indexHtmlPath, string message)
        => new(true, false, indexHtmlPath, message);

    public static AniLibertyWebUiPatchResult Failure(string? indexHtmlPath, string message)
        => new(false, false, indexHtmlPath, message);
}

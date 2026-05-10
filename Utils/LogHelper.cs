using System;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Utils;

internal static class LogHelper
{
    private static string WithLevel(string level, string msg)
        => $"[{level}] {msg}";

    private static void AppendTaskLogSafe(LogLevel level, string levelLabel, string msg)
    {
        try
        {
            // In normal plugin runtime Instance is available;
            // in tests it can be null, so we just exit.
            var plugin = Plugin.Instance;
            if (plugin is null)
                return;

            plugin.AppendTaskLog(WithLevel(levelLabel, msg), level);
        }
        catch
        {
            // Logging must never break business logic; swallow all exceptions.
        }
    }

    private static void AppendTaskLogSafe(LogLevel level, string levelLabel, string msg, Exception ex)
        => AppendTaskLogSafe(level, levelLabel, $"{msg} — {ex.Message}");

    private static void AppendRawSupportLogSafe(string msg)
    {
        try
        {
            var plugin = Plugin.Instance;
            if (plugin is null)
                return;

            plugin.AppendRawSupportLog(msg);
        }
        catch
        {
            // Diagnostic logging must never break business logic.
        }
    }

    private static void AppendRawExceptionDetailSafe(string msg, Exception ex)
        => AppendRawSupportLogSafe($"[DBG] Exception detail for {msg}:{Environment.NewLine}{ex.ToString()}");

    public static void Info(this ILogger log, string fmt, params object?[] args)
    {
        if (log is null) return;

        var msg = string.Format(fmt, args);
        log.LogInformation(msg);
        AppendTaskLogSafe(LogLevel.Information, "INFO", msg);
    }

    public static void Warn(this ILogger log, string fmt, params object?[] args)
    {
        if (log is null) return;

        var msg = string.Format(fmt, args);
        log.LogWarning(msg);
        AppendTaskLogSafe(LogLevel.Warning, "WARN", msg);
    }

    public static void Warn(this ILogger log, Exception ex, string fmt, params object?[] args)
    {
        if (log is null) return;

        var msg = string.Format(fmt, args);
        log.LogWarning(ex, msg);
        AppendTaskLogSafe(LogLevel.Warning, "WARN", msg, ex);
        AppendRawExceptionDetailSafe(msg, ex);
    }

    public static void Err(this ILogger log, Exception ex, string fmt, params object?[] args)
    {
        if (log is null) return;

        var msg = string.Format(fmt, args);
        log.LogError(ex, msg);
        AppendTaskLogSafe(LogLevel.Error, "ERROR", msg, ex);
        AppendRawExceptionDetailSafe(msg, ex);
    }

    public static void Debug(this ILogger log, string fmt, params object?[] args)
    {
        if (log is null) return;

        var cfg = Plugin.Instance?.Configuration;
        var uiDebug = cfg?.EnableDebugLogs == true;
        var rawSupport = cfg?.EnableRawSupportLogs == true;
        if (!uiDebug && !rawSupport) return;

        var msg = string.Format(fmt, args);

        if (log.IsEnabled(LogLevel.Debug))
            log.LogDebug(msg);

        AppendTaskLogSafe(LogLevel.Debug, "DBG", msg);
    }
}

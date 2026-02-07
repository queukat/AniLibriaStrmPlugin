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
            // В обычном плагине Instance есть,
            // в тестах — null, тогда просто выходим.
            var plugin = Plugin.Instance;
            if (plugin is null)
                return;

            plugin.AppendTaskLog(WithLevel(levelLabel, msg), level);
        }
        catch
        {
            // Логирование НЕ должно ронять логику — гасим любые ошибки.
        }
    }

    private static void AppendTaskLogSafe(LogLevel level, string levelLabel, string msg, Exception ex)
        => AppendTaskLogSafe(level, levelLabel, $"{msg} — {ex.Message}");

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
    }

    public static void Err(this ILogger log, Exception ex, string fmt, params object?[] args)
    {
        if (log is null) return;

        var msg = string.Format(fmt, args);
        log.LogError(ex, msg);
        AppendTaskLogSafe(LogLevel.Error, "ERROR", msg, ex);
    }

    public static void Debug(this ILogger log, string fmt, params object?[] args)
    {
        if (log is null) return;

        // Глобальный флажок “Debug logs” — если OFF, то не пишем debug вообще
        var cfg = Plugin.Instance?.Configuration;
        if (cfg?.EnableDebugLogs != true) return;

        if (!log.IsEnabled(LogLevel.Debug)) return;

        var msg = string.Format(fmt, args);
        log.LogDebug(msg);

        // DEBUG в UI-лог только при включённом EnableDebugLogs (и дальше ещё фильтруется UiMinLogLevel)
        AppendTaskLogSafe(LogLevel.Debug, "DBG", msg);
    }
}

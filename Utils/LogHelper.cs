using System;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Utils;

internal static class LogHelper
{
    private static string WithLevel(string level, string msg)
        => $"[{level}] {msg}";

    private static void AppendTaskLogSafe(string level, string msg)
    {
        try
        {
            // В обычном плагине Instance есть,
            // в тестах — null, тогда просто выходим.
            var plugin = Plugin.Instance;
            if (plugin is null)
                return;

            plugin.AppendTaskLog(WithLevel(level, msg));
        }
        catch
        {
            // Логирование НЕ должно ронять логику — гасим любые ошибки.
        }
    }

    private static void AppendTaskLogSafe(string level, string msg, Exception ex)
        => AppendTaskLogSafe(level, $"{msg} — {ex.Message}");

    public static void Info(this ILogger log, string fmt, params object?[] args)
    {
        if (log is null) return;

        var msg = string.Format(fmt, args);
        log.LogInformation(msg);
        AppendTaskLogSafe("INFO", msg);
    }

    public static void Warn(this ILogger log, string fmt, params object?[] args)
    {
        if (log is null) return;

        var msg = string.Format(fmt, args);
        log.LogWarning(msg);
        AppendTaskLogSafe("WARN", msg);
    }

    public static void Warn(this ILogger log, Exception ex, string fmt, params object?[] args)
    {
        if (log is null) return;

        var msg = string.Format(fmt, args);
        log.LogWarning(ex, msg);
        AppendTaskLogSafe("WARN", msg, ex);
    }

    public static void Err(this ILogger log, Exception ex, string fmt, params object?[] args)
    {
        if (log is null) return;

        var msg = string.Format(fmt, args);
        log.LogError(ex, msg);
        AppendTaskLogSafe("ERROR", msg, ex);
    }

    public static void Debug(this ILogger log, string fmt, params object?[] args)
    {
        if (log is null) return;
        if (!log.IsEnabled(LogLevel.Debug)) return;

        var msg = string.Format(fmt, args);
        log.LogDebug(msg);

        // Ты раньше писал, что хочешь «полные» логи — оставляем DEBUG в task-логе.
        AppendTaskLogSafe("DBG", msg);
    }
}

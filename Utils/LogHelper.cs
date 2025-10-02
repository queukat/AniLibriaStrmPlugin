using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Utils;

internal static class LogHelper
{
    private static string WithLevel(string level, string msg) => $"[{level}] {msg}";

    public static void Info(this ILogger log, string fmt, params object?[] args)
    {
        var msg = string.Format(fmt, args);
        log.LogInformation(msg);
        Plugin.Instance.AppendTaskLog(WithLevel("INFO", msg));
    }

    public static void Warn(this ILogger log, string fmt, params object?[] args)
    {
        var msg = string.Format(fmt, args);
        log.LogWarning(msg);
        Plugin.Instance.AppendTaskLog(WithLevel("WARN", msg));
    }

    public static void Warn(this ILogger log, Exception ex, string fmt, params object?[] args)
    {
        var msg = string.Format(fmt, args);
        log.LogWarning(ex, msg);
        Plugin.Instance.AppendTaskLog(WithLevel("WARN", $"{msg} — {ex.Message}"));
    }

    public static void Err(this ILogger log, Exception ex, string fmt, params object?[] args)
    {
        var msg = string.Format(fmt, args);
        log.LogError(ex, msg);
        Plugin.Instance.AppendTaskLog(WithLevel("ERROR", $"{msg} — {ex.Message}"));
    }

    public static void Debug(this ILogger log, string fmt, params object?[] args)
    {
        var msg = string.Format(fmt, args);
        log.LogDebug(msg);
        // хотим «полные» логи — показываем и отладку
        Plugin.Instance.AppendTaskLog(WithLevel("DBG", msg));
    }
}

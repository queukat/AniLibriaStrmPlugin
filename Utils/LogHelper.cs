namespace AniLibriaStrmPlugin.Utils;

using AniLibriaStrmPlugin;
using Microsoft.Extensions.Logging;

internal static class LogHelper
{
    public static void Info(this ILogger log, string fmt, params object?[] args)
    {
        var msg = string.Format(fmt, args);
        log.LogInformation(msg);
        Plugin.Instance.AppendTaskLog(msg);      //    «Last Task Logs»
    }

    public static void Warn(this ILogger log, string fmt, params object?[] args)
    {
        var msg = string.Format(fmt, args);
        log.LogWarning(msg);
        Plugin.Instance.AppendTaskLog("⚠ " + msg);
    }

    public static void Err(this ILogger log, Exception ex, string fmt, params object?[] args)
    {
        var msg = string.Format(fmt, args);
        log.LogError(ex, msg);
        Plugin.Instance.AppendTaskLog("ERROR: " + ex.Message);
    }
}
using AniLibertyStrmPlugin.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class LogHelperTests
{
    [Fact]
    public void ExtensionMethods_AppendUiAndRawLogsThroughPlugin()
    {
        using var host = PluginTestHost.Create(cfg =>
        {
            cfg.EnableDebugLogs = true;
            cfg.EnableRawSupportLogs = true;
            cfg.UiMinLogLevel = LogLevel.Debug;
        });
        var logger = NullLogger.Instance;

        logger.Info("hello {0}", "info");
        logger.Warn("hello {0}", "warn");
        logger.Warn(new InvalidOperationException("warn-ex"), "hello {0}", "warn-ex");
        logger.Err(new InvalidOperationException("err-ex"), "hello {0}", "err-ex");
        logger.Debug("hello {0}", "debug");

        Assert.Contains("[INFO] hello info", host.Plugin.Configuration.LastTaskLog);
        Assert.Contains("[WARN] hello warn", host.Plugin.Configuration.LastTaskLog);
        Assert.Contains("warn-ex", host.Plugin.Configuration.LastTaskLog);
        Assert.Contains("err-ex", host.Plugin.Configuration.LastTaskLog);
        Assert.Contains("[DBG] hello debug", host.Plugin.Configuration.LastTaskLog);
        Assert.Contains("Exception detail for hello warn-ex", host.Plugin.Configuration.LastRawTaskLog);
        Assert.Contains("Exception detail for hello err-ex", host.Plugin.Configuration.LastRawTaskLog);
    }

    [Fact]
    public void ExtensionMethods_ReturnWhenLoggerOrDebugConfigIsMissing()
    {
        ILogger? missing = null;
        missing!.Info("ignored");
        missing!.Warn("ignored");
        missing!.Warn(new InvalidOperationException("ignored"), "ignored");
        missing!.Err(new InvalidOperationException("ignored"), "ignored");
        missing!.Debug("ignored");

        using var host = PluginTestHost.Create(cfg =>
        {
            cfg.EnableDebugLogs = false;
            cfg.EnableRawSupportLogs = false;
        });

        NullLogger.Instance.Debug("hidden");

        Assert.Empty(host.Plugin.Configuration.LastTaskLog);
    }
}

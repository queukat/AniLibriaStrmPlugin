using AniLibertyStrmPlugin.Configuration;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class ConfigurationDefaultsTests
{
    [Fact]
    public void PluginConfiguration_DefaultsToSafeCleanupAndPlaybackProxy()
    {
        var config = new PluginConfiguration();

        Assert.Equal(StaleCleanupMode.DryRun, config.StaleCleanupMode);
        Assert.True(config.UseJellyfinPlaybackProxy);
        Assert.False(config.EnablePlaybackDiagnostics);
        Assert.Empty(config.CurrentOtpCode);
        Assert.Empty(config.LastRawTaskLog);
        Assert.False(config.EnableRawSupportLogs);
        Assert.Equal(20000, config.LastRawLogMaxLines);
        Assert.True(config.EnableAniLibertyPopularityBadge);
        Assert.True(config.EnableAniLibertyAuthNotifications);
        Assert.Equal(DateTime.MinValue, config.LastAniLibertyAuthExpiredNotificationUtc);
    }

    [Fact]
    public void PluginIdentity_UsesAniLibertyProductNameWithLegacyRepositoryUrl()
    {
        Assert.Equal("AniLiberty STRM Plugin", PluginIdentity.DisplayName);
        Assert.Equal("AniLibertyStrmPlugin", PluginIdentity.ProductToken);
        Assert.Equal("https://github.com/queukat/AniLibriaStrmPlugin", PluginIdentity.RepositoryUrl);
        Assert.Contains(PluginIdentity.ProductToken + "/", PluginIdentity.UserAgent);
        Assert.Contains("+https://github.com/queukat/AniLibriaStrmPlugin", PluginIdentity.UserAgent);
    }
}

using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Logging;

namespace AniLibertyStrmPlugin.Configuration;

public enum StaleCleanupMode
{
    Off = 0,
    DryRun = 1,
    Delete = 2
}

/// <summary>Plugin configuration for AniLiberty STRM v2.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public string StrmAllPath { get; set; } = @"D:\video\Anime\AniLibertySTRM";
    public string StrmFavoritesPath { get; set; } = @"D:\video\Anime\AniLibertySTRMFavorites";
    public string PreferredResolution { get; set; } = "1080";

    /// <summary>JWT token for AniLiberty API v1.</summary>
    public string AniLibertyToken { get; set; } = string.Empty;

    public bool EnableFavorites { get; set; } = true;
    public bool EnableAll { get; set; } = true;

    /* --- OTP/auth state --- */
    public string AniDeviceId { get; set; } = string.Empty;
    public string CurrentOtpCode { get; set; } = string.Empty;
    public string LastTaskLog { get; set; } = string.Empty;

    /* --- Paging settings --- */
    public int AllTitlesPageSize { get; set; } = 50;
    public int AllTitlesMaxPages { get; set; } = 100;
    public int FavoritesPageSize { get; set; } = 50;
    public int FavoritesMaxPages { get; set; } = 50;

    // Minimum level for messages to be included in LastTaskLog (UI).
    public LogLevel UiMinLogLevel { get; set; } = LogLevel.Information;

    // Whether to enable verbose debug/trace logs (including detailed title progress).
    public bool EnableDebugLogs { get; set; } = false;

    // Playback diagnostics: detailed logs for HLS URLs and .strm files.
    public bool EnablePlaybackDiagnostics { get; set; } = false;

    // Cleanup for generated files that are no longer present in the latest API response.
    public StaleCleanupMode StaleCleanupMode { get; set; } = StaleCleanupMode.DryRun;

    // Route playback through a local Jellyfin HLS proxy instead of exposing AniLiberty CDN URLs directly.
    public bool UseJellyfinPlaybackProxy { get; set; } = true;

    // Optional manual override for the base URL used in generated playback proxy .strm links.
    public string JellyfinPlaybackProxyBaseUrl { get; set; } = string.Empty;

    // Push episode watch progress to AniLiberty (/accounts/users/me/views/timecodes).
    public bool EnableAniLibertyViewSync { get; set; } = false;

    // Minimum progress delta (seconds) to avoid spamming the API.
    public int AniLibertyViewSyncMinDeltaSeconds { get; set; } = 30;

    // Force sending progress when playback stops.
    public bool AniLibertyViewSyncOnStop { get; set; } = true;

    // For pull sync: Jellyfin UserId to import AniLiberty progress into.
    public string AniLibertyViewSyncJellyfinUserId { get; set; } = string.Empty;

    // Number of lines to keep in LastTaskLog.
    public int LastLogMaxLines { get; set; } = 800; // recommended: 800-1500
}

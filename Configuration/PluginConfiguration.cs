using MediaBrowser.Model.Plugins;

namespace AniLibriaStrmPlugin.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string StrmAllPath { get; set; } = @"D:\video\Anime\AniLibriaSTRM";
    public string StrmFavoritesPath { get; set; } = @"D:\video\Anime\AniLibriaSTRMFavorites";
    public string PreferredResolution { get; set; } = "1080";

    /// <summary>JWT-токен авторизации AniLibria API v1.</summary>
    public string AniLibriaToken { get; set; } = string.Empty;

    public bool EnableFavorites { get; set; } = true;
    public bool EnableAll { get; set; } = true;

    /// <summary> WebSocket-трансляции для STRM-обновлений.</summary>
    public bool EnableRealtimeUpdates { get; set; } = true;

    // --- вспомогательные поля ---
    public string AniDeviceId   { get; set; } = string.Empty;
    public string CurrentOtpCode { get; set; } = string.Empty;
    public string LastTaskLog    { get; set; } = string.Empty;

    // --- лимиты ---
    public int AllTitlesPageSize   { get; set; } = 50;
    public int AllTitlesMaxPages   { get; set; } = 100;
    public int FavoritesPageSize   { get; set; } = 50;
    public int FavoritesMaxPages   { get; set; } = 50;
}
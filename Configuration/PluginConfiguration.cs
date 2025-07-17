using MediaBrowser.Model.Plugins;

namespace AniLibertyStrmPlugin.Configuration;

/// <summary>Конфигурация плагина (AniLiberty STRM v2).</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public string StrmAllPath       { get; set; } = @"D:\video\Anime\AniLibertySTRM";
    public string StrmFavoritesPath { get; set; } = @"D:\video\Anime\AniLibertySTRMFavorites";
    public string PreferredResolution { get; set; } = "1080";

    /// <summary>JWT-токен авторизации AniLiberty API v1.</summary>
    public string AniLibertyToken { get; set; } = string.Empty;

    public bool EnableFavorites { get; set; } = true;
    public bool EnableAll       { get; set; } = true;

    /* --- вспомогательные поля (остались) --- */
    public string AniDeviceId     { get; set; } = string.Empty;
    public string CurrentOtpCode  { get; set; } = string.Empty;
    public string LastTaskLog     { get; set; } = string.Empty;

    /* --- лимиты --- */
    public int AllTitlesPageSize { get; set; }   = 50;
    public int AllTitlesMaxPages { get; set; }   = 100;
    public int FavoritesPageSize { get; set; }   = 50;
    public int FavoritesMaxPages { get; set; }   = 50;
}

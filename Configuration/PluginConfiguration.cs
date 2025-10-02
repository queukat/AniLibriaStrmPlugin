using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Logging;
namespace AniLibertyStrmPlugin.Configuration;

/// <summary>  (AniLiberty STRM v2).</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public string StrmAllPath       { get; set; } = @"D:\video\Anime\AniLibertySTRM";
    public string StrmFavoritesPath { get; set; } = @"D:\video\Anime\AniLibertySTRMFavorites";
    public string PreferredResolution { get; set; } = "1080";

    /// <summary>JWT-  AniLiberty API v1.</summary>
    public string AniLibertyToken { get; set; } = string.Empty;

    public bool EnableFavorites { get; set; } = true;
    public bool EnableAll       { get; set; } = true;

    /* ---   () --- */
    public string AniDeviceId     { get; set; } = string.Empty;
    public string CurrentOtpCode  { get; set; } = string.Empty;
    public string LastTaskLog     { get; set; } = string.Empty;

    /* ---  --- */
    public int AllTitlesPageSize { get; set; }   = 50;
    public int AllTitlesMaxPages { get; set; }   = 100;
    public int FavoritesPageSize { get; set; }   = 50;
    public int FavoritesMaxPages { get; set; }   = 50;
    
    // Минимальный уровень для попадания сообщений в LastTaskLog (UI)
    public LogLevel UiMinLogLevel { get; set; } = LogLevel.Information;

    // Сколько строк хранить в LastTaskLog
    public int LastLogMaxLines { get; set; } = 800;  // можно 800–1500
}

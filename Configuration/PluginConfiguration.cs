using MediaBrowser.Model.Plugins;

namespace AniLibriaStrmPlugin.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string StrmAllPath        { get; set; } = @"D:\video\Anime\AniLibriaSTRM";
        public string StrmFavoritesPath  { get; set; } = @"D:\video\Anime\AniLibriaSTRMFavorites";
        public string PreferredResolution{ get; set; } = "1080";

        /// <summary>PHPSESSID,     .</summary>
        public string AniLibriaSession   { get; set; } = string.Empty;

        /// <summary> STRM   ,   .</summary>
        public bool   TrackFavoritesOnly { get; set; } = false;

        /// <summary> WebSocket   STRM- « ».</summary>
        public bool   EnableRealtimeUpdates { get; set; } = true;

        // ---   ---
        public string AniDeviceId    { get; set; } = string.Empty;
        public string CurrentOtpCode { get; set; } = string.Empty;
        public string LastTaskLog    { get; set; } = string.Empty;

        // ---  ---
        public int AllTitlesPageSize    { get; set; } = 50;
        public int AllTitlesMaxPages    { get; set; } = 100;
        public int FavoritesPageSize    { get; set; } = 50;
        public int FavoritesMaxPages    { get; set; } = 50;
    }
}

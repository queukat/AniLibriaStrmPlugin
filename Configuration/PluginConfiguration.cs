using MediaBrowser.Model.Plugins;

namespace AniLibriaStrmPlugin.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string StrmAllPath { get; set; } = @"D:\video\Anime\AniLibriaSTRM";
        public string StrmFavoritesPath { get; set; } = @"D:\video\Anime\AniLibriaSTRMFavorites";
        public string PreferredResolution { get; set; } = "1080";
        public string AniLibriaSession { get; set; } = string.Empty;

        // DeviceId,   OTP (   "authHolder.getDeviceId()")
        public string AniDeviceId { get; set; } = string.Empty;

        //  OTP,    auth_get_otp (  )
        public string CurrentOtpCode { get; set; } = string.Empty;

        //     (   )
        public string LastTaskLog { get; set; } = string.Empty;

        // =============     / =============
        //   " " (AniLibriaAllTask)
        public int AllTitlesPageSize { get; set; } = 50;      //   (limit)
        public int AllTitlesMaxPages { get; set; } = 100;     //   ( )

        //   "" (AniLibriaFavoritesTask)
        public int FavoritesPageSize { get; set; } = 50;      //    
        public int FavoritesMaxPages { get; set; } = 50;      //   ( )
    }
}

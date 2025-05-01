using AniLibriaStrmPlugin.Tasks;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace AniLibriaStrmPlugin;

public class AniLibriaServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(
        IServiceCollection services,
        IServerApplicationHost applicationHost) // ← 2-  
    {
        // 1)  HttpClientFactory
        services.AddHttpClient("AniLibria", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(300);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Jellyfin-AniLibriaStrm/1.0");
        });

        // 2)  
        services.AddTransient<IAniLibriaClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient("AniLibria");
            var log = sp.GetRequiredService<ILogger<AniLibriaClient>>();
            return new AniLibriaClient(http, log);
        });

        // 3)  singletons
        services.AddSingleton<IAniLibriaStrmGenerator,
            AniLibriaStrmGenerator>();

        services.AddSingleton<IScheduledTask, AniLibriaAllTask>();
        services.AddSingleton<IScheduledTask, AniLibriaFavoritesTask>();

        services.AddHostedService<AniLibriaRealtimeWatcher>();
    }
}

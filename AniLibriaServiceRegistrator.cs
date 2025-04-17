using AniLibriaStrmPlugin.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using System;
using MediaBrowser.Model.Tasks;

namespace AniLibriaStrmPlugin;

public class AniLibriaServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        // 1) HttpClient + Polly retry + User‑Agent
        services.AddHttpClient<IAniLibriaClient, AniLibriaClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-AniLibriaStrm/1.0");
        })
        .AddPolicyHandler(HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(2 << i))); // 2s, 4s, 8s

        // 2)  STRM
        services.AddSingleton<IAniLibriaStrmGenerator, AniLibriaStrmGenerator>();

        // 3)  
        services.AddSingleton<IScheduledTask, AniLibriaAllTask>();
        services.AddSingleton<IScheduledTask, AniLibriaFavoritesTask>();

        // 4)  
        services.AddSingleton<AniLibriaAuthController>();
    }
}

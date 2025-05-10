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
        
        // HttpClient with retry
        services.AddHttpClient("AniLibria", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(300);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-AniLibriaStrm/1.0");
            })
            .AddPolicyHandler(PolicyHelpers.GetRetryPolicy());

        // AniLibriaClient
        services.AddTransient<IAniLibriaClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("AniLibria");
            var log = sp.GetRequiredService<ILogger<AniLibriaClient>>();
            return new AniLibriaClient(http, log);
        });

        // singletons
        services.AddSingleton<IAniLibriaStrmGenerator, AniLibriaStrmGenerator>();
        services.AddSingleton<IScheduledTask, AniLibriaAllTask>();
        services.AddSingleton<IScheduledTask, AniLibriaFavoritesTask>();
        services.AddHostedService<AniLibriaRealtimeWatcher>();
    }
}

internal static class PolicyHelpers
{
    private static readonly Random _rnd = new();

    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, attempt =>
                TimeSpan.FromSeconds(Math.Pow(2, attempt)) +
                TimeSpan.FromMilliseconds(_rnd.Next(0, 1000)));
}
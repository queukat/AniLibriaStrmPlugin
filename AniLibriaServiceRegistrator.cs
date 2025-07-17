using AniLibriaStrmPlugin.Tasks;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace AniLibriaStrmPlugin;

/// <summary>DI-регистрация сервисов для Jellyfin 10.11.</summary>
public class AniLibriaServiceRegistrator : IPluginServiceRegistrator
{
    void IPluginServiceRegistrator.RegisterServices(IServiceCollection services, IServerApplicationHost _)
        => Register(services);

    private static void Register(IServiceCollection services)
    {
        /* ---- HttpClient с retry + UA ---- */
        services.AddHttpClient("AniLiberty", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(300);
                c.DefaultRequestHeaders.UserAgent
                       .ParseAdd("Jellyfin-AniLibertyStrm/1.0");      
            })
            .AddPolicyHandler(PolicyHelpers.GetRetryPolicy());

        /* ---- AniLibriaClient ---- */
        services.AddTransient<IAniLibriaClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("AniLiberty");
            var log  = sp.GetRequiredService<ILogger<AniLibriaClient>>();
            return new AniLibriaClient(http, log);                      
        });

        /* ---- singletons / tasks ---- */
        services.AddSingleton<IAniLibriaStrmGenerator, AniLibriaStrmGenerator>();
        services.AddSingleton<IScheduledTask, AniLibriaAllTask>();
        services.AddSingleton<IScheduledTask, AniLibriaFavoritesTask>();
        
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

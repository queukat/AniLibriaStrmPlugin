using AniLibertyStrmPlugin.Tasks;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace AniLibertyStrmPlugin;

/// <summary>DI-регистрация сервисов для Jellyfin 10.11.</summary>
public class AniLibertyServiceRegistrator : IPluginServiceRegistrator
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
                    .ParseAdd("Jellyfin-AniLibertyStrm/2.0 (+https://github.com/queukat/AniLibertyStrmPlugin)");      
            })
            .AddPolicyHandler(PolicyHelpers.GetRetryPolicy());

        /* ---- AniLibertyClient ---- */
        services.AddTransient<IAniLibertyClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("AniLiberty");
            var log  = sp.GetRequiredService<ILogger<AniLibertyClient>>();
            return new AniLibertyClient(http, log);                      
        });

        /* ---- singletons / tasks ---- */
        services.AddSingleton<IAniLibertyStrmGenerator, AniLibertyStrmGenerator>();
        services.AddSingleton<IScheduledTask, AniLibertyAllTask>();
        services.AddSingleton<IScheduledTask, AniLibertyFavoritesTask>();
        
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

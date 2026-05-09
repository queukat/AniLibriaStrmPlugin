using System.Net;
using AniLibertyStrmPlugin.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace AniLibertyStrmPlugin;

/// <summary>DI wiring for Jellyfin 10.11.</summary>
public class AniLibertyServiceRegistrator : IPluginServiceRegistrator
{
    void IPluginServiceRegistrator.RegisterServices(IServiceCollection services, IServerApplicationHost _)
    {
        Register(services);
    }

    private static void Register(IServiceCollection services)
    {
        /* ---- HttpClient retry + UA ---- */
        services.AddHttpClient("AniLiberty", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(300);
                c.DefaultRequestHeaders.UserAgent.ParseAdd(PluginIdentity.UserAgent);
                c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru,en;q=0.8");
            })
            .AddPolicyHandler(PolicyHelpers.GetRetryPolicy());

        services.AddHttpClient("AniLibertyMediaProxy", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(120);
                c.DefaultRequestHeaders.UserAgent.ParseAdd(PluginIdentity.UserAgent);
                c.DefaultRequestHeaders.Accept.ParseAdd("*/*");
                c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru,en;q=0.8");
            })
            .AddPolicyHandler(PolicyHelpers.GetRetryPolicy());

        /* ---- AniLibertyClient ---- */
        services.AddTransient<IAniLibertyClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("AniLiberty");
            var log = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AniLibertyClient>>();
            return new AniLibertyClient(http, log);
        });

        /* ---- singletons / tasks ---- */
        services.AddSingleton<IAniLibertyStrmGenerator, AniLibertyStrmGenerator>();
        services.AddSingleton<IScheduledTask, AniLibertyAllTask>();
        services.AddSingleton<IScheduledTask, AniLibertyFavoritesTask>();
        services.AddSingleton<IScheduledTask, AniLibertyViewsPullTask>();
        services.AddSingleton<IHostedService, AniLibertyViewSyncHostedService>();
    }
}

internal static class PolicyHelpers
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests) // 429
            .WaitAndRetryAsync(
                3,
                attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)) +
                    TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)));
    }
}

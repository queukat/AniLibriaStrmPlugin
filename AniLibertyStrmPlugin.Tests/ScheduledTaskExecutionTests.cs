using AniLibertyStrmPlugin.Configuration;
using AniLibertyStrmPlugin.Models;
using AniLibertyStrmPlugin.Tasks;
using AniLibertyStrmPlugin.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class ScheduledTaskExecutionTests
{
    [Fact]
    public async Task AllTask_SkipsDisabledAndEmptyPathBeforeFetching()
    {
        using var disabledHost = PluginTestHost.Create(cfg => cfg.EnableAll = false);
        var disabledClient = new StubClient();
        var disabledGenerator = new StubGenerator();
        var disabledTask = NewAllTask(disabledClient, disabledGenerator);

        await disabledTask.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(0, disabledClient.FetchAllCalls);
        Assert.Equal(0, disabledGenerator.GenerateCalls);

        using var emptyPathHost = PluginTestHost.Create(cfg =>
        {
            cfg.EnableAll = true;
            cfg.StrmAllPath = " ";
        });
        var emptyClient = new StubClient();
        var emptyGenerator = new StubGenerator();
        var emptyTask = NewAllTask(emptyClient, emptyGenerator);

        await emptyTask.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(0, emptyClient.FetchAllCalls);
        Assert.Equal(0, emptyGenerator.GenerateCalls);
    }

    [Fact]
    public async Task AllTask_FetchesTitlesAndRunsGenerator()
    {
        using var host = PluginTestHost.Create(cfg =>
        {
            cfg.EnableAll = true;
        });
        var cfg = host.Plugin.Configuration;
        cfg.StrmAllPath = Path.Combine(host.Root, "all");
        cfg.AllTitlesPageSize = 2;
        cfg.AllTitlesMaxPages = 3;
        cfg.PreferredResolution = "720";
        host.Plugin.UpdateConfiguration(cfg);

        var titles = new List<ReleaseResponse> { new() { Id = 1 }, new() { Id = 2 } };
        var client = new StubClient { AllTitles = titles };
        var generator = new StubGenerator();
        var task = NewAllTask(client, generator);

        await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, client.FetchAllCalls);
        Assert.Equal((2, 3), client.LastAllPaging);
        Assert.Equal(1, generator.GenerateCalls);
        Assert.Same(titles, generator.LastTitles);
        Assert.Equal(cfg.StrmAllPath, generator.LastBasePath);
        Assert.Equal("720", generator.LastResolution);
    }

    [Fact]
    public async Task FavoritesTask_ValidatesConfigurationBeforeFetching()
    {
        using var disabledHost = PluginTestHost.Create(cfg => cfg.EnableFavorites = false);
        var disabledClient = new StubClient();
        var disabledGenerator = new StubGenerator();
        await NewFavoritesTask(disabledClient, disabledGenerator).ExecuteAsync(new Progress<double>(), CancellationToken.None);
        Assert.Equal(0, disabledClient.FetchFavoritesCalls);

        using var noTokenHost = PluginTestHost.Create(cfg =>
        {
            cfg.EnableFavorites = true;
            cfg.AniLibertyToken = " ";
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewFavoritesTask(new StubClient(), new StubGenerator()).ExecuteAsync(new Progress<double>(), CancellationToken.None));

        using var emptyPathHost = PluginTestHost.Create(cfg =>
        {
            cfg.EnableFavorites = true;
            cfg.AniLibertyToken = "token";
            cfg.StrmFavoritesPath = " ";
        });
        var emptyClient = new StubClient();
        await NewFavoritesTask(emptyClient, new StubGenerator()).ExecuteAsync(new Progress<double>(), CancellationToken.None);
        Assert.Equal(0, emptyClient.FetchFavoritesCalls);
    }

    [Fact]
    public async Task FavoritesTask_FetchesFavoritesGeneratesAndUpdatesCache()
    {
        using var host = PluginTestHost.Create(cfg =>
        {
            cfg.EnableFavorites = true;
            cfg.AniLibertyToken = "token";
            cfg.FavoritesPageSize = 4;
            cfg.FavoritesMaxPages = 5;
            cfg.PreferredResolution = "480";
        });
        var cfg = host.Plugin.Configuration;
        cfg.StrmFavoritesPath = Path.Combine(host.Root, "favorites");
        host.Plugin.UpdateConfiguration(cfg);

        var titles = new List<ReleaseResponse> { new() { Id = 91 }, new() { Id = 92 } };
        var client = new StubClient { Favorites = titles };
        var generator = new StubGenerator();

        await NewFavoritesTask(client, generator).ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, client.FetchFavoritesCalls);
        Assert.Equal(("token", 4, 5), client.LastFavoritesPaging);
        Assert.Equal(1, generator.GenerateCalls);
        Assert.Same(titles, generator.LastTitles);
        Assert.True(FavoritesCache.Contains(91));
        Assert.True(FavoritesCache.Contains(92));
    }

    [Fact]
    public async Task FavoritesTask_NotifiesWhenAniLibertyAuthExpired()
    {
        using var host = PluginTestHost.Create(cfg =>
        {
            cfg.EnableFavorites = true;
            cfg.AniLibertyToken = "expired-token";
        });
        var cfg = host.Plugin.Configuration;
        cfg.StrmFavoritesPath = Path.Combine(host.Root, "favorites");
        host.Plugin.UpdateConfiguration(cfg);

        var client = new StubClient { ThrowFavoritesAuthExpired = true };
        var notifications = new RecordingAuthNotifications();
        var task = new AniLibertyFavoritesTask(
            client,
            new StubGenerator(),
            NullLogger<AniLibertyFavoritesTask>.Instance,
            notifications);

        await Assert.ThrowsAsync<AniLibertyAuthExpiredException>(() =>
            task.ExecuteAsync(new Progress<double>(), CancellationToken.None));

        Assert.Equal(1, client.FetchFavoritesCalls);
        var notification = Assert.Single(notifications.Notifications);
        Assert.Equal("Favorites catalogue update", notification.Source);
    }

    [Fact]
    public async Task ViewsPullTask_SkipsWhenTokenMissingOrConfiguredUserIdInvalid()
    {
        using var noTokenHost = PluginTestHost.Create(cfg => cfg.AniLibertyToken = " ");
        var noTokenClient = new StubClient();
        var noTokenTask = new AniLibertyViewsPullTask(
            client: noTokenClient,
            library: null!,
            userDataManager: null!,
            userManager: null!,
            log: NullLogger<AniLibertyViewsPullTask>.Instance);

        await noTokenTask.ExecuteAsync(new Progress<double>(), CancellationToken.None);
        Assert.Equal(0, noTokenClient.FetchViewTimecodesCalls);

        using var invalidUserHost = PluginTestHost.Create(cfg =>
        {
            cfg.AniLibertyToken = "token";
            cfg.AniLibertyViewSyncJellyfinUserId = "not-guid";
        });
        var invalidUserClient = new StubClient();
        var invalidUserTask = new AniLibertyViewsPullTask(
            client: invalidUserClient,
            library: null!,
            userDataManager: null!,
            userManager: null!,
            log: NullLogger<AniLibertyViewsPullTask>.Instance);

        await invalidUserTask.ExecuteAsync(new Progress<double>(), CancellationToken.None);
        Assert.Equal(0, invalidUserClient.FetchViewTimecodesCalls);
    }

    private static AniLibertyAllTask NewAllTask(IAniLibertyClient client, IAniLibertyStrmGenerator generator)
        => new(client, generator, NullLogger<AniLibertyAllTask>.Instance);

    private static AniLibertyFavoritesTask NewFavoritesTask(IAniLibertyClient client, IAniLibertyStrmGenerator generator)
        => new(client, generator, NullLogger<AniLibertyFavoritesTask>.Instance);

    private sealed class StubClient : IAniLibertyClient
    {
        public List<ReleaseResponse> AllTitles { get; init; } = [];
        public List<ReleaseResponse> Favorites { get; init; } = [];
        public bool ThrowFavoritesAuthExpired { get; init; }
        public int FetchAllCalls { get; private set; }
        public int FetchFavoritesCalls { get; private set; }
        public int FetchViewTimecodesCalls { get; private set; }
        public (int PageSize, int MaxPages) LastAllPaging { get; private set; }
        public (string Token, int PageSize, int MaxPages) LastFavoritesPaging { get; private set; }

        public Task<string> GetStringWithLoggingAsync(string url, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> GetStringAuthAsync(string url, string bearer, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<ReleaseResponse>> FetchAllTitlesAsync(int pageSize, int maxPages, CancellationToken ct)
        {
            FetchAllCalls++;
            LastAllPaging = (pageSize, maxPages);
            return Task.FromResult(AllTitles);
        }

        public Task<List<ReleaseResponse>> FetchFavoritesAsync(string bearerToken, int pageSize, int maxPages, CancellationToken ct)
        {
            FetchFavoritesCalls++;
            LastFavoritesPaging = (bearerToken, pageSize, maxPages);
            if (ThrowFavoritesAuthExpired)
                throw new AniLibertyAuthExpiredException(System.Net.HttpStatusCode.Forbidden, "https://api.test/favorites", "{}");

            return Task.FromResult(Favorites);
        }

        public Task<bool> UpdateViewTimecodesAsync(string bearerToken, IEnumerable<ViewTimecodeUpdateItem> updates, CancellationToken ct)
            => Task.FromResult(true);

        public Task<List<ViewTimecodeEntry>> FetchViewTimecodesAsync(string bearerToken, DateTimeOffset? since, CancellationToken ct)
        {
            FetchViewTimecodesCalls++;
            return Task.FromResult(new List<ViewTimecodeEntry>());
        }

        public Task<ReleaseResponse?> FetchReleaseByIdAsync(int id, CancellationToken ct)
            => Task.FromResult<ReleaseResponse?>(null);

        public Task<List<FranchiseInfo>?> FetchFranchisesForReleaseAsync(int releaseId, CancellationToken ct)
            => Task.FromResult<List<FranchiseInfo>?>(null);
    }

    private sealed class StubGenerator : IAniLibertyStrmGenerator
    {
        public int GenerateCalls { get; private set; }
        public IEnumerable<ReleaseResponse>? LastTitles { get; private set; }
        public string? LastBasePath { get; private set; }
        public string? LastResolution { get; private set; }

        public Task GenerateTitlesAsync(
            IEnumerable<ReleaseResponse> titles,
            string basePath,
            string resolution,
            IProgress<double>? progress,
            CancellationToken token)
        {
            GenerateCalls++;
            LastTitles = titles;
            LastBasePath = basePath;
            LastResolution = resolution;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAuthNotifications : IAniLibertyAuthNotificationService
    {
        public List<(string Source, AniLibertyAuthExpiredException Exception)> Notifications { get; } = [];

        public Task NotifyAuthExpiredAsync(
            string source,
            AniLibertyAuthExpiredException exception,
            CancellationToken cancellationToken)
        {
            Notifications.Add((source, exception));
            return Task.CompletedTask;
        }
    }
}

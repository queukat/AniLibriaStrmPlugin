using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using AniLibertyStrmPlugin.Media;
using AniLibertyStrmPlugin.Utils;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public sealed class AniLibertyMediaSegmentLifecycleTests
{
    [Fact]
    public async Task Provider_ReturnsCompleteSetOnRepeatedRunsAndAfterTimingChanges()
    {
        using var host = PluginTestHost.Create();
        var root = host.Plugin.Configuration.StrmAllPath;
        var episode = new Episode { Id = Guid.NewGuid(), Path = Path.Combine(root, "one.strm") };
        var library = Proxy<ILibraryManager>((method, _) => method.Name == "GetItemById" ? episode : Default(method.ReturnType));
        var provider = new AniLibertyMediaSegmentProvider(library, new(), NullLogger<AniLibertyMediaSegmentProvider>.Instance);
        var state = State(root, episode.Path);
        await state.SaveAsync(CancellationToken.None);
        var first = await provider.GetMediaSegments(new() { ItemId = episode.Id, ExistingSegments = [] }, CancellationToken.None);
        var repeated = await provider.GetMediaSegments(new() { ItemId = episode.Id, ExistingSegments = first }, CancellationToken.None);
        Assert.Single(repeated);
        Assert.Equal(10, repeated[0].StartTicks);

        state.Track(episode.Path, 1, "episode", [
            new() { Type = "Intro", StartTicks = 20, EndTicks = 30 },
            new() { Type = "Outro", StartTicks = 100, EndTicks = 120 }]);
        await state.SaveAsync(CancellationToken.None);
        var changed = await provider.GetMediaSegments(new() { ItemId = episode.Id, ExistingSegments = first }, CancellationToken.None);
        Assert.Collection(changed,
            intro => { Assert.Equal(MediaSegmentType.Intro, intro.Type); Assert.Equal(20, intro.StartTicks); },
            outro => { Assert.Equal(MediaSegmentType.Outro, outro.Type); Assert.Equal(100, outro.StartTicks); });
        var unchanged = await provider.GetMediaSegments(new() { ItemId = episode.Id, ExistingSegments = changed }, CancellationToken.None);
        Assert.Equal(changed.Select(x => (x.Type, x.StartTicks, x.EndTicks)), unchanged.Select(x => (x.Type, x.StartTicks, x.EndTicks)));

        // A successfully read empty source is authoritative, unlike a read failure.
        await new AniLibertyMediaSegmentState(root).SaveAsync(CancellationToken.None);
        Assert.Empty(await provider.GetMediaSegments(new() { ItemId = episode.Id, ExistingSegments = changed }, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_UnavailableSourceThrowsInsteadOfReturningAnEmptyReplacement(bool malformed)
    {
        using var host = PluginTestHost.Create();
        var root = host.Plugin.Configuration.StrmAllPath;
        var episode = new Episode { Id = Guid.NewGuid(), Path = Path.Combine(root, "one.strm") };
        var state = State(root, episode.Path);
        if (malformed)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(state.StatePath)!);
            await File.WriteAllTextAsync(state.StatePath, "{broken");
        }
        var library = Proxy<ILibraryManager>((method, _) => method.Name == "GetItemById" ? episode : Default(method.ReturnType));
        var provider = new AniLibertyMediaSegmentProvider(library, new(), NullLogger<AniLibertyMediaSegmentProvider>.Instance);
        var request = new MediaSegmentGenerationRequest
        {
            ItemId = episode.Id,
            ExistingSegments = [new MediaSegmentDto { ItemId = episode.Id, Type = MediaSegmentType.Intro, StartTicks = 10, EndTicks = 20 }]
        };
        await Assert.ThrowsAsync<IOException>(() => provider.GetMediaSegments(request, CancellationToken.None));
        Assert.Single(request.ExistingSegments);
    }

    [Fact]
    public async Task Index_RetriesFailedReadWithoutAFileChangeOrInvalidation()
    {
        using var host = PluginTestHost.Create();
        var root = host.Plugin.Configuration.StrmAllPath;
        var state = State(root, Path.Combine(root, "one.strm"));
        await state.SaveAsync(CancellationToken.None);
        var timestamp = File.GetLastWriteTimeUtc(state.StatePath);
        var index = new AniLibertyMediaSegmentIndex();
        using (var locked = new FileStream(state.StatePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Null(await index.GetSnapshotForRootAsync(root, CancellationToken.None));

        Assert.NotNull(await index.GetSnapshotForRootAsync(root, CancellationToken.None));
        Assert.Equal(2, index.LoadCount);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(state.StatePath));
        Assert.NotNull(await index.GetSnapshotForRootAsync(root, CancellationToken.None));
        Assert.Equal(2, index.LoadCount);
    }

    [Fact]
    public async Task ChangedSource_RefreshesExistingSegmentsButPeriodicRepairDoesNot()
    {
        using var host = PluginTestHost.Create();
        var root = host.Plugin.Configuration.StrmAllPath;
        var episode = new Episode { Id = Guid.NewGuid(), Path = Path.Combine(root, "one.strm") };
        await State(root, episode.Path).SaveAsync(CancellationToken.None);
        var library = Proxy<ILibraryManager>((method, _) => method.Name switch
        {
            "GetItemList" => new BaseItem[] { episode },
            "GetItemById" => episode,
            _ => Default(method.ReturnType)
        });
        var calls = 0;
        var manager = Proxy<IMediaSegmentManager>((method, args) =>
        {
            if (method.Name == "HasSegments") return true;
            if (method.Name == "RunSegmentPluginProviders")
            {
                Assert.False((bool)args![2]!); // Never delete other providers via forceOverwrite.
                calls++;
                return Task.CompletedTask;
            }
            return Default(method.ReturnType);
        });
        using var service = Service(library, manager);
        try
        {
            Invoke(service, "RequestFullPass", TimeSpan.FromHours(1), "PeriodicMissingSegments", null, false);
            await (Task)Invoke(service, "WarmupWorkerAsync")!;
            Assert.Equal(0, calls);

            Invoke(service, "OnMediaSegmentStateSaved", root, 1);
            // Coalescing a weaker request must preserve the required refresh.
            Invoke(service, "RequestFullPass", TimeSpan.FromHours(1), "PeriodicMissingSegments", null, false);
            await (Task)Invoke(service, "WarmupWorkerAsync")!;
            Assert.Equal(1, calls);

            Invoke(service, "RequestFullPass", TimeSpan.FromHours(1), "PeriodicMissingSegments", null, false);
            await (Task)Invoke(service, "WarmupWorkerAsync")!;
            Assert.Equal(1, calls);
        }
        finally { await service.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Save_UnchangedContentPreservesBytesTimestampAndCachedSnapshot()
    {
        using var host = PluginTestHost.Create();
        var root = host.Plugin.Configuration.StrmAllPath;
        var path = Path.Combine(root, "Show", "S01E01.strm");
        var notifications = 0;
        void OnSaved(string savedRoot, int _) { if (savedRoot == root) notifications++; }
        AniLibertyMediaSegmentState.Saved += OnSaved;
        try
        {
            var first = State(root, path);
            await first.SaveAsync(CancellationToken.None);
            var bytes = await File.ReadAllBytesAsync(first.StatePath);
            var timestamp = File.GetLastWriteTimeUtc(first.StatePath);
            var index = new AniLibertyMediaSegmentIndex();
            var snapshot = await index.GetSnapshotForPathAsync(path, CancellationToken.None);

            await State(root, path).SaveAsync(CancellationToken.None);

            Assert.Equal(bytes, await File.ReadAllBytesAsync(first.StatePath));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(first.StatePath));
            Assert.Equal(1, notifications);
            Assert.Same(snapshot, await index.GetSnapshotForPathAsync(path, CancellationToken.None));
            Assert.Equal(1, index.LoadCount);

            await State(root, path, 20).SaveAsync(CancellationToken.None);
            Assert.Equal(2, notifications);
            Assert.Equal(20, (await index.TryGetEntryForPathAsync(path, CancellationToken.None))!.Segments[0].StartTicks);

            await new AniLibertyMediaSegmentState(root).SaveAsync(CancellationToken.None);
            Assert.Equal(3, notifications);
            Assert.Null(await index.TryGetEntryForPathAsync(path, CancellationToken.None));
        }
        finally
        {
            AniLibertyMediaSegmentState.Saved -= OnSaved;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedSource_ReconcilesRemovedTimingsIncludingTheLastEntry(bool lastEntryRemoved)
    {
        using var host = PluginTestHost.Create();
        var root = host.Plugin.Configuration.StrmAllPath;
        var episode = new Episode { Id = Guid.NewGuid(), Path = Path.Combine(root, "removed.strm") };
        await State(root, episode.Path).SaveAsync(CancellationToken.None);
        var current = lastEntryRemoved ? new AniLibertyMediaSegmentState(root) : State(root, Path.Combine(root, "other.strm"));
        await current.SaveAsync(CancellationToken.None);
        var library = Proxy<ILibraryManager>((method, _) => method.Name switch
        {
            "GetItemList" => new BaseItem[] { episode },
            "GetItemById" => episode,
            _ => Default(method.ReturnType)
        });
        var provider = new AniLibertyMediaSegmentProvider(library, new(), NullLogger<AniLibertyMediaSegmentProvider>.Instance);
        var calls = 0;
        var manager = Proxy<IMediaSegmentManager>((method, _) =>
        {
            if (method.Name == "HasSegments") return calls == 0;
            if (method.Name == "RunSegmentPluginProviders") return Reconcile();
            return Default(method.ReturnType);
        });
        async Task Reconcile()
        {
            calls++;
            Assert.Empty(await provider.GetMediaSegments(new()
            {
                ItemId = episode.Id,
                ExistingSegments = [new() { Type = MediaSegmentType.Intro, StartTicks = 10, EndTicks = 20 }]
            }, CancellationToken.None));
        }
        using var service = Service(library, manager);
        try
        {
            Invoke(service, "OnMediaSegmentStateSaved", root, lastEntryRemoved ? 0 : 1);
            await (Task)Invoke(service, "WarmupWorkerAsync")!;
            Assert.Equal(1, calls);
            Assert.True((bool)Invoke(service, "CanRetryMissingItem", episode.Id, DateTime.UtcNow)!);
            Invoke(service, "RequestFullPass", TimeSpan.FromHours(1), "PeriodicMissingSegments", null, false);
            await (Task)Invoke(service, "WarmupWorkerAsync")!;
            Assert.Equal(1, calls);
        }
        finally { await service.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public void SemanticComparison_IgnoresAccountingAndOrderButNotAuthority()
    {
        var before = new AniLibertyMediaSegmentDocument
        {
            Entries = [Entry("one.strm", 1), Entry("two.strm", 2)]
        };
        var after = JsonSerializer.Deserialize<AniLibertyMediaSegmentDocument>(JsonSerializer.Serialize(before))!;
        Array.Reverse(after.Entries);
        after.GeneratedAtUtc = before.GeneratedAtUtc.AddDays(1);
        after.Entries[0].LastSeenUtc = DateTimeOffset.UtcNow.AddDays(2);
        Assert.True(AniLibertyMediaSegmentState.HasSameContent(before, after));
        after.Entries[0].ReleaseEpisodeId = "changed";
        Assert.False(AniLibertyMediaSegmentState.HasSameContent(before, after));
        after.Entries[0].ReleaseEpisodeId = null;
        after.Entries[0].Segments[0].EndTicks++;
        Assert.False(AniLibertyMediaSegmentState.HasSameContent(before, after));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Repair_ScopesQueryAndSkipsSegmentDatabaseForMissingSourceEntries(bool rootIsImportedFolder)
    {
        using var host = PluginTestHost.Create();
        var root = host.Plugin.Configuration.StrmAllPath;
        var folder = new Folder { Id = Guid.NewGuid(), Path = root };
        var present = new Episode { Id = Guid.NewGuid(), Path = Path.Combine(root, "with.strm") };
        var absent = new Episode { Id = Guid.NewGuid(), Path = Path.Combine(root, "without.strm") };
        await State(root, present.Path).SaveAsync(CancellationToken.None);
        var queries = new List<InternalItemsQuery>();
        var segmentChecks = new List<Guid>();
        var providers = 0;
        var library = Proxy<ILibraryManager>((method, args) => method.Name switch
        {
            "GetItemList" => Query((InternalItemsQuery)args![0]!),
            "GetItemById" => present,
            _ => Default(method.ReturnType)
        });
        IReadOnlyList<BaseItem> Query(InternalItemsQuery query)
        {
            queries.Add(query);
            if (query.Path == root) return rootIsImportedFolder ? [folder] : [];
            return [present, absent];
        }
        var manager = Proxy<IMediaSegmentManager>((method, args) =>
        {
            if (method.Name == "HasSegments")
            {
                segmentChecks.Add((Guid)args![0]!);
                return providers > 0;
            }
            if (method.Name == "RunSegmentPluginProviders") { providers++; return Task.CompletedTask; }
            return Default(method.ReturnType);
        });
        using var service = Service(library, manager);
        try
        {
            Invoke(service, "RequestFullPass", TimeSpan.FromHours(1), "ScanCompleted", null, false);
            await (Task)Invoke(service, "WarmupWorkerAsync")!;
            Assert.Equal(1, providers);
            Assert.DoesNotContain(absent.Id, segmentChecks);
            Assert.Equal(rootIsImportedFolder ? new[] { folder.Id } : [], queries.Single(q => q.Path is null).AncestorIds);
            Assert.DoesNotContain(queries, q => q.Path == host.Plugin.Configuration.StrmFavoritesPath);

            // A later reconciliation still checks source-backed episodes, repairing a lost event.
            providers = 0;
            Invoke(service, "RequestFullPass", TimeSpan.FromHours(1), "PeriodicMissingSegments", null, false);
            await (Task)Invoke(service, "WarmupWorkerAsync")!;
            Assert.Equal(1, providers);
        }
        finally { await service.StopAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Repair_NoSourceEntriesDoesNotQueryJellyfin(bool emptyStateExists)
    {
        using var host = PluginTestHost.Create();
        if (emptyStateExists)
            await new AniLibertyMediaSegmentState(host.Plugin.Configuration.StrmAllPath).SaveAsync(CancellationToken.None);
        var queries = 0;
        var library = Proxy<ILibraryManager>((method, _) =>
        {
            if (method.Name == "GetItemList") { queries++; return Array.Empty<BaseItem>(); }
            return Default(method.ReturnType);
        });
        using var service = Service(library, null!);
        try
        {
            Invoke(service, "RequestFullPass", TimeSpan.FromHours(1), "PeriodicMissingSegments", null, false);
            await (Task)Invoke(service, "WarmupWorkerAsync")!;
            Assert.Equal(0, queries);
        }
        finally { await service.StopAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData(null, "root-a", true)]
    [InlineData(null, "root-a", false)]
    [InlineData("root-a", "root-b", true)]
    [InlineData("root-a", "root-b", false)]
    public async Task Requests_CoalesceDifferentRootsAndDoNotNarrowGlobalRepair(
        string? firstRoot, string secondRoot, bool active)
    {
        using var service = Service(Proxy<ILibraryManager>((method, _) => Default(method.ReturnType)), null!);
        try
        {
            Invoke(service, "RequestFullPass", TimeSpan.FromHours(1), "Startup", firstRoot, false);
            if (active) Invoke(service, "TryBeginFullPass");
            Invoke(service, "RequestFullPass", TimeSpan.FromHours(1), "Changed", secondRoot, false);
            Assert.Null(typeof(AniLibertyMediaSegmentWarmupHostedService)
                .GetField("_fullPassRootPath", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service));
        }
        finally { await service.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task ChangedState_RetriesOnlyAffectedRootAndPreservesOtherCooldowns()
    {
        using var host = PluginTestHost.Create();
        var all = new Episode { Id = Guid.NewGuid(), Path = Path.Combine(host.Plugin.Configuration.StrmAllPath, "one.strm") };
        var favorite = new Episode { Id = Guid.NewGuid(), Path = Path.Combine(host.Plugin.Configuration.StrmFavoritesPath, "two.strm") };
        await State(host.Plugin.Configuration.StrmAllPath, all.Path).SaveAsync(CancellationToken.None);
        await State(host.Plugin.Configuration.StrmFavoritesPath, favorite.Path).SaveAsync(CancellationToken.None);
        var providerCalls = new List<Guid>();
        var library = Proxy<ILibraryManager>((method, args) => method.Name == "GetItemById"
            ? (Guid)args![0]! == all.Id ? all : favorite
            : Default(method.ReturnType));
        var manager = Proxy<IMediaSegmentManager>((method, args) =>
        {
            if (method.Name == "HasSegments") return false;
            if (method.Name == "RunSegmentPluginProviders")
            {
                providerCalls.Add(((BaseItem)args![0]!).Id);
                return Task.CompletedTask;
            }
            return Default(method.ReturnType);
        });
        using var service = Service(library, manager);
        var pending = (ConcurrentDictionary<Guid, byte>)typeof(AniLibertyMediaSegmentWarmupHostedService)
            .GetField("_pendingItemIds", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!;
        try
        {
            pending[all.Id] = 0;
            pending[favorite.Id] = 0;
            await (Task)Invoke(service, "ProcessPendingItemsAsync", "Initial", CancellationToken.None)!;
            Assert.Equal(2, providerCalls.Count);

            Invoke(service, "OnMediaSegmentStateSaved", host.Plugin.Configuration.StrmAllPath, 1);
            pending[all.Id] = 0;
            pending[favorite.Id] = 0;
            await (Task)Invoke(service, "ProcessPendingItemsAsync", "Changed", CancellationToken.None)!;
            Assert.Equal(3, providerCalls.Count);
            Assert.Equal(all.Id, providerCalls[2]);
        }
        finally { await service.StopAsync(CancellationToken.None); }
    }

    private static AniLibertyMediaSegmentState State(string root, string path, long start = 10)
    {
        var state = new AniLibertyMediaSegmentState(root);
        state.Track(path, 1, "episode", [new() { Type = "Intro", StartTicks = start, EndTicks = start + 10 }]);
        return state;
    }

    private static AniLibertyMediaSegmentEntry Entry(string path, int id) => new()
    {
        RelativePath = path,
        ReleaseId = id,
        Segments = [new() { Type = "Intro", StartTicks = 10, EndTicks = 20 }]
    };

    private static AniLibertyMediaSegmentWarmupHostedService Service(ILibraryManager library, IMediaSegmentManager manager)
        => new(library, manager, new(), NullLogger<AniLibertyMediaSegmentWarmupHostedService>.Instance);

    private static object? Invoke(object instance, string name, params object?[] arguments)
        => instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, arguments);

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)proxy).Call = call;
        return proxy;
    }

    private static object? Default(Type type) => type == typeof(void) ? null :
        type == typeof(Task) ? Task.CompletedTask : type.IsValueType ? Activator.CreateInstance(type) : null;

    public class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args);
    }
}

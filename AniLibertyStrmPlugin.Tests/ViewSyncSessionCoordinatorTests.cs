using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AniLibertyStrmPlugin.Utils;
using Xunit;

namespace AniLibertyStrmPlugin.Tests;

public class ViewSyncSessionCoordinatorTests
{
    [Fact]
    public async Task Dispose_CancelsShutdownTokenAndDisposesCreatedGates()
    {
        var coordinator = new ViewSyncSessionCoordinator();

        Assert.True(await coordinator.TrySendAsync(
            "episode|session",
            10,
            5,
            isStopEvent: false,
            _ => Task.FromResult(true)));

        coordinator.Dispose();
    }

    [Fact]
    public async Task TrySendAsync_SerializesConcurrentSendsPerSession()
    {
        var coordinator = new ViewSyncSessionCoordinator();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();

        var first = coordinator.TrySendAsync(
            "episode|session",
            10,
            5,
            isStopEvent: false,
            async _ =>
            {
                lock (calls) calls.Add("start1");
                firstStarted.SetResult();
                await allowFirstToFinish.Task;
                lock (calls) calls.Add("end1");
                return true;
            });

        await firstStarted.Task;

        var second = coordinator.TrySendAsync(
            "episode|session",
            20,
            5,
            isStopEvent: false,
            _ =>
            {
                lock (calls)
                {
                    calls.Add("start2");
                    calls.Add("end2");
                }

                secondStarted.SetResult();
                return Task.FromResult(true);
            });

        await Task.Delay(100);
        Assert.False(secondStarted.Task.IsCompleted);

        allowFirstToFinish.SetResult();

        Assert.True(await first);
        Assert.True(await second);
        Assert.True(secondStarted.Task.IsCompleted);
        Assert.Equal(["start1", "end1", "start2", "end2"], calls);
    }

    [Fact]
    public async Task TrySendAsync_RechecksMinDeltaAfterQueuedSend()
    {
        var coordinator = new ViewSyncSessionCoordinator();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sendCount = 0;

        var first = coordinator.TrySendAsync(
            "episode|session",
            100,
            30,
            isStopEvent: false,
            async _ =>
            {
                Interlocked.Increment(ref sendCount);
                firstStarted.SetResult();
                await allowFirstToFinish.Task;
                return true;
            });

        await firstStarted.Task;

        var second = coordinator.TrySendAsync(
            "episode|session",
            110,
            30,
            isStopEvent: false,
            _ =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.FromResult(true);
            });

        allowFirstToFinish.SetResult();

        Assert.True(await first);
        Assert.False(await second);
        Assert.Equal(1, sendCount);
    }

    [Fact]
    public async Task CancelPending_CancelsInFlightSend()
    {
        var coordinator = new ViewSyncSessionCoordinator();
        var senderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var sendTask = coordinator.TrySendAsync(
            "episode|session",
            10,
            5,
            isStopEvent: false,
            async ct =>
            {
                senderStarted.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return true;
            });

        await senderStarted.Task;

        coordinator.CancelPending();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sendTask);
    }
}

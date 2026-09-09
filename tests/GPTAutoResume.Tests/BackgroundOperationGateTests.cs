using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class BackgroundOperationGateTests
{
    [Fact]
    public async Task TimeoutCancelsProvider()
    {
        var gate = new BackgroundOperationGate();
        var providerSawCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var result = await gate.RunAsync(async token =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
            }
            catch (OperationCanceledException)
            {
                providerSawCancellation.SetResult();
                throw;
            }

            return 1;
        }, TimeSpan.FromMilliseconds(30));

        await providerSawCancellation.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(BackgroundOperationStatus.TimedOut, result.Status);
    }

    [Fact]
    public async Task AlreadyRunningPreventsTaskAccumulation()
    {
        var gate = new BackgroundOperationGate();
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        var first = gate.RunAsync(async _ =>
        {
            Interlocked.Increment(ref started);
            await releaseFirst.Task;
            return 1;
        }, TimeSpan.FromSeconds(5));
        await WaitUntil(() => Volatile.Read(ref started) == 1);

        var second = await gate.RunAsync(async _ =>
        {
            await Task.Yield();
            Interlocked.Increment(ref started);
            return 2;
        }, TimeSpan.FromSeconds(5));

        releaseFirst.SetResult();
        await first;

        Assert.Equal(BackgroundOperationStatus.AlreadyRunning, second.Status);
        Assert.Equal(1, started);
    }

    [Fact]
    public async Task TimedOutOperationStillBlocksNewWorkUntilProviderStops()
    {
        var gate = new BackgroundOperationGate();
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = await gate.RunAsync(async token =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
            }
            catch (OperationCanceledException)
            {
                await releaseProvider.Task;
                throw;
            }

            return 1;
        }, TimeSpan.FromMilliseconds(30));

        var second = await gate.RunAsync(_ => Task.FromResult(2), TimeSpan.FromSeconds(1));
        releaseProvider.SetResult();
        await WaitUntil(async () => (await gate.RunAsync(_ => Task.FromResult(3), TimeSpan.FromSeconds(1))).Status == BackgroundOperationStatus.Completed);

        Assert.Equal(BackgroundOperationStatus.TimedOut, first.Status);
        Assert.Equal(BackgroundOperationStatus.AlreadyRunning, second.Status);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, cts.Token);
        }
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!await condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, cts.Token);
        }
    }
}

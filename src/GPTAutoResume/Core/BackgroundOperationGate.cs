namespace GPTAutoResume.Core;

public enum BackgroundOperationStatus
{
    Completed,
    TimedOut,
    AlreadyRunning,
    Failed
}

public sealed record BackgroundOperationResult<T>(BackgroundOperationStatus Status, T? Value);

public sealed class BackgroundOperationGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<BackgroundOperationResult<T>> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken))
        {
            return new BackgroundOperationResult<T>(BackgroundOperationStatus.AlreadyRunning, default);
        }

        var releaseInFinally = true;
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var task = operation(timeoutSource.Token);
            var completed = await Task.WhenAny(task, Task.Delay(timeout, timeoutSource.Token));
            if (completed != task)
            {
                timeoutSource.Cancel();
                releaseInFinally = false;
                _ = task.ContinueWith(_ => _semaphore.Release(), TaskScheduler.Default);
                return new BackgroundOperationResult<T>(BackgroundOperationStatus.TimedOut, default);
            }

            return new BackgroundOperationResult<T>(BackgroundOperationStatus.Completed, await task);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new BackgroundOperationResult<T>(BackgroundOperationStatus.TimedOut, default);
        }
        catch
        {
            return new BackgroundOperationResult<T>(BackgroundOperationStatus.Failed, default);
        }
        finally
        {
            if (releaseInFinally)
            {
                _semaphore.Release();
            }
        }
    }
}

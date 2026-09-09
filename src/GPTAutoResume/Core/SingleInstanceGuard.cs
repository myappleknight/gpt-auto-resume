namespace GPTAutoResume.Core;

public sealed class SingleInstanceGuard : IDisposable
{
    public const string AppMutexName = "Local\\GPTAutoResume.App";
    private readonly Mutex? _mutex;

    private SingleInstanceGuard(Mutex? mutex, bool acquired)
    {
        _mutex = mutex;
        Acquired = acquired;
    }

    public bool Acquired { get; }

    public static SingleInstanceGuard TryAcquire(string mutexName = AppMutexName)
    {
        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstanceGuard(mutex, acquired: true);
        }

        mutex.Dispose();
        return new SingleInstanceGuard(null, acquired: false);
    }

    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // The process may be shutting down after ownership was already released.
        }

        _mutex.Dispose();
    }
}

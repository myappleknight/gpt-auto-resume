using System.IO;
using System.Text.Json;

namespace GPTAutoResume.Core;

public sealed class JsonEventStore : IEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string EventStoreMutexName = "Local\\GPTAutoResume.EventStore";
    private static readonly TimeSpan UnconfirmedClaimTtl = TimeSpan.FromSeconds(90);
    private readonly string _path;
    private Dictionary<string, EventRecord> _records;
    private bool _loadFailed;

    public JsonEventStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GPTAutoResume");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "events.json");
        _records = Load(out _loadFailed);
    }

    public JsonEventStore(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        _path = path;
        _records = Load(out _loadFailed);
    }

    public bool HasResumeAttempt(string eventId)
    {
        using var mutex = AcquireStoreMutex();
        RefreshFromDisk();
        return _loadFailed || IsAttempted(eventId);
    }

    public void MarkResumeAttempted(string eventId, DateTimeOffset attemptedAt, bool sent)
    {
        using var mutex = AcquireStoreMutex();
        RefreshFromDisk();
        if (_loadFailed)
        {
            throw new InvalidDataException("The resume event journal is corrupt. Automatic resume is blocked until the journal is repaired or removed.");
        }

        if (!sent && IsAttempted(eventId))
        {
            throw new InvalidOperationException("This resume event has already been claimed.");
        }

        _records[eventId] = new EventRecord(true, attemptedAt, sent || IsSent(eventId));
        WriteAtomic();
    }

    private void RefreshFromDisk()
    {
        _records = Load(out _loadFailed);
    }

    private Dictionary<string, EventRecord> Load(out bool failed)
    {
        failed = false;
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, EventRecord>>(File.ReadAllText(_path)) ?? [];
        }
        catch
        {
            failed = true;
            return [];
        }
    }

    private sealed record EventRecord(bool ResumeAttempted, DateTimeOffset AttemptedAt, bool Sent);

    private bool IsAttempted(string eventId) =>
        _records.TryGetValue(eventId, out var record)
        && record.ResumeAttempted
        && (record.Sent || DateTimeOffset.Now - record.AttemptedAt <= UnconfirmedClaimTtl);

    private bool IsSent(string eventId) =>
        _records.TryGetValue(eventId, out var record) && record.Sent;

    private void WriteAtomic()
    {
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_records, JsonOptions));
        File.Move(tempPath, _path, overwrite: true);
    }

    private static MutexLease AcquireStoreMutex()
    {
        var mutex = new Mutex(initiallyOwned: false, EventStoreMutexName);
        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.FromSeconds(3));
            if (!acquired)
            {
                throw new TimeoutException("Timed out waiting for the resume event journal lock.");
            }

            return new MutexLease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private sealed class MutexLease(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}

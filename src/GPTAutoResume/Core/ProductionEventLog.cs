using System.IO;
using System.Text.Json;

namespace GPTAutoResume.Core;

public sealed record ProductionTrace(DateTimeOffset At, string Stage, string Outcome,
    int? ProcessId = null, string? Hwnd = null, DateTimeOffset? RetryAt = null,
    string? IdentityHash = null, int? Confidence = null);

public interface IProductionEventLog
{
    void Write(ProductionTrace entry);
}

public sealed class ProductionEventLog : IProductionEventLog
{
    private static readonly object Gate = new();
    private readonly string _path;
    public ProductionEventLog(string? path = null) => _path = path ?? Path.Combine(LocalAppPaths.AppDataRoot, "production-events.jsonl");

    public void Write(ProductionTrace entry)
    {
        // Diagnostics must neither authorize a send nor break monitoring if storage fails.
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                if (File.Exists(_path) && new FileInfo(_path).Length > 512 * 1024)
                    File.Move(_path, _path + ".previous", overwrite: true);
                File.AppendAllText(_path, JsonSerializer.Serialize(entry) + Environment.NewLine);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class NullProductionEventLog : IProductionEventLog
{
    public void Write(ProductionTrace entry) { }
}

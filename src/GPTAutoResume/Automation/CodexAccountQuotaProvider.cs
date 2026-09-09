using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GPTAutoResume.Core;

namespace GPTAutoResume.Automation;

public interface IAccountQuotaProvider
{
    QuotaSnapshot? Read(bool forceRefresh = false, CancellationToken cancellationToken = default);
}

// Only the documented account RPC is used. Authentication remains owned by Codex.
public sealed class CodexAccountQuotaProvider : IAccountQuotaProvider
{
    public static CodexAccountQuotaProvider Shared { get; } = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private QuotaSnapshot? _cached;
    private DateTimeOffset _attemptedAt;

    public QuotaSnapshot? Read(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(9));
        var entered = false;
        try
        {
            _gate.Wait(timeout.Token);
            entered = true;
            if (!forceRefresh && DateTimeOffset.UtcNow - _attemptedAt < TimeSpan.FromSeconds(60))
                return _cached;
            _attemptedAt = DateTimeOffset.UtcNow;
            _cached = null;
            var executable = FindExecutable();
            if (executable is null) return null;
            // CLI diagnostics can call from WPF's dispatcher; isolate the entire async transport.
            _cached = Task.Run(() => QueryAsync(executable, timeout.Token), timeout.Token).GetAwaiter().GetResult();
            return _cached;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or JsonException
            or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (entered) _gate.Release();
        }
    }

    private static string? FindExecutable()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var direct = Path.Combine(directory.Trim('"'), "codex.exe");
            if (File.Exists(direct)) return direct;
        }
        var packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "node_modules", "@openai", "codex", "node_modules", "@openai");
        if (!Directory.Exists(packageRoot)) return null;
        return Directory.EnumerateFiles(packageRoot, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static async Task<QuotaSnapshot?> QueryAsync(string executable, CancellationToken token)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable, "app-server")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        }};
        process.Start();
        var drain = DrainAsync(process.StandardError);
        try
        {
            await process.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"gpt_auto_resume\",\"version\":\"0.1.0\"}}}".AsMemory(), token);
            if (await ReadResponseAsync(process.StandardOutput, 1, token) is null) return null;
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}".AsMemory(), token);
            await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\"}".AsMemory(), token);
            var result = await ReadResponseAsync(process.StandardOutput, 2, token);
            return result is null ? null : Parse(result, DateTimeOffset.Now);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await drain;
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer) > 0) { }
    }

    private static async Task<string?> ReadResponseAsync(StreamReader reader, int id, CancellationToken token)
    {
        for (var count = 0; count < 100; count++)
        {
            var line = await reader.ReadLineAsync(token);
            if (line is null) return null;
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.ValueKind != JsonValueKind.Number
                || !responseId.TryGetInt32(out var value) || value != id)
                continue;
            return root.TryGetProperty("result", out var result) ? result.GetRawText() : null;
        }
        return null;
    }

    public static QuotaSnapshot? Parse(string json, DateTimeOffset capturedAt)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        JsonElement bucket;
        if (root.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object)
        {
            if (!buckets.TryGetProperty("codex", out bucket)) return null;
        }
        else if (!root.TryGetProperty("rateLimits", out bucket)) return null;
        if (bucket.ValueKind != JsonValueKind.Object) return null;
        if (bucket.TryGetProperty("limitId", out var limitId) && limitId.ValueKind == JsonValueKind.String
            && limitId.GetString() != "codex") return null;
        int? shortRemaining = null, weeklyRemaining = null;
        DateTimeOffset? shortReset = null, weeklyReset = null;
        foreach (var name in new[] { "primary", "secondary" })
        {
            if (!bucket.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object
                || !window.TryGetProperty("windowDurationMins", out var duration) || duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt32(out var minutes)
                || !window.TryGetProperty("usedPercent", out var percent) || percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out var used)
                || !double.IsFinite(used) || used < 0 || used > 100) continue;
            var remaining = (int)Math.Ceiling(100 - used);
            DateTimeOffset? resetAt = null;
            if (window.TryGetProperty("resetsAt", out var reset) && reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var seconds)
                && seconds >= 0 && seconds <= 253402300799)
                resetAt = DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();
            if (minutes == 300) { shortRemaining = remaining; shortReset = resetAt; }
            if (minutes == 10080) { weeklyRemaining = remaining; weeklyReset = resetAt; }
        }
        return shortRemaining is null && weeklyRemaining is null ? null
            : new QuotaSnapshot(shortRemaining, shortReset, weeklyRemaining, weeklyReset, capturedAt);
    }
}

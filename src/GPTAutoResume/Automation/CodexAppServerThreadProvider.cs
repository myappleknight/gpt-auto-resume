using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GPTAutoResume.Automation;

public sealed record CodexThreadRecord(
    string Id,
    string? SessionId,
    string? Name,
    string? Preview,
    string? SourceKind,
    string? StatusType,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CodexThreadListResult(
    IReadOnlyList<CodexThreadRecord> Threads,
    IReadOnlyList<string> LoadedThreadIds,
    bool ThreadListReadable,
    bool LoadedListReadable,
    string? Error);

public sealed class CodexAppServerThreadProvider
{
    private static readonly string[] SourceKinds =
    [
        "cli",
        "vscode",
        "exec",
        "appServer",
        "subAgent",
        "subAgentReview",
        "subAgentCompact",
        "subAgentThreadSpawn",
        "subAgentOther",
        "unknown"
    ];

    public CodexThreadListResult ReadThreads(CancellationToken cancellationToken = default) =>
        ReadThreads([], cancellationToken);

    public CodexThreadListResult ReadThreads(IEnumerable<string> searchTerms, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var executable = FindExecutable();
            if (executable is null)
                return new CodexThreadListResult([], [], false, false, "codex.exe not found");

            return Task.Run(() => QueryAsync(executable, searchTerms, timeout.Token), timeout.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or JsonException
            or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return new CodexThreadListResult([], [], false, false, ex.GetType().Name);
        }
    }

    public static IReadOnlyList<CodexThreadRecord> ParseThreadListResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var records = new List<CodexThreadRecord>();
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            records.Add(new CodexThreadRecord(
                id,
                ReadString(item, "sessionId"),
                ReadString(item, "name"),
                ReadString(item, "preview"),
                ReadString(item, "sourceKind"),
                item.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object
                    ? ReadString(status, "type") : null,
                ReadUnixTime(item, "createdAt"),
                ReadUnixTime(item, "updatedAt")));
        }

        return records;
    }

    public static IReadOnlyList<string> ParseLoadedListResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];
        return data.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private static async Task<CodexThreadListResult> QueryAsync(string executable, IEnumerable<string> searchTerms, CancellationToken token)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable, "app-server")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        var drain = DrainAsync(process.StandardError);
        try
        {
            await process.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"gpt_auto_resume_identity_audit\",\"version\":\"0.1.0\"},\"capabilities\":{\"experimentalApi\":true}}}".AsMemory(), token);
            if (await ReadResponseAsync(process.StandardOutput, 1, token) is null)
                return new CodexThreadListResult([], [], false, false, "initialize failed");
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}".AsMemory(), token);

            await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"thread/loaded/list\",\"params\":{}}".AsMemory(), token);
            string? loadedJson = null;
            try
            {
                using var loadedTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                loadedTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                loadedJson = await ReadResponseAsync(process.StandardOutput, 2, loadedTimeout.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }

            var threads = new List<CodexThreadRecord>();
            var queryTerms = searchTerms.Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Distinct(StringComparer.Ordinal)
                .Take(20)
                .DefaultIfEmpty("")
                .ToArray();
            var nextId = 3;
            var successfulLists = 0;
            foreach (var term in queryTerms)
            {
                var id = nextId++;
                var listParams = JsonSerializer.Serialize(new
                {
                    cursor = (string?)null,
                    limit = 50,
                    sortKey = "updated_at",
                    sortDirection = "desc",
                    sourceKinds = SourceKinds,
                    useStateDbOnly = true,
                    searchTerm = string.IsNullOrWhiteSpace(term) ? null : term
                });
                await process.StandardInput.WriteLineAsync($"{{\"id\":{id},\"method\":\"thread/list\",\"params\":{listParams}}}".AsMemory(), token);
                string? listJson = null;
                try
                {
                    using var listTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    listTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                    listJson = await ReadResponseAsync(process.StandardOutput, id, listTimeout.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
                if (listJson is null) continue;
                successfulLists++;
                threads.AddRange(ParseThreadListResult(listJson));
            }

            return new CodexThreadListResult(
                threads.GroupBy(thread => thread.Id, StringComparer.Ordinal).Select(group => group.First()).ToArray(),
                loadedJson is null ? [] : ParseLoadedListResult(loadedJson),
                successfulLists > 0,
                loadedJson is not null,
                null);
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
        for (var count = 0; count < 200; count++)
        {
            var line = await reader.ReadLineAsync(token);
            if (line is null) return null;
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }
            using (document)
            {
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var responseId) || responseId.ValueKind != JsonValueKind.Number
                    || !responseId.TryGetInt32(out var value) || value != id)
                    continue;
                return root.TryGetProperty("result", out var result) ? result.GetRawText() : null;
            }
        }
        return null;
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
        return Directory.Exists(packageRoot)
            ? Directory.EnumerateFiles(packageRoot, "codex.exe", SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ReadUnixTime(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var seconds) || seconds < 0 || seconds > 253402300799)
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();
    }
}

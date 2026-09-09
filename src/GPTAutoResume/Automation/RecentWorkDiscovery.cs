using System.IO;

namespace GPTAutoResume.Automation;

public sealed record DiscoveredWork(string DisplayTitle, ConversationTargetIdentity Identity, DateTimeOffset SeenAt);

public sealed record RecentWorkDiscoveryResult(
    IReadOnlyList<DiscoveredWork> Works,
    IReadOnlyList<string> Diagnostics);

public static class RecentWorkDiscovery
{
    public static RecentWorkDiscoveryResult Discover(
        IWindowScanner scanner,
        UiAutomationReader reader,
        int maxTargets = 5,
        CancellationToken cancellationToken = default)
    {
        var works = new List<DiscoveredWork>();
        var diagnostics = new List<string>();

        foreach (var target in scanner.FindTargets().Take(maxTargets))
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add($"Target: PID={target.ProcessId}; HWND={target.Handle}; Title={target.Title}; Process={target.ProcessName}");
            var input = reader.FindChatInputForDiscovery(target.Handle);
            cancellationToken.ThrowIfCancellationRequested();
            var inputSummary = reader.DescribeElement(input);
            diagnostics.Add($"Input: {inputSummary}");
            var text = "";
            diagnostics.Add("VisibleTextLength: skipped");
            if (!LooksLikeWorkSurface(target.Title, text, inputSummary))
            {
                diagnostics.Add("Rejected: no Work/Codex signal.");
                continue;
            }

            var identity = reader.CaptureConversationIdentity(target.Handle);
            cancellationToken.ThrowIfCancellationRequested();
            if (identity is null || !identity.HasUsableSignal)
            {
                diagnostics.Add("Rejected: no usable ConversationTargetIdentity.");
                continue;
            }

            var title = reader.GetActiveWorkDisplayName(target.Handle);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(title))
            {
                title = BuildWorkTitle(target, text);
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                diagnostics.Add("Rejected: no display title.");
                continue;
            }

            diagnostics.Add($"Accepted: {title}");
            works.Add(new DiscoveredWork(title, identity, DateTimeOffset.Now));
        }

        return new RecentWorkDiscoveryResult(works, diagnostics);
    }

    public static void CaptureToFile(string root)
    {
        var diagnosticsDir = Path.Combine(root, "diagnostics");
        Directory.CreateDirectory(diagnosticsDir);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var result = Task.Run(() => Discover(new WindowScanner(), new UiAutomationReader(), cancellationToken: cts.Token), cts.Token)
                .WaitAsync(cts.Token)
                .GetAwaiter()
                .GetResult();
            File.WriteAllLines(Path.Combine(diagnosticsDir, "work-discovery.txt"), result.Diagnostics.Append($"Works: {result.Works.Count}"));
        }
        catch (OperationCanceledException)
        {
            File.WriteAllLines(Path.Combine(diagnosticsDir, "work-discovery.txt"),
                [
                    "Result: TIMEOUT",
                    "Work discovery exceeded 10 seconds. UI refresh will not block on this."
                ]);
        }
    }

    private static bool LooksLikeWorkSurface(string title, string text, string inputSummary)
    {
        var combined = $"{title}\n{text}\n{inputSummary}";
        return combined.Contains("Codex", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("Work", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("工作", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("一起工作", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildWorkTitle(TargetWindow target, string text)
    {
        var candidate = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length is >= 4 and <= 60
                && !IsGenericTitle(line)
                && !line.Contains("usage", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("使用上限", StringComparison.OrdinalIgnoreCase));
        if (candidate is not null)
        {
            return candidate;
        }

        return IsGenericTitle(target.Title) ? "__unnamed_work__" : target.Title;
    }

    private static bool IsGenericTitle(string title) =>
        string.Equals(title.Trim(), "ChatGPT", StringComparison.OrdinalIgnoreCase)
        || string.Equals(title.Trim(), "Codex", StringComparison.OrdinalIgnoreCase)
        || string.Equals(title.Trim(), "ChatGPT / Codex", StringComparison.OrdinalIgnoreCase);
}

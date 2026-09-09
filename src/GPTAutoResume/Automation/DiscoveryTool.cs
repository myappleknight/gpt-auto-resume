using System.IO;

namespace GPTAutoResume.Automation;

public static class DiscoveryTool
{
    public static DiscoveryReport CaptureToFile(string projectRoot)
    {
        var scanner = new WindowScanner();
        var reader = new UiAutomationReader();
        var windows = scanner.FindTargets();
        var diagnosticsDir = Path.Combine(projectRoot, "diagnostics");
        Directory.CreateDirectory(diagnosticsDir);
        var dumpPath = Path.Combine(diagnosticsDir, "uia-tree.txt");

        if (windows.Count == 0)
        {
            File.WriteAllText(dumpPath, "No ChatGPT/Codex target windows found.");
            return new DiscoveryReport(false, "", null, null, "", false, "NOT FOUND", "UNVERIFIED", dumpPath);
        }

        var chunks = new List<string>();
        DiscoveryReport? first = null;
        foreach (var window in windows)
        {
            chunks.Add($"# {window.ProcessName} [{window.ProcessId}] {window.Title}");
            var tree = reader.DumpTree(window.Handle);
            var text = reader.ReadVisibleText(window.Handle, out _);
            var editable = reader.FindChatInput(window.Handle);
            var editableFound = editable is not null;
            var messageTextStatus = ClassifyMessageTextAccess(text);
            chunks.Add(tree);
            first ??= new DiscoveryReport(
                true,
                window.ProcessName,
                window.ProcessId,
                window.Handle,
                window.Title,
                editableFound,
                reader.DescribeElement(editable),
                messageTextStatus,
                dumpPath);
        }

        File.WriteAllText(dumpPath, string.Join(Environment.NewLine, chunks));
        return first!;
    }

    private static string ClassifyMessageTextAccess(string text)
    {
        if (text.Contains("使用上限", StringComparison.OrdinalIgnoreCase)
            || text.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
            || text.Contains("try again", StringComparison.OrdinalIgnoreCase))
        {
            return "VERIFIED";
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            return "LIMITED";
        }

        return "UNVERIFIED";
    }
}

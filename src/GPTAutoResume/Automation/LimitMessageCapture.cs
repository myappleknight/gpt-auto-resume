using System.IO;
using System.Text.Json;
using GPTAutoResume.Core;

namespace GPTAutoResume.Automation;

public static class LimitMessageCapture
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Capture(string root)
    {
        var diagnosticsDir = Path.Combine(root, "diagnostics");
        Directory.CreateDirectory(diagnosticsDir);
        var outputPath = Path.Combine(diagnosticsDir, "capture-limit-message.txt");
        var scanner = new WindowScanner();
        var reader = new UiAutomationReader();
        var detector = new UsageLimitDetector(PatternCatalog.LoadDefault(), new RetryTimeParser());
        var now = DateTimeOffset.Now;

        foreach (var window in scanner.FindTargets())
        {
            var text = reader.ReadVisibleText(window.Handle, out var hasWarningRole);
            var result = detector.AnalyzeForAutomation(text, now, hasWarningRole, text.Contains('⚠') || text.Contains('!'));
            if (result.Kind != DetectionKind.LimitDetected || result.RetryAt is null)
            {
                continue;
            }

            var fixture = new CapturedLimitFixture(
                CapturedAt: now,
                ProcessName: window.ProcessName,
                ProcessId: window.ProcessId,
                WindowHandle: window.Handle.ToString(),
                WindowTitle: window.Title,
                RetryAt: result.RetryAt.Value,
                Confidence: result.Confidence,
                DetectedLanguage: result.DetectedLanguage ?? "unknown",
                MatchedPatternIds: result.MatchedPatternIds,
                MessageText: ExtractLimitSnippet(text),
                UiTree: reader.DumpTree(window.Handle));

            var fixtureDir = Path.Combine(root, "tests", "fixtures");
            Directory.CreateDirectory(fixtureDir);
            var fixturePath = Path.Combine(fixtureDir, $"real-usage-limit-{fixture.DetectedLanguage}.json");
            File.WriteAllText(fixturePath, JsonSerializer.Serialize(fixture, JsonOptions));
            File.WriteAllText(outputPath, $"Real usage-limit fixture captured: {fixturePath}");
            return outputPath;
        }

        File.WriteAllText(outputPath, "Real usage-limit capture: NOT YET. No high-confidence usage-limit message is visible now.");
        return outputPath;
    }

    private static string ExtractLimitSnippet(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Contains("使用上限", StringComparison.OrdinalIgnoreCase)
                || line.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
                || line.Contains("out of", StringComparison.OrdinalIgnoreCase)
                || line.Contains("usage to reset", StringComparison.OrdinalIgnoreCase)
                || line.Contains("add credits", StringComparison.OrdinalIgnoreCase)
                || line.Contains("upgrade", StringComparison.OrdinalIgnoreCase)
                || line.Contains("try again", StringComparison.OrdinalIgnoreCase)
                || line.Contains("再試", StringComparison.OrdinalIgnoreCase)
                || line.Contains("再试", StringComparison.OrdinalIgnoreCase)
                || line.Contains("再試行", StringComparison.OrdinalIgnoreCase)
                || line.Contains("升級方案", StringComparison.OrdinalIgnoreCase)
                || line.Contains("加值點數", StringComparison.OrdinalIgnoreCase)
                || line.Contains("清晨", StringComparison.OrdinalIgnoreCase))
            .Take(6);
        var snippet = string.Join(Environment.NewLine, lines);
        return snippet.Length <= 1_000 ? snippet : snippet[..1_000];
    }

    private sealed record CapturedLimitFixture(
        DateTimeOffset CapturedAt,
        string ProcessName,
        int ProcessId,
        string WindowHandle,
        string WindowTitle,
        DateTimeOffset RetryAt,
        int Confidence,
        string DetectedLanguage,
        IReadOnlyList<string> MatchedPatternIds,
        string MessageText,
        string UiTree);
}

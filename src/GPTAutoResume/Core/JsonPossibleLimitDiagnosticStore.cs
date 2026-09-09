using System.IO;
using System.Text.Json;
using GPTAutoResume.Automation;

namespace GPTAutoResume.Core;

public sealed class JsonPossibleLimitDiagnosticStore : IPossibleLimitDiagnosticStore
{
    private const int MaxSnapshots = 20;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;

    public JsonPossibleLimitDiagnosticStore()
        : this(LocalAppPaths.PossibleLimitDiagnosticsDirectory)
    {
    }

    public JsonPossibleLimitDiagnosticStore(string directory)
    {
        _directory = directory;
    }

    public void Save(TargetWindow window, string visibleText, DetectionResult result, bool hasWarningRole, string rejectionReason, DateTimeOffset capturedAt)
    {
        var signal = PossibleLimitHeuristics.Analyze(visibleText);
        if (!signal.IsInteresting)
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        var snapshot = new PossibleLimitDiagnosticSnapshot(
            CapturedAt: capturedAt,
            ProcessName: window.ProcessName,
            ProcessId: window.ProcessId,
            WindowHandle: window.Handle.ToString(),
            WindowTitle: window.Title,
            HasWarningRole: hasWarningRole,
            DetectorKind: result.Kind.ToString(),
            DetectorConfidence: result.Confidence,
            RetryAt: result.RetryAt,
            MatchedPatternIds: result.MatchedPatternIds,
            TextFragment: signal.TextFragment,
            DateTimeCandidate: signal.DateTimeCandidate,
            RejectionReason: rejectionReason);

        var path = Path.Combine(_directory, $"possible-limit-{capturedAt:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions));
        TrimOldSnapshots();
    }

    private void TrimOldSnapshots()
    {
        foreach (var file in Directory.GetFiles(_directory, "possible-limit-*.json")
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Skip(MaxSnapshots))
        {
            file.Delete();
        }
    }

    private sealed record PossibleLimitDiagnosticSnapshot(
        DateTimeOffset CapturedAt,
        string ProcessName,
        int ProcessId,
        string WindowHandle,
        string WindowTitle,
        bool HasWarningRole,
        string DetectorKind,
        int DetectorConfidence,
        DateTimeOffset? RetryAt,
        IReadOnlyList<string> MatchedPatternIds,
        string TextFragment,
        string DateTimeCandidate,
        string RejectionReason);
}

public sealed class NullPossibleLimitDiagnosticStore : IPossibleLimitDiagnosticStore
{
    public void Save(TargetWindow window, string visibleText, DetectionResult result, bool hasWarningRole, string rejectionReason, DateTimeOffset capturedAt)
    {
    }
}

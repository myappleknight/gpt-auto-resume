using System.Windows.Automation;
using GPTAutoResume.Automation;

namespace GPTAutoResume.Core;

public sealed record QuotaSimulationResult(
    string InputText,
    QuotaSnapshot Snapshot,
    AppState State,
    DateTimeOffset? RetryAt,
    string UsageLimitText);

public static class QuotaSimulationRunner
{
    public static QuotaSimulationResult Run(
        int shortWindowPercent,
        string shortWindowReset,
        int weeklyPercent,
        string weeklyReset,
        DateTimeOffset now)
    {
        var visibleText =
            $"""
            剩餘用量
            5 小時 {shortWindowPercent}% {shortWindowReset}
            1 週 {weeklyPercent}% {weeklyReset}
            """;
        var timeParser = new RetryTimeParser();
        var snapshot = QuotaSnapshotParser.Parse(visibleText, now, timeParser);
        var target = new TargetWindow(1, 100, "ChatGPT / Codex", "ChatGPT");
        var service = new MonitorService(
            new AppConfig
            {
                AutoResume = true,
                DryRun = true,
                SendEnter = false,
                AllowRealSubmit = false,
                ResumeDelaySeconds = 30,
                ResumePolicy = ResumePolicy.ConfirmBeforeResume,
                RequireWorkSelection = false
            },
            new SimulationScanner(target),
            new SimulationReader(target.Handle, visibleText),
            new SimulationSender(),
            new InMemoryEventStore(),
            new NullPossibleLimitDiagnosticStore(),
            new AllowAllWorkSelectionStore(),
            PatternCatalog.LoadDefault(),
            timeParser);

        service.Tick(now);
        return new QuotaSimulationResult(visibleText, snapshot, service.State, service.RetryAt, service.UsageLimitText);
    }

    private sealed class SimulationScanner(TargetWindow target) : IWindowScanner
    {
        public IReadOnlyList<TargetWindow> FindTargets() => [target];
        public bool IsStillValid(TargetWindow candidate) => candidate == target;
    }

    private sealed class SimulationReader(nint handle, string quotaText) : IUiAutomationReader, IAccountQuotaReader
    {
        public string ReadVisibleText(nint hwnd, out bool hasWarningRole)
        {
            hasWarningRole = false;
            return "";
        }

        public AutomationElement? FindChatInput(nint hwnd) => null;
        public string DumpTree(nint hwnd) => "";
        public string DescribeElement(AutomationElement? element) => "";
        public string ReadAccountQuotaSurfaceText(nint hwnd) => hwnd == handle ? quotaText : "";
    }

    private sealed class SimulationSender : IResumeSender
    {
        public bool VerifyTarget(TargetWindow target) => true;
        public bool TrySend(TargetWindow target, string text, bool dryRun, bool sendEnter) => true;
    }
}

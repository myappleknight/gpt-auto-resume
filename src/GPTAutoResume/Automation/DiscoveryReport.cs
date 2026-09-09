namespace GPTAutoResume.Automation;

public sealed record DiscoveryReport(
    bool WindowFound,
    string ProcessName,
    int? ProcessId,
    nint? WindowHandle,
    string WindowTitle,
    bool EditableControlFound,
    string EditableControlSummary,
    string MessageTextAccessStatus,
    string TreeDumpPath);

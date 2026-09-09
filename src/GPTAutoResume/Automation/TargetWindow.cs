namespace GPTAutoResume.Automation;

public sealed record TargetWindow(int ProcessId, nint Handle, string Title, string ProcessName);

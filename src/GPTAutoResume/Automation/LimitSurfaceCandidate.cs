namespace GPTAutoResume.Automation;

public enum LimitSurfaceKind
{
    Unknown,
    Banner,
    Alert,
    Status,
    ConversationBody,
    Composer,
    Sidebar,
    Offscreen,
    UnrelatedProcess
}

public sealed record LimitSurfaceCandidate(
    string Text,
    LimitSurfaceKind Kind,
    bool HasWarningRole,
    bool HasWarningIcon,
    int Confidence,
    string ControlType,
    string AutomationId,
    string ClassName,
    int Depth,
    bool IsOffscreen,
    string Bounds,
    string ParentControlType,
    string GrandparentControlType,
    string SiblingSummary)
{
    public bool IsEligibleForAutomation =>
        !string.IsNullOrWhiteSpace(Text)
        && !IsOffscreen
        && Kind is LimitSurfaceKind.Banner or LimitSurfaceKind.Alert or LimitSurfaceKind.Status
        && Confidence >= 60;
}

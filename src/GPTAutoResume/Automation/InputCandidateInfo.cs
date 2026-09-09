namespace GPTAutoResume.Automation;

public sealed record InputCandidateInfo(
    string ControlType,
    string ClassName,
    string AutomationId,
    string Name,
    bool IsEnabled,
    bool IsKeyboardFocusable,
    bool HasValuePattern,
    bool HasTextPattern,
    bool IsInsideTargetWindow,
    int DepthFromRoot);

public sealed record InputCandidateScore(int Confidence, bool IsSafe, string Summary);

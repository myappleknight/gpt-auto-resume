namespace GPTAutoResume.Automation;

public static class InputCandidateScorer
{
    private static readonly string[] EditorClassSignals = ["ProseMirror", "editor", "text"];
    private static readonly string[] NameSignals = ["message", "prompt", "chat", "ask", "send", "input", "compose", "輸入", "訊息", "メッセージ"];

    public static InputCandidateScore Score(InputCandidateInfo candidate)
    {
        var confidence = 0;
        var reasons = new List<string>();

        Add(candidate.IsInsideTargetWindow, 20, "inside target window");
        Add(candidate.IsEnabled, 18, "enabled");
        Add(candidate.IsKeyboardFocusable, 18, "focusable");

        if (candidate.ControlType.EndsWith(".Edit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.ControlType, "Edit", StringComparison.OrdinalIgnoreCase))
        {
            Add(true, 18, "edit control");
        }
        else if (candidate.ControlType.EndsWith(".Document", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.ControlType, "Document", StringComparison.OrdinalIgnoreCase))
        {
            Add(true, 12, "document control");
        }

        Add(candidate.HasValuePattern, 14, "value pattern");
        Add(candidate.HasTextPattern, 10, "text pattern");
        Add(EditorClassSignals.Any(signal => candidate.ClassName.Contains(signal, StringComparison.OrdinalIgnoreCase)), 10, "editor class");
        Add(NameSignals.Any(signal => candidate.Name.Contains(signal, StringComparison.OrdinalIgnoreCase)
            || candidate.AutomationId.Contains(signal, StringComparison.OrdinalIgnoreCase)), 10, "name/id signal");
        Add(candidate.DepthFromRoot is > 0 and < 16, 4, "reasonable hierarchy");

        var hasEditableType = candidate.ControlType.EndsWith(".Edit", StringComparison.OrdinalIgnoreCase)
            || candidate.ControlType.EndsWith(".Document", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.ControlType, "Edit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.ControlType, "Document", StringComparison.OrdinalIgnoreCase);
        var hasPattern = candidate.HasValuePattern || candidate.HasTextPattern;
        var hasIdentitySignal = candidate.ClassName.Contains("ProseMirror", StringComparison.OrdinalIgnoreCase)
            || NameSignals.Any(signal => candidate.Name.Contains(signal, StringComparison.OrdinalIgnoreCase)
                || candidate.AutomationId.Contains(signal, StringComparison.OrdinalIgnoreCase));
        var isSafe = confidence >= 72
            && candidate.IsInsideTargetWindow
            && candidate.IsEnabled
            && candidate.IsKeyboardFocusable
            && hasEditableType
            && hasPattern
            && hasIdentitySignal;

        return new InputCandidateScore(confidence, isSafe, string.Join(", ", reasons));

        void Add(bool condition, int points, string reason)
        {
            if (!condition)
            {
                return;
            }

            confidence += points;
            reasons.Add(reason);
        }
    }
}

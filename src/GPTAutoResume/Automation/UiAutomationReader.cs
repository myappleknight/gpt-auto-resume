using System.Text;
using System.Windows.Automation;
using Rect = System.Windows.Rect;

namespace GPTAutoResume.Automation;

public sealed class UiAutomationReader : IUiAutomationReader, IConversationIdentityProvider, IAccountQuotaReader
{
    public string ReadAccountQuotaSurfaceText(nint hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root is null)
            {
                return "";
            }

            var surfaces = new List<string>();
            foreach (var menu in EnumerateBounded(root, maxDepth: 18, maxElements: 3_000)
                .Where(element => element.Current.ControlType == ControlType.Menu && !element.Current.IsOffscreen)
                .Take(12))
            {
                var lines = EnumerateBounded(menu, maxDepth: 10, maxElements: 300)
                    .Where(element => !element.Current.IsOffscreen && element.Current.ControlType != ControlType.Edit)
                    .Select(element => element.Current.Name?.Trim() ?? "")
                    .Where(name => name.Length is > 0 and < 200)
                    .ToArray();
                if (lines.Any(IsAccountQuotaHeading))
                {
                    surfaces.Add(string.Join(Environment.NewLine, lines));
                }
            }

            // Separate desktop popups need proven ownership before they can be used.
            return surfaces.Count == 1 ? surfaces[0] : "";
        }
        catch
        {
            return "";
        }
    }

    public string ReadVisibleText(nint hwnd, out bool hasWarningRole)
    {
        hasWarningRole = false;
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root is null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            Walk(root, builder, ref hasWarningRole, 0, 10, TreeWalker.RawViewWalker);
            return builder.ToString();
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    public AutomationElement? FindChatInput(nint hwnd)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return FindChatInputForDiscovery(hwnd);
            }
            catch (ElementNotAvailableException)
            {
                Thread.Sleep(200);
            }
        }

        return null;
    }

    public AutomationElement? FindChatInputForDiscovery(nint hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root is null)
            {
                return null;
            }

            var editCandidates = FindCandidatesByControlType(root, ControlType.Edit);
            var bestEdit = editCandidates
                .Where(item => item.Score.IsSafe)
                .OrderBy(item => item.Score.Confidence)
                .LastOrDefault()
                .Element;
            if (bestEdit is not null)
            {
                return bestEdit;
            }

            var candidates = new List<(AutomationElement Element, InputCandidateScore Score)>();
            var visited = 0;
            WalkInputCandidates(root, root, 0, maxDepth: 24, maxElements: 3_000, candidates, ref visited);
            return candidates
                .Where(item => item.Score.IsSafe)
                .Where(item => !IsGenericRootWebArea(item.Element))
                .OrderBy(item => item.Score.Confidence)
                .LastOrDefault()
                .Element;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    public string DumpTree(nint hwnd)
    {
        var root = AutomationElement.FromHandle(hwnd);
        if (root is null)
        {
            return "No UI Automation root found.";
        }

        var builder = new StringBuilder();
        Dump(root, builder, 0, 14);
        return builder.ToString();
    }

    public string DescribeElement(AutomationElement? element)
    {
        try
        {
            if (element is null)
            {
                return "NOT FOUND";
            }

            var candidate = ToCandidate(null, element);
            var score = InputCandidateScorer.Score(candidate);
            return $"ControlType={element.Current.ControlType.ProgrammaticName}; Class={element.Current.ClassName}; AutomationId={element.Current.AutomationId}; Name={RedactName(element.Current.Name)}; HasValuePattern={element.TryGetCurrentPattern(ValuePattern.Pattern, out _)}; HasTextPattern={element.TryGetCurrentPattern(TextPattern.Pattern, out _)}; InputConfidence={score.Confidence}; InputSafe={score.IsSafe}; Signals={score.Summary}";
        }
        catch (ElementNotAvailableException)
        {
            return "NOT FOUND - element expired";
        }
    }

    public string GetActiveWorkDisplayName(nint hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root is null)
            {
                return "";
            }

            var rootBounds = root.Current.BoundingRectangle;
            return EnumerateBounded(root, maxDepth: 18, maxElements: 2_500)
                .Select(element => TryCreateActiveTitleCandidate(element, rootBounds))
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Bounds.Top)
                .Select(candidate => candidate.Name)
                .FirstOrDefault() ?? "";
        }
        catch (ElementNotAvailableException)
        {
            return "";
        }
    }

    public ConversationTargetIdentity? CaptureConversationIdentity(nint hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root is null)
            {
                return null;
            }

            var boundedElements = EnumerateBounded(root, maxDepth: 18, maxElements: 2_500).ToList();
            var document = boundedElements
                .Where(element => IsControlType(element, ControlType.Document))
                .FirstOrDefault();
            var selected = boundedElements
                .Where(IsSelectedItem)
                .Select(SummarizeElementIdentity)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
            var composer = FindChatInputForDiscovery(hwnd);
            var activeTitle = GetActiveWorkDisplayName(hwnd);
            var titleForIdentity = string.IsNullOrWhiteSpace(activeTitle) ? document?.Current.Name : activeTitle;
            var identity = new ConversationTargetIdentity(
                SurfaceType: document?.Current.ControlType.ProgrammaticName ?? root.Current.ControlType.ProgrammaticName,
                ConversationTitleHash: ConversationTargetIdentity.Hash(titleForIdentity),
                SelectedItemIdentity: ConversationTargetIdentity.Hash(selected),
                ContainerAutomationId: document?.Current.AutomationId ?? "",
                ContainerNameHash: ConversationTargetIdentity.Hash(document?.Current.Name),
                ComposerIdentity: ConversationTargetIdentity.Hash(SummarizeElementIdentity(composer)));

            return identity.HasUsableSignal ? identity : null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    public bool IsConversationStillActive(nint hwnd, ConversationTargetIdentity identity)
    {
        var current = CaptureConversationIdentity(hwnd);
        if (current is null || !identity.HasUsableSignal)
        {
            return false;
        }

        var matches = MatchIfPresent(identity.ConversationTitleHash, current.ConversationTitleHash)
            && (identity.NavigationRowIdentity.Length == 0
                || UiAutomationWorkNavigator.IsRowCurrent(hwnd, identity.NavigationRowIdentity, identity.ConversationTitleHash))
            && MatchIfPresent(identity.SelectedItemIdentity, current.SelectedItemIdentity)
            && MatchIfPresent(identity.ContainerAutomationId, current.ContainerAutomationId)
            && MatchIfPresent(identity.ContainerNameHash, current.ContainerNameHash)
            && MatchIfPresent(identity.ComposerIdentity, current.ComposerIdentity);
        if (!matches)
            new GPTAutoResume.Core.ProductionEventLog().Write(new GPTAutoResume.Core.ProductionTrace(DateTimeOffset.Now,
                "IDENTITY_MISMATCH", $"Title={identity.ConversationTitleHash == current.ConversationTitleHash};Selected={identity.SelectedItemIdentity == current.SelectedItemIdentity};ContainerId={identity.ContainerAutomationId == current.ContainerAutomationId};ContainerName={identity.ContainerNameHash == current.ContainerNameHash};Composer={identity.ComposerIdentity == current.ComposerIdentity}"));
        return matches;
    }

    public ConversationTargetIdentity? CaptureComposerIdentity(AutomationElement? composer)
    {
        if (composer is null)
        {
            return null;
        }

        try
        {
            var identity = new ConversationTargetIdentity(
                SurfaceType: composer.Current.ControlType.ProgrammaticName,
                ConversationTitleHash: "",
                SelectedItemIdentity: "",
                ContainerAutomationId: "",
                ContainerNameHash: "",
                ComposerIdentity: ConversationTargetIdentity.Hash(SummarizeElementIdentity(composer)));
            return identity.HasUsableSignal ? identity : null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    public bool HasSubstantiveText(nint hwnd, string windowTitle)
    {
        var text = ReadVisibleText(hwnd, out _);
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return lines.Any(line => !string.Equals(line, windowTitle, StringComparison.OrdinalIgnoreCase)
            && line.Length > 8);
    }

    private static void Walk(AutomationElement element, StringBuilder builder, ref bool hasWarningRole, int depth, int maxDepth, TreeWalker walker)
    {
        if (depth > maxDepth)
        {
            return;
        }

        var controlType = element.Current.ControlType;
        if (controlType == ControlType.Pane && depth > 0)
        {
            // Panes often repeat descendant text in Chromium apps, so keep walking but do not log pane names.
        }
        else
        {
            AppendIfUseful(builder, element.Current.Name);
        }

        if (controlType == ControlType.Text || controlType == ControlType.Document || controlType == ControlType.Edit)
        {
            AppendIfUseful(builder, TryGetValue(element));
        }

        if (element.Current.LocalizedControlType.Contains("alert", StringComparison.OrdinalIgnoreCase)
            || element.Current.Name.Contains('⚠')
            || element.Current.Name.Contains('!'))
        {
            hasWarningRole = true;
        }

        for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
        {
            Walk(child, builder, ref hasWarningRole, depth + 1, maxDepth, walker);
        }
    }

    private static bool IsAccountQuotaHeading(string value) =>
        new[] { "剩餘用量", "剩余用量", "remaining usage", "usage remaining" }
            .Any(heading => value.Equals(heading, StringComparison.OrdinalIgnoreCase)
                || value.StartsWith(heading + " ", StringComparison.OrdinalIgnoreCase));

    private static void Dump(AutomationElement element, StringBuilder builder, int depth, int maxDepth)
    {
        if (depth > maxDepth)
        {
            return;
        }

        var indent = new string(' ', depth * 2);
        builder.Append(indent)
            .Append(element.Current.ControlType.ProgrammaticName)
            .Append(" | Class=")
            .Append(element.Current.ClassName)
            .Append(" | AutomationId=")
            .Append(element.Current.AutomationId)
            .Append(" | Name=")
            .Append(RedactName(element.Current.Name))
            .AppendLine();

        var walker = TreeWalker.RawViewWalker;
        for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
        {
            Dump(child, builder, depth + 1, maxDepth);
        }
    }

    private static void WalkInputCandidates(
        AutomationElement root,
        AutomationElement element,
        int depth,
        int maxDepth,
        int maxElements,
        List<(AutomationElement Element, InputCandidateScore Score)> candidates,
        ref int visited)
    {
        if (depth > maxDepth || visited >= maxElements)
        {
            return;
        }

        visited++;
        try
        {
            var candidate = ToCandidate(root, element);
            if (candidate.ControlType.Contains("Edit", StringComparison.OrdinalIgnoreCase)
                || candidate.HasValuePattern
                || candidate.HasTextPattern
                || candidate.ClassName.Contains("ProseMirror", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add((element, InputCandidateScorer.Score(candidate)));
            }

            var walker = TreeWalker.RawViewWalker;
            for (var child = walker.GetFirstChild(element); child is not null && visited < maxElements; child = walker.GetNextSibling(child))
            {
                WalkInputCandidates(root, child, depth + 1, maxDepth, maxElements, candidates, ref visited);
            }
        }
        catch (ElementNotAvailableException)
        {
            // Discovery is best-effort and must never block core monitoring.
        }
    }

    private static IReadOnlyList<(AutomationElement Element, InputCandidateScore Score)> FindCandidatesByControlType(
        AutomationElement root,
        ControlType controlType)
    {
        try
        {
            return root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, controlType))
                .Cast<AutomationElement>()
                .Select(element => (Element: element, Score: InputCandidateScorer.Score(ToCandidate(root, element))))
                .ToList();
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }
    }

    private static IEnumerable<AutomationElement> EnumerateBounded(AutomationElement root, int maxDepth, int maxElements)
    {
        var results = new List<AutomationElement>();
        var visited = 0;
        WalkElements(root, 0, maxDepth, maxElements, results, ref visited);
        return results;
    }

    private static void WalkElements(
        AutomationElement element,
        int depth,
        int maxDepth,
        int maxElements,
        List<AutomationElement> output,
        ref int visited)
    {
        if (depth > maxDepth || visited >= maxElements)
        {
            return;
        }

        visited++;
        output.Add(element);
        try
        {
            var walker = TreeWalker.RawViewWalker;
            for (var child = walker.GetFirstChild(element); child is not null && visited < maxElements; child = walker.GetNextSibling(child))
            {
                WalkElements(child, depth + 1, maxDepth, maxElements, output, ref visited);
            }
        }
        catch (ElementNotAvailableException)
        {
            // Bounded discovery is best-effort.
        }
    }

    private static bool IsControlType(AutomationElement element, ControlType controlType)
    {
        try
        {
            return element.Current.ControlType == controlType;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool IsGenericRootWebArea(AutomationElement element)
    {
        try
        {
            return string.Equals(element.Current.AutomationId, "RootWebArea", StringComparison.OrdinalIgnoreCase)
                && string.Equals(element.Current.Name, "ChatGPT", StringComparison.OrdinalIgnoreCase);
        }
        catch (ElementNotAvailableException)
        {
            return true;
        }
    }

    private static ActiveTitleCandidate? TryCreateActiveTitleCandidate(AutomationElement element, Rect rootBounds)
    {
        try
        {
            var name = element.Current.Name?.Trim() ?? "";
            if (!IsLikelyActiveWorkTitle(name))
            {
                return null;
            }

            var bounds = element.Current.BoundingRectangle;
            if (bounds.IsEmpty || rootBounds.IsEmpty)
            {
                return null;
            }

            var relativeLeft = bounds.Left - rootBounds.Left;
            var relativeTop = bounds.Top - rootBounds.Top;
            var inActiveHeader = relativeLeft >= 250
                && relativeLeft <= rootBounds.Width - 160
                && relativeTop >= 24
                && relativeTop <= 130
                && bounds.Height is >= 14 and <= 42;
            if (!inActiveHeader)
            {
                return null;
            }

            var score = 0;
            if (element.Current.ControlType == ControlType.Button)
            {
                score += 20;
            }

            if (element.Current.ControlType == ControlType.Text)
            {
                score += 12;
            }

            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out _))
            {
                score += 10;
            }

            score += Math.Max(0, 20 - (int)Math.Abs(relativeTop - 48));
            return new ActiveTitleCandidate(TrimDisplayName(name), bounds, score);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static bool IsLikelyActiveWorkTitle(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 4)
        {
            return false;
        }

        var excluded = new[]
        {
            "ChatGPT",
            "Codex",
            "Search",
            "搜尋",
            "New chat",
            "新對話",
            "設定",
            "Settings",
            "更多",
            "More",
            "查看活動",
            "對話動作",
            "開啟個人檔案選單",
            "開啟說明選單"
        };

        if (excluded.Any(value => string.Equals(name, value, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var excludedPrefixes = new[]
        {
            "專案：",
            "Project:",
            "切換模式",
            "Open profile"
        };

        return !excludedPrefixes.Any(value => name.StartsWith(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string TrimDisplayName(string value) =>
        value.Length <= 60 ? value : value[..57] + "...";

    private static InputCandidateInfo ToCandidate(AutomationElement? root, AutomationElement element)
    {
        var hasValuePattern = element.TryGetCurrentPattern(ValuePattern.Pattern, out _);
        var hasTextPattern = element.TryGetCurrentPattern(TextPattern.Pattern, out _);
        return new InputCandidateInfo(
            ControlType: element.Current.ControlType.ProgrammaticName,
            ClassName: element.Current.ClassName ?? "",
            AutomationId: element.Current.AutomationId ?? "",
            Name: element.Current.Name ?? "",
            IsEnabled: element.Current.IsEnabled,
            IsKeyboardFocusable: element.Current.IsKeyboardFocusable,
            HasValuePattern: hasValuePattern,
            HasTextPattern: hasTextPattern,
            IsInsideTargetWindow: root is null || IsDescendantOf(element, root),
            DepthFromRoot: root is null ? 1 : DepthFromRoot(element, root));
    }

    private static bool IsDescendantOf(AutomationElement element, AutomationElement root)
    {
        for (var current = element; current is not null; current = TreeWalker.RawViewWalker.GetParent(current))
        {
            if (Equals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    private static int DepthFromRoot(AutomationElement element, AutomationElement root)
    {
        var depth = 0;
        for (var current = element; current is not null && depth < 64; current = TreeWalker.RawViewWalker.GetParent(current))
        {
            if (Equals(current, root))
            {
                return depth;
            }

            depth++;
        }

        return 64;
    }

    private static string TryGetValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var raw)
                && raw is ValuePattern valuePattern)
            {
                return valuePattern.Current.Value ?? string.Empty;
            }
        }
        catch
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static bool IsSelectedItem(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var raw)
                && raw is SelectionItemPattern pattern
                && pattern.Current.IsSelected;
        }
        catch
        {
            return false;
        }
    }

    private static string SummarizeElementIdentity(AutomationElement? element)
    {
        if (element is null)
        {
            return "";
        }

        try
        {
            return string.Join("|",
                element.Current.ControlType.ProgrammaticName,
                ComposerText.StableClassName(element.Current.ClassName),
                element.Current.AutomationId,
                element.Current.Name);
        }
        catch (ElementNotAvailableException)
        {
            return "";
        }
    }

    private static bool MatchIfPresent(string expected, string current) =>
        string.IsNullOrWhiteSpace(expected) || string.Equals(expected, current, StringComparison.Ordinal);

    private static void AppendIfUseful(StringBuilder builder, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Length is > 1 and < 2_000)
        {
            builder.AppendLine(trimmed);
        }
    }

    private static string RedactName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= 32 && !trimmed.Contains('\n') && !trimmed.Contains('\r'))
        {
            return trimmed;
        }

        return $"<redacted length={trimmed.Length}>";
    }

    private sealed record ActiveTitleCandidate(string Name, Rect Bounds, int Score);
}

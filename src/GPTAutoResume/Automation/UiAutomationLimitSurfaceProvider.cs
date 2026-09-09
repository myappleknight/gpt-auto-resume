using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Rect = System.Windows.Rect;

namespace GPTAutoResume.Automation;

public sealed partial class UiAutomationLimitSurfaceProvider : ILimitSurfaceProvider
{
    public IReadOnlyList<LimitSurfaceCandidate> FindLimitSurfaces(TargetWindow target)
    {
        try
        {
            var root = AutomationElement.FromHandle(target.Handle);
            if (root is null)
            {
                return [];
            }

            var rootBounds = root.Current.BoundingRectangle;
            var candidates = new List<LimitSurfaceCandidate>();
            var queue = new Queue<(AutomationElement Element, int Depth)>();
            queue.Enqueue((root, 0));
            var visited = 0;
            while (queue.Count > 0 && visited < 5_000)
            {
                var (element, depth) = queue.Dequeue();
                visited++;
                try
                {
                    var current = element.Current;
                    var text = JoinDistinct([ReadNodeText(element), ReadCompactSubtreeText(element, maxDepth: 2, maxElements: 24)]);
                    if (!string.IsNullOrWhiteSpace(text) && PossibleLimitRegex().IsMatch(text))
                    {
                        var candidate = CreateCandidate(element, text, depth, rootBounds, target.ProcessId);
                        if (candidate.Confidence >= 35)
                        {
                            candidates.Add(candidate);
                        }
                    }

                    if (depth >= 40 || current.ControlType == ControlType.Edit)
                    {
                        continue;
                    }

                    for (var child = TreeWalker.RawViewWalker.GetFirstChild(element);
                         child is not null && queue.Count < 5_000;
                         child = TreeWalker.RawViewWalker.GetNextSibling(child))
                    {
                        queue.Enqueue((child, depth + 1));
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // ChatGPT's UIA tree mutates while rendering; expired nodes are ignored.
                }
            }

            return candidates
                .OrderByDescending(candidate => candidate.IsEligibleForAutomation)
                .ThenByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => candidate.Depth)
                .Take(12)
                .ToArray();
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }
    }

    private static LimitSurfaceCandidate CreateCandidate(
        AutomationElement element,
        string text,
        int depth,
        Rect rootBounds,
        int targetProcessId)
    {
        var current = element.Current;
        var bounds = current.BoundingRectangle;
        var parent = SafeParent(element);
        var grandparent = parent is null ? null : SafeParent(parent);
        var kind = Classify(element, text, rootBounds, targetProcessId, parent, grandparent);
        var hasWarningRole = LooksLikeWarning(current);
        var hasWarningIcon = text.Contains('⚠') || text.Contains('!');
        var confidence = Score(text, current, bounds, rootBounds, kind, hasWarningRole, hasWarningIcon);
        return new LimitSurfaceCandidate(
            TrimCandidateText(text),
            kind,
            hasWarningRole,
            hasWarningIcon,
            confidence,
            current.ControlType.ProgrammaticName,
            current.AutomationId ?? "",
            current.ClassName ?? "",
            depth,
            current.IsOffscreen,
            bounds.ToString(CultureInfo.InvariantCulture),
            parent?.Current.ControlType.ProgrammaticName ?? "",
            grandparent?.Current.ControlType.ProgrammaticName ?? "",
            SummarizeSiblings(element));
    }

    private static LimitSurfaceKind Classify(
        AutomationElement element,
        string text,
        Rect rootBounds,
        int targetProcessId,
        AutomationElement? parent,
        AutomationElement? grandparent)
    {
        var current = element.Current;
        if (current.ProcessId != targetProcessId)
        {
            return LimitSurfaceKind.UnrelatedProcess;
        }

        if (current.IsOffscreen)
        {
            return LimitSurfaceKind.Offscreen;
        }

        if (current.ControlType == ControlType.Edit || AncestorHasControlType(parent, ControlType.Edit))
        {
            return LimitSurfaceKind.Composer;
        }

        if (IsSidebarRegion(current.BoundingRectangle, rootBounds))
        {
            return LimitSurfaceKind.Sidebar;
        }

        if (LooksLikeWarning(current) || LooksLikeNotice(parent) || LooksLikeNotice(grandparent))
        {
            return LimitSurfaceKind.Alert;
        }

        if (LooksLikeCompactNotice(text, current.BoundingRectangle, rootBounds)
            && (ResetOrActionRegex().IsMatch(text) || SiblingHasActionOrResetContext(element)))
        {
            return LimitSurfaceKind.Banner;
        }

        return LimitSurfaceKind.ConversationBody;
    }

    private static int Score(
        string text,
        AutomationElement.AutomationElementInformation current,
        Rect bounds,
        Rect rootBounds,
        LimitSurfaceKind kind,
        bool hasWarningRole,
        bool hasWarningIcon)
    {
        var score = 0;
        if (StrongLimitRegex().IsMatch(text))
        {
            score += 35;
        }

        if (ResetOrActionRegex().IsMatch(text))
        {
            score += 25;
        }

        if (hasWarningRole)
        {
            score += 15;
        }

        if (hasWarningIcon)
        {
            score += 5;
        }

        if (!bounds.IsEmpty && !rootBounds.IsEmpty && !IsSidebarRegion(bounds, rootBounds))
        {
            score += 10;
        }

        if (LooksLikeCompactNotice(text, bounds, rootBounds))
        {
            score += 15;
        }

        if (current.ControlType == ControlType.Text || current.ControlType == ControlType.Group || current.ControlType == ControlType.Pane)
        {
            score += 5;
        }

        score += kind switch
        {
            LimitSurfaceKind.Banner => 25,
            LimitSurfaceKind.Alert => 25,
            LimitSurfaceKind.Status => 15,
            LimitSurfaceKind.ConversationBody => -40,
            LimitSurfaceKind.Composer => -80,
            LimitSurfaceKind.Sidebar => -80,
            LimitSurfaceKind.Offscreen => -80,
            LimitSurfaceKind.UnrelatedProcess => -100,
            _ => 0
        };

        return Math.Max(0, score);
    }

    private static bool LooksLikeCompactNotice(string text, Rect bounds, Rect rootBounds)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Take(16)
            .ToArray();
        if (lines.Length > 10 || text.Length > 700)
        {
            return false;
        }

        if (bounds.IsEmpty || rootBounds.IsEmpty)
        {
            return true;
        }

        var relativeLeft = bounds.Left - rootBounds.Left;
        var relativeTop = bounds.Top - rootBounds.Top;
        return relativeLeft >= 220
            && relativeTop >= 80
            && bounds.Width is >= 120 and <= 1_000
            && bounds.Height <= 260;
    }

    private static bool IsSidebarRegion(Rect bounds, Rect rootBounds)
    {
        if (bounds.IsEmpty || rootBounds.IsEmpty)
        {
            return false;
        }

        var relativeLeft = bounds.Left - rootBounds.Left;
        var relativeRight = bounds.Right - rootBounds.Left;
        return relativeRight <= 260 || relativeLeft < 225 && bounds.Width <= 240;
    }

    private static bool LooksLikeWarning(AutomationElement.AutomationElementInformation current) =>
        current.LocalizedControlType.Contains("alert", StringComparison.OrdinalIgnoreCase)
        || current.Name.Contains('⚠')
        || current.Name.Contains('!');

    private static bool LooksLikeNotice(AutomationElement? element)
    {
        if (element is null)
        {
            return false;
        }

        try
        {
            var current = element.Current;
            var name = current.Name ?? "";
            return LooksLikeWarning(current)
                || current.LocalizedControlType.Contains("alert", StringComparison.OrdinalIgnoreCase)
                || name.Contains("alert", StringComparison.OrdinalIgnoreCase)
                || name.Contains("notice", StringComparison.OrdinalIgnoreCase)
                || name.Contains("警告", StringComparison.OrdinalIgnoreCase);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool AncestorHasControlType(AutomationElement? element, ControlType controlType)
    {
        for (var current = element; current is not null; current = SafeParent(current))
        {
            try
            {
                if (current.Current.ControlType == controlType)
                {
                    return true;
                }
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool SiblingHasActionOrResetContext(AutomationElement element)
    {
        var parent = SafeParent(element);
        return parent is not null && EnumerateSiblingNames(parent, 16).Any(value => ResetOrActionRegex().IsMatch(value));
    }

    private static string SummarizeSiblings(AutomationElement element)
    {
        var parent = SafeParent(element);
        if (parent is null)
        {
            return "";
        }

        return string.Join(" | ", EnumerateSiblingNames(parent, 8)
            .Select(value => value.Length <= 32 ? value : value[..29] + "..."));
    }

    private static IReadOnlyList<string> EnumerateSiblingNames(AutomationElement parent, int maxSiblings)
    {
        var names = new List<string>();
        try
        {
            for (var child = TreeWalker.RawViewWalker.GetFirstChild(parent);
                 child is not null && names.Count < maxSiblings;
                 child = TreeWalker.RawViewWalker.GetNextSibling(child))
            {
                var current = child.Current;
                var name = current.Name?.Trim() ?? "";
                if (name.Length is > 0 and < 300 && current.ControlType != ControlType.Edit)
                {
                    names.Add(name);
                }
            }
        }
        catch (ElementNotAvailableException)
        {
        }

        return names;
    }

    private static string ReadNodeText(AutomationElement element)
    {
        var builder = new StringBuilder();
        AppendIfUseful(builder, element.Current.Name);
        if (element.Current.ControlType == ControlType.Text || element.Current.ControlType == ControlType.Document)
        {
            AppendIfUseful(builder, TryGetValue(element));
        }

        return builder.ToString();
    }

    private static string ReadCompactSubtreeText(AutomationElement root, int maxDepth, int maxElements)
    {
        var builder = new StringBuilder();
        var visited = 0;
        ReadCompactSubtreeText(root, builder, 0, maxDepth, maxElements, ref visited);
        return builder.ToString();
    }

    private static void ReadCompactSubtreeText(
        AutomationElement element,
        StringBuilder builder,
        int depth,
        int maxDepth,
        int maxElements,
        ref int visited)
    {
        if (depth > maxDepth || visited >= maxElements)
        {
            return;
        }

        visited++;
        try
        {
            if (element.Current.ControlType != ControlType.Edit)
            {
                AppendIfUseful(builder, element.Current.Name);
                if (element.Current.ControlType == ControlType.Text || element.Current.ControlType == ControlType.Document)
                {
                    AppendIfUseful(builder, TryGetValue(element));
                }
            }

            for (var child = TreeWalker.RawViewWalker.GetFirstChild(element);
                 child is not null && visited < maxElements;
                 child = TreeWalker.RawViewWalker.GetNextSibling(child))
            {
                ReadCompactSubtreeText(child, builder, depth + 1, maxDepth, maxElements, ref visited);
            }
        }
        catch (ElementNotAvailableException)
        {
        }
    }

    private static AutomationElement? SafeParent(AutomationElement element)
    {
        try
        {
            return TreeWalker.RawViewWalker.GetParent(element);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static string JoinDistinct(IEnumerable<string> values)
    {
        var lines = values
            .SelectMany(value => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return string.Join(Environment.NewLine, lines);
    }

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

    private static string TryGetValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var raw) && raw is ValuePattern valuePattern)
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

    private static string TrimCandidateText(string text)
    {
        var trimmed = string.Join(Environment.NewLine, text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Take(12));
        return trimmed.Length <= 1_000 ? trimmed : trimmed[..1_000];
    }

    [GeneratedRegex(@"usage\s+limit|out\s+of.{0,80}usage|使用上限|使用量已用完|用量已用完|codex\s*和工作使用量已用完", RegexOptions.IgnoreCase)]
    private static partial Regex PossibleLimitRegex();

    [GeneratedRegex(@"try\s+again|reset|add\s+credits|upgrade|再試一次|重置|用量重置|新增點數|加值點數|升級方案|清晨|凌晨|上午|下午|晚上", RegexOptions.IgnoreCase)]
    private static partial Regex ResetOrActionRegex();

    [GeneratedRegex(@"out\s+of.{0,80}usage|使用量已用完|用量已用完|codex\s*和工作使用量已用完|使用上限", RegexOptions.IgnoreCase)]
    private static partial Regex StrongLimitRegex();
}

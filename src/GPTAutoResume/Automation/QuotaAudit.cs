using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using GPTAutoResume.Core;

namespace GPTAutoResume.Automation;

public static class QuotaAudit
{
    public static string Run(string root)
    {
        var diagnosticsDir = Path.Combine(root, "diagnostics");
        Directory.CreateDirectory(diagnosticsDir);
        var path = Path.Combine(diagnosticsDir, "quota-audit.txt");
        var scanner = new WindowScanner();
        var reader = new UiAutomationReader();
        var targets = scanner.FindTargets();
        if (targets.Count == 0)
        {
            File.WriteAllText(path, "Target: NOT FOUND");
            return path;
        }

        var now = DateTimeOffset.Now;
        var reports = targets
            .Select((target, index) => CaptureTargetReport(reader, target, now, index + 1, targets.Count))
            .ToArray();
        File.WriteAllText(path, string.Join(Environment.NewLine + Environment.NewLine + "---" + Environment.NewLine + Environment.NewLine, reports));
        return path;
    }

    private static string CaptureTargetReport(UiAutomationReader reader, TargetWindow target, DateTimeOffset now, int index, int total)
    {
        var text = reader.ReadAccountQuotaSurfaceText(target.Handle);
        var snapshot = QuotaSnapshotParser.Parse(text, now, new RetryTimeParser());
        var rootElement = AutomationElement.FromHandle(target.Handle);
        var profileElements = rootElement is null ? [] : rootElement.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
            .Cast<AutomationElement>()
            .Where(IsProfileOrMenuCandidate)
            .Take(12)
            .ToArray();
        var profileCandidates = profileElements
            .Select(DescribeMenuCandidate)
            .ToArray();
        var profile = profileElements.Length == 1 ? profileElements[0] : null;
        var menuState = profile is not null
            && profile.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var menuRaw)
            && menuRaw is ExpandCollapsePattern menuPattern
                ? SafeExpandCollapseState(menuPattern).ToString() : "UNKNOWN";
        var expandReport = TryExpandReadCollapse(profile, reader, target, now);

        return $"""
               Target: FOUND ({index}/{total})
               Process: {target.ProcessName}
               ProcessId: {target.ProcessId}
               HWND: {target.Handle}
               TitlePresent: {!string.IsNullOrWhiteSpace(target.Title)}
               ReadScope: verified quota menu descendants under this target HWND only
               MenuStateBeforeProbe: {menuState}
               QuotaReadableBeforeProbe: {HasCompleteQuota(snapshot)}
               ClosedMenuQuotaReadable: {(menuState == "Collapsed" ? HasCompleteQuota(snapshot).ToString() : "NOT TESTED")}
               ShortWindowRemainingPercent: {snapshot.ShortWindowRemainingPercent?.ToString() ?? "NOT READ"}
               ShortWindowResetAt: {snapshot.ShortWindowResetAt?.ToString("yyyy/MM/dd HH:mm") ?? "NOT READ"}
               WeeklyRemainingPercent: {snapshot.WeeklyRemainingPercent?.ToString() ?? "NOT READ"}
               WeeklyResetAt: {snapshot.WeeklyResetAt?.ToString("yyyy/MM/dd HH:mm") ?? "NOT READ"}
               SeparateDesktopPopupOwnership: NOT VERIFIED; excluded
               ProfileMenuCandidates:
               {string.Join(Environment.NewLine, profileCandidates)}

               UIA Expand Probe:
               {expandReport}
               """;
    }

    private static string TryExpandReadCollapse(AutomationElement? profileMenu, UiAutomationReader reader, TargetWindow target, DateTimeOffset now)
    {
        if (profileMenu is null)
        {
            return "Profile menu: NOT FOUND";
        }

        if (!profileMenu.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var raw)
            || raw is not ExpandCollapsePattern pattern)
        {
            return "UIA Expand: NO";
        }

        var foregroundBefore = GetForegroundWindow();
        _ = GetCursorPos(out var cursorBefore);
        var activeWorkBefore = reader.GetActiveWorkDisplayName(target.Handle);
        var stateBefore = SafeExpandCollapseState(pattern);
        var openedByProbe = false;
        try
        {
            if (stateBefore == ExpandCollapseState.Collapsed)
            {
                openedByProbe = true;
                pattern.Expand();
                Thread.Sleep(900);
            }
            else if (stateBefore != ExpandCollapseState.Expanded)
            {
                return "UIA Expand: NOT SAFE; initial state unknown";
            }

            var expandedRoot = AutomationElement.FromHandle(target.Handle);
            var quotaSection = expandedRoot is null
                ? new AccountQuotaExpansionScope(false, false, false, "Quota section expander: TARGET EXPIRED", null)
                : AccountQuotaUiaExpander.TryExpandQuotaSection(expandedRoot, CancellationToken.None);
            var expandedText = reader.ReadAccountQuotaSurfaceText(target.Handle);
            var snapshot = QuotaSnapshotParser.Parse(expandedText, now, new RetryTimeParser());
            var relatedPopupReport = CaptureRelatedPopupReport(target, now);
            quotaSection.Dispose();

            var collapsed = false;
            try
            {
                if (stateBefore != ExpandCollapseState.Expanded)
                {
                    pattern.Collapse();
                    Thread.Sleep(300);
                }

                collapsed = SafeExpandCollapseState(pattern) == stateBefore;
            }
            catch
            {
                collapsed = false;
            }

            var foregroundAfterProbe = GetForegroundWindow();
            _ = GetCursorPos(out var cursorAfterProbe);
            var activeWorkAfter = reader.GetActiveWorkDisplayName(target.Handle);
            var quotaReadable = HasCompleteQuota(snapshot);
            return $"""
                   UIA Expand: {(openedByProbe ? "EXECUTED" : "NOT EXECUTED; already open")}
                   ExpandCollapseState before: {stateBefore}
                   Quota section found: {(quotaSection.Found ? "YES" : "NO")}
                   Quota section expanded: {(quotaSection.Expanded ? "YES" : "NO")}
                   Quota section opened by probe: {(quotaSection.OpenedByProbe ? "YES" : "NO")}
                   Quota section detail: {quotaSection.Detail}
                   Quota section restored: {(quotaSection.RestoreAttempted ? (quotaSection.Restored ? "PASS" : "FAIL") : "NOT NEEDED")}
                   Quota after UIA Expand: {(quotaReadable ? "PASS" : "FAIL")}
                   5h Remaining: {snapshot.ShortWindowRemainingPercent?.ToString() ?? "NOT READ"}
                   5h Reset: {snapshot.ShortWindowResetAt?.ToString("yyyy/MM/dd HH:mm") ?? "NOT READ"}
                   Weekly Remaining: {snapshot.WeeklyRemainingPercent?.ToString() ?? "NOT READ"}
                   Weekly Reset: {snapshot.WeeklyResetAt?.ToString("yyyy/MM/dd HH:mm") ?? "NOT READ"}
                   Related popup/root probe:
                   {relatedPopupReport}
                   Foreground unchanged: {(foregroundBefore == foregroundAfterProbe ? "PASS" : "FAIL")}
                   Mouse unchanged: {(cursorBefore.X == cursorAfterProbe.X && cursorBefore.Y == cursorAfterProbe.Y ? "PASS" : "FAIL")}
                   Active title unchanged: {(string.Equals(activeWorkBefore, activeWorkAfter, StringComparison.Ordinal) ? "PASS" : "FAIL")}
                   Menu state restored: {(collapsed ? "PASS" : "FAIL")}
                   Focus/composer unchanged: NOT TESTED
                   Background quota monitoring feasible: {(quotaReadable ? "PARTIAL; focus/composer verification pending" : "NO; complete quota not read")}
                   """;
        }
        catch (Exception ex)
        {
            return $"""
                   UIA Expand: FAIL
                   ExpandCollapseState before: {stateBefore}
                   ErrorType: {ex.GetType().FullName}
                   Background quota monitoring feasible: NO
                   """;
        }
        finally
        {
            if (openedByProbe)
            {
                try { pattern.Collapse(); }
                catch (Exception) { /* Provider may have disappeared; no input fallback. */ }
            }
        }
    }

    private static ExpandCollapseState SafeExpandCollapseState(ExpandCollapsePattern pattern)
    {
        try
        {
            return pattern.Current.ExpandCollapseState;
        }
        catch
        {
            return ExpandCollapseState.LeafNode;
        }
    }

    private static bool HasCompleteQuota(QuotaSnapshot snapshot) =>
        snapshot.ShortWindowRemainingPercent is >= 0 and <= 100
        && snapshot.ShortWindowResetAt is not null
        && snapshot.WeeklyRemainingPercent is >= 0 and <= 100
        && snapshot.WeeklyResetAt is not null;

    private static string CaptureRelatedPopupReport(TargetWindow target, DateTimeOffset now)
    {
        try
        {
            var root = AutomationElement.RootElement;
            var targetElement = AutomationElement.FromHandle(target.Handle);
            if (targetElement is null)
            {
                return "Target root: NOT FOUND";
            }

            var targetBounds = targetElement.Current.BoundingRectangle;
            var candidates = root.FindAll(TreeScope.Children, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Where(element => IsRelatedPopupCandidate(element, target, targetBounds))
                .Take(12)
                .Select(element => DescribePopupQuotaCandidate(element, now))
                .Where(report => !string.IsNullOrWhiteSpace(report))
                .ToArray();

            return candidates.Length == 0
                ? "Related quota popup: NOT FOUND"
                : string.Join(Environment.NewLine, candidates);
        }
        catch (Exception ex)
        {
            return $"Related popup probe failed: {ex.GetType().FullName}";
        }
    }

    private static bool IsRelatedPopupCandidate(AutomationElement element, TargetWindow target, System.Windows.Rect targetBounds)
    {
        try
        {
            if (Equals(element, AutomationElement.FromHandle(target.Handle)))
            {
                return false;
            }

            var processMatches = element.Current.ProcessId == target.ProcessId;
            var bounds = element.Current.BoundingRectangle;
            var boundsRelated = !bounds.IsEmpty
                && !targetBounds.IsEmpty
                && bounds.Right >= targetBounds.Left - 40
                && bounds.Left <= targetBounds.Right + 40
                && bounds.Bottom >= targetBounds.Top - 40
                && bounds.Top <= targetBounds.Bottom + 40;
            var type = element.Current.ControlType;
            var popupType = type == ControlType.Window
                || type == ControlType.Menu
                || type == ControlType.Pane
                || type == ControlType.Group;

            return popupType && processMatches && boundsRelated;
        }
        catch
        {
            return false;
        }
    }

    private static string DescribePopupQuotaCandidate(AutomationElement element, DateTimeOffset now)
    {
        try
        {
            var lines = ReadCandidateLines(element, maxDepth: 8, maxElements: 500);
            if (!lines.Any(IsAccountQuotaHeading))
            {
                return "";
            }

            var text = string.Join(Environment.NewLine, lines);
            var snapshot = QuotaSnapshotParser.Parse(text, now, new RetryTimeParser());
            return $"""
                   Related quota popup: FOUND
                   ControlType: {element.Current.ControlType.ProgrammaticName}
                   ProcessIdMatches: YES
                   NativeWindowHandlePresent: {element.Current.NativeWindowHandle != 0}
                   CompleteRows: {HasCompleteQuota(snapshot)}
                   5h Remaining: {snapshot.ShortWindowRemainingPercent?.ToString() ?? "NOT READ"}
                   5h Reset: {snapshot.ShortWindowResetAt?.ToString("yyyy/MM/dd HH:mm") ?? "NOT READ"}
                   Weekly Remaining: {snapshot.WeeklyRemainingPercent?.ToString() ?? "NOT READ"}
                   Weekly Reset: {snapshot.WeeklyResetAt?.ToString("yyyy/MM/dd HH:mm") ?? "NOT READ"}
                   """;
        }
        catch
        {
            return "";
        }
    }

    private static IReadOnlyList<string> ReadCandidateLines(AutomationElement root, int maxDepth, int maxElements)
    {
        var lines = new List<string>();
        var visited = 0;
        WalkCandidateLines(root, 0, maxDepth, maxElements, lines, ref visited);
        return lines;
    }

    private static void WalkCandidateLines(
        AutomationElement element,
        int depth,
        int maxDepth,
        int maxElements,
        List<string> lines,
        ref int visited)
    {
        if (depth > maxDepth || visited >= maxElements)
        {
            return;
        }

        visited++;
        try
        {
            var name = element.Current.Name?.Trim() ?? "";
            if (name.Length is > 0 and < 200 && element.Current.ControlType != ControlType.Edit)
            {
                lines.Add(name);
            }

            var walker = TreeWalker.RawViewWalker;
            for (var child = walker.GetFirstChild(element); child is not null && visited < maxElements; child = walker.GetNextSibling(child))
            {
                WalkCandidateLines(child, depth + 1, maxDepth, maxElements, lines, ref visited);
            }
        }
        catch (ElementNotAvailableException)
        {
            // Popup content can disappear during audit.
        }
    }

    private static bool IsAccountQuotaHeading(string value) =>
        new[] { "剩餘用量", "剩余用量", "remaining usage", "usage remaining" }
            .Any(heading => value.Equals(heading, StringComparison.OrdinalIgnoreCase)
                || value.StartsWith(heading + " ", StringComparison.OrdinalIgnoreCase));

    private static bool IsProfileOrMenuCandidate(AutomationElement button)
    {
        var name = button.Current.Name ?? "";
        var id = button.Current.AutomationId ?? "";
        return name.Contains("profile", StringComparison.OrdinalIgnoreCase)
            || name.Contains("account", StringComparison.OrdinalIgnoreCase)
            || name.Contains("帳號", StringComparison.OrdinalIgnoreCase)
            || name.Contains("個人檔案", StringComparison.OrdinalIgnoreCase)
            || name.Contains("プロフィール", StringComparison.OrdinalIgnoreCase)
            || id.Contains("profile", StringComparison.OrdinalIgnoreCase)
            || id.Contains("account", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeMenuCandidate(AutomationElement element)
    {
        try
        {
            return $"ControlType={element.Current.ControlType.ProgrammaticName}; NamePresent={!string.IsNullOrEmpty(element.Current.Name)}; AutomationIdPresent={!string.IsNullOrEmpty(element.Current.AutomationId)}; ExpandCollapsePattern={element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _)}; InvokePattern={element.TryGetCurrentPattern(InvokePattern.Pattern, out _)}";
        }
        catch (ElementNotAvailableException)
        {
            return "expired";
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}

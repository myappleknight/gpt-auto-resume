using System.Runtime.InteropServices;
using System.Windows.Automation;
using GPTAutoResume.Core;

namespace GPTAutoResume.Automation;

public sealed record QuotaProbeResult(
    QuotaProbeStatus Status,
    QuotaSnapshot? Snapshot,
    bool TargetFound,
    bool ForegroundUnchanged,
    bool MouseUnchanged,
    bool UsedUnrelatedDesktopText,
    DateTimeOffset CapturedAt);

public static class QuotaProbe
{
    public static QuotaProbeResult Run(CancellationToken cancellationToken = default) =>
        Run(allowUiInteraction: true, allowControlledForeground: true, cancellationToken);

    public static QuotaProbeResult Run(bool allowUiInteraction, CancellationToken cancellationToken = default)
        => Run(allowUiInteraction, allowControlledForeground: false, cancellationToken);

    public static QuotaProbeResult Run(
        bool allowUiInteraction,
        bool allowControlledForeground,
        CancellationToken cancellationToken = default)
    {
        var foreground = GetForegroundWindow();
        var cursorRead = GetCursorPos(out var cursor);
        var snapshot = CodexAccountQuotaProvider.Shared.Read(forceRefresh: true, cancellationToken);
        var cursorAfterRead = GetCursorPos(out var cursorAfter);
        return new QuotaProbeResult(snapshot is null ? QuotaProbeStatus.NotRead : ToStatus(snapshot),
            snapshot, snapshot is not null, foreground == GetForegroundWindow(),
            cursorRead && cursorAfterRead && cursor.X == cursorAfter.X && cursor.Y == cursorAfter.Y,
            false, DateTimeOffset.Now);
    }

    // Retained for explicit UIA diagnostics only, never used by startup/manual quota queries.
    internal static QuotaProbeResult RunUiAudit(
        bool allowUiInteraction,
        bool allowControlledForeground,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        var scanner = new WindowScanner();
        var reader = new UiAutomationReader();
        var parser = new RetryTimeParser();
        var targets = scanner.FindTargets();
        if (targets.Count == 0)
        {
            return new QuotaProbeResult(QuotaProbeStatus.Unsupported, null, false, true, true, false, now);
        }

        foreach (var target in targets.Take(5))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var closedSnapshot = ParseTargetOwnedQuota(reader, parser, target, now);
            if (ToStatus(closedSnapshot) is QuotaProbeStatus.Complete or QuotaProbeStatus.Partial)
            {
                return new QuotaProbeResult(ToStatus(closedSnapshot), closedSnapshot, true, true, true, false, now);
            }

            if (!allowUiInteraction)
            {
                continue;
            }

            var expanded = TryReadExpandedProfileQuota(reader, parser, target, now, activateTarget: false, cancellationToken);
            if (expanded.Status is QuotaProbeStatus.Complete or QuotaProbeStatus.Partial)
            {
                return expanded;
            }

            if (!allowControlledForeground)
            {
                continue;
            }

            var foregroundExpanded = TryReadExpandedProfileQuota(reader, parser, target, now, activateTarget: true, cancellationToken);
            if (foregroundExpanded.Status is QuotaProbeStatus.Complete or QuotaProbeStatus.Partial)
            {
                return foregroundExpanded;
            }
        }

        return new QuotaProbeResult(QuotaProbeStatus.Unsupported, null, true, true, true, false, now);
    }

    private static QuotaSnapshot ParseTargetOwnedQuota(UiAutomationReader reader, RetryTimeParser parser, TargetWindow target, DateTimeOffset now)
    {
        var text = reader.ReadAccountQuotaSurfaceText(target.Handle);
        return QuotaSnapshotParser.Parse(text, now, parser);
    }

    private static QuotaProbeResult TryReadExpandedProfileQuota(
        UiAutomationReader reader,
        RetryTimeParser parser,
        TargetWindow target,
        DateTimeOffset now,
        bool activateTarget,
        CancellationToken cancellationToken)
    {
        var foregroundBefore = GetForegroundWindow();
        _ = GetCursorPos(out var cursorBefore);
        var targetWasMinimized = IsIconic(target.Handle);
        var shouldRestoreForeground = activateTarget
            && foregroundBefore != nint.Zero
            && foregroundBefore != target.Handle
            && IsWindow(foregroundBefore);
        var status = QuotaProbeStatus.Unsupported;
        QuotaSnapshot? snapshot = null;
        AccountQuotaExpansionScope? quotaSection = null;
        ExpandCollapsePattern? profilePattern = null;
        var openedProfileByProbe = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (activateTarget && GetForegroundWindow() != target.Handle)
            {
                if (targetWasMinimized)
                {
                    _ = ShowWindow(target.Handle, ShowRestore);
                    AccountQuotaUiaExpander.WaitBriefly(cancellationToken, 200);
                }

                ActivateWindow(target.Handle);
                if (!WaitForForeground(target.Handle, TimeSpan.FromMilliseconds(1_500), cancellationToken))
                {
                    return new QuotaProbeResult(QuotaProbeStatus.Unsupported, null, true, false, false, false, now);
                }

                AccountQuotaUiaExpander.WaitBriefly(cancellationToken, 250);
            }

            var root = AutomationElement.FromHandle(target.Handle);
            var profile = root is null ? null : root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
                .Cast<AutomationElement>()
                .Where(IsProfileOrMenuCandidate)
                .FirstOrDefault();
            if (profile is null
                || !profile.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var raw)
                || raw is not ExpandCollapsePattern pattern)
            {
                return new QuotaProbeResult(QuotaProbeStatus.Unsupported, null, true, !activateTarget, true, false, now);
            }

            profilePattern = pattern;
            var stateBefore = SafeExpandCollapseState(pattern);
            if (stateBefore == ExpandCollapseState.Collapsed)
            {
                openedProfileByProbe = true;
                pattern.Expand();
                AccountQuotaUiaExpander.WaitBriefly(cancellationToken, 700);
            }
            else if (stateBefore != ExpandCollapseState.Expanded)
            {
                return new QuotaProbeResult(QuotaProbeStatus.Unsupported, null, true, false, false, false, now);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var expandedRoot = AutomationElement.FromHandle(target.Handle);
            quotaSection = expandedRoot is null
                ? new AccountQuotaExpansionScope(false, false, false, "Quota section expander: TARGET EXPIRED", null)
                : AccountQuotaUiaExpander.TryExpandQuotaSection(expandedRoot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            snapshot = ParseTargetOwnedQuota(reader, parser, target, now);
            status = ToStatus(snapshot);
        }
        catch
        {
            status = QuotaProbeStatus.Unsupported;
            snapshot = null;
        }
        finally
        {
            quotaSection?.Dispose();
            if (openedProfileByProbe && profilePattern is not null)
            {
                try { profilePattern.Collapse(); }
                catch { /* Diagnostics probe must never fall back to input/mouse workarounds. */ }
            }

            if (activateTarget)
            {
                RestoreForegroundAndWindowState(foregroundBefore, target.Handle, targetWasMinimized, shouldRestoreForeground);
            }
        }

        var foregroundAfter = GetForegroundWindow();
        _ = GetCursorPos(out var cursorAfter);
        var foregroundRestored = !shouldRestoreForeground
            || !IsWindow(foregroundBefore)
            || foregroundAfter == foregroundBefore;
        var mouseUnchanged = cursorBefore.X == cursorAfter.X && cursorBefore.Y == cursorAfter.Y;
        return new QuotaProbeResult(
            status is QuotaProbeStatus.NotRead ? QuotaProbeStatus.Unsupported : status,
            status is QuotaProbeStatus.NotRead or QuotaProbeStatus.Unsupported ? null : snapshot,
            true,
            !activateTarget || foregroundRestored,
            mouseUnchanged,
            false,
            now);
    }

    private static QuotaProbeStatus ToStatus(QuotaSnapshot snapshot)
    {
        var fieldsRead = new[]
        {
            snapshot.ShortWindowRemainingPercent is not null,
            snapshot.ShortWindowResetAt is not null,
            snapshot.WeeklyRemainingPercent is not null,
            snapshot.WeeklyResetAt is not null
        }.Count(value => value);

        return fieldsRead switch
        {
            4 => QuotaProbeStatus.Complete,
            > 0 => QuotaProbeStatus.Partial,
            _ => QuotaProbeStatus.NotRead
        };
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

    private static bool WaitForForeground(nint hwnd, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetForegroundWindow() == hwnd)
            {
                return true;
            }

            AccountQuotaUiaExpander.WaitBriefly(cancellationToken, 50);
        }

        return GetForegroundWindow() == hwnd;
    }

    private static void ActivateWindow(nint hwnd)
    {
        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(hwnd, out _);

        _ = AttachThreadInput(currentThread, targetThread, true);
        if (foregroundThread != 0)
        {
            _ = AttachThreadInput(currentThread, foregroundThread, true);
        }

        try
        {
            _ = BringWindowToTop(hwnd);
            _ = SetActiveWindow(hwnd);
            _ = SetForegroundWindow(hwnd);
        }
        finally
        {
            if (foregroundThread != 0)
            {
                _ = AttachThreadInput(currentThread, foregroundThread, false);
            }

            _ = AttachThreadInput(currentThread, targetThread, false);
        }
    }

    private static void RestoreForegroundAndWindowState(
        nint foregroundBefore,
        nint targetHwnd,
        bool targetWasMinimized,
        bool shouldRestoreForeground)
    {
        try
        {
            if (shouldRestoreForeground && IsWindow(foregroundBefore))
            {
                ActivateWindow(foregroundBefore);
                Thread.Sleep(150);
            }
        }
        catch
        {
            // Manual quota probe should report the restore check instead of cascading into another failure.
        }

        if (!targetWasMinimized || !IsWindow(targetHwnd))
        {
            return;
        }

        try
        {
            _ = ShowWindow(targetHwnd, ShowMinimize);
        }
        catch
        {
            // Best-effort restoration only; never use input simulation as a fallback.
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint SetActiveWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    private const int ShowRestore = 9;
    private const int ShowMinimize = 6;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using GPTAutoResume.Core;

namespace GPTAutoResume.Automation;

public sealed class UiAutomationWorkNavigator(UiAutomationReader reader) : IWorkNavigator
{
    internal static string RowId(AutomationElement row) => string.Join(".", row.GetRuntimeId());
    private static bool Highlighted(AutomationElement row) => row.Current.ClassName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Contains("bg-primary-ghost-hover", StringComparer.Ordinal);

    internal static IReadOnlyList<AutomationElement> Rows(nint hwnd, CancellationToken cancellationToken = default)
    {
        var results = new List<AutomationElement>();
        var timer = Stopwatch.StartNew();
        var count = 0;
        var cache = new CacheRequest { TreeScope = TreeScope.Subtree, TreeFilter = System.Windows.Automation.Automation.RawViewCondition };
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.ClassNameProperty);
        void Visit(AutomationElement element, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > 48 || ++count > 12000 || timer.Elapsed > TimeSpan.FromSeconds(5))
                throw new InvalidOperationException("Navigation tree incomplete");
            if (element.Cached.ControlType == ControlType.Button
                && element.Cached.ClassName.Contains("app-action-sidebar-thread-selected", StringComparison.Ordinal))
                results.Add(element);
            foreach (AutomationElement child in element.CachedChildren) Visit(child, depth + 1);
        }
        Visit(AutomationElement.FromHandle(hwnd).GetUpdatedCache(cache), 0);
        return results;
    }

    public static bool IsRowCurrent(nint hwnd, string rowId, string titleHash)
    {
        try
        {
            var matches = Rows(hwnd).Where(row => RowId(row) == rowId).ToArray();
            return matches.Length == 1 && Highlighted(matches[0])
                && ConversationTargetIdentity.Hash(matches[0].Current.Name) == titleHash;
        }
        catch { return false; }
    }

    public IWorkVisit? TryVisit(TargetWindow window, WorkSelectionRecord work, CancellationToken cancellationToken)
    {
        Visit? visit = null;
        try
        {
            var foreground = GetForegroundWindow();
            var inputStamp = LastInput();
            if (!work.AutoResumeEnabled || work.IsStale || !new WindowScanner().IsStillValid(window) || IsIconic(window.Handle)) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var originalIdentity = reader.CaptureConversationIdentity(window.Handle);
            if (originalIdentity is null) return null;
            var rows = Rows(window.Handle, cancellationToken);
            // Legacy hashes locate diagnostic candidates only. A runtime id acquired after matching
            // a title does not prove that the user selected this particular conversation.
            var candidates = rows.Where(row => JsonWorkSelectionStore.BuildHash(originalIdentity with
                { ConversationTitleHash = ConversationTargetIdentity.Hash(row.Current.Name) }) == work.ConversationIdentityHash).ToArray();
            var originals = rows.Where(row => ConversationTargetIdentity.Hash(row.Current.Name) == originalIdentity.ConversationTitleHash).ToArray();
            if (candidates.Length != 1 || originals.Length != 1 || !Highlighted(originals[0])) return null;
            var candidate = candidates[0];
            if (candidate.Current.IsOffscreen || !candidate.Current.IsEnabled
                || !candidate.TryGetCurrentPattern(InvokePattern.Pattern, out var raw)) return null;
            if (GetForegroundWindow() != foreground || LastInput() != inputStamp) return null;
            visit = new Visit(reader, window, originals[0], originalIdentity, candidate, foreground, inputStamp);
            if (GetForegroundWindow() != window.Handle) SetForegroundWindow(window.Handle);
            if (GetForegroundWindow() != window.Handle) { visit.Dispose(); return null; }
            if (LastInput() != inputStamp) { visit.Dispose(); return null; }
            ((InvokePattern)raw).Invoke();
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(10))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (LastInput() != visit.InputStamp) break;
                var identity = reader.CaptureConversationIdentity(window.Handle);
                if (identity is not null && Highlighted(candidate)
                    && JsonWorkSelectionStore.BuildHash(identity) == work.ConversationIdentityHash
                    && reader.FindChatInputForDiscovery(window.Handle) is not null)
                {
                    visit.Identity = identity with { NavigationRowIdentity = RowId(candidate) };
                    if (visit.IsCurrent) return visit;
                }
                if (cancellationToken.WaitHandle.WaitOne(150)) cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) { visit?.Dispose(); throw; }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { }
        visit?.Dispose();
        return null;
    }

    private sealed class Visit(UiAutomationReader reader, TargetWindow window, AutomationElement original,
        ConversationTargetIdentity originalIdentity, AutomationElement candidate, nint foreground, uint inputStamp) : IWorkVisit
    {
        private bool disposed;
        public uint InputStamp => inputStamp;
        public bool SelectionIdentityVerified => false;
        private ConversationTargetIdentity? acquiredIdentity;
        public ConversationTargetIdentity Identity { get => acquiredIdentity ?? originalIdentity; set => acquiredIdentity = value; }
        public bool IsCurrent => !disposed && LastInput() == inputStamp && GetForegroundWindow() == window.Handle
            && reader.IsConversationStillActive(window.Handle, Identity);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                // A user navigation/input during inspection takes precedence over restoration.
                if (LastInput() != inputStamp || GetForegroundWindow() != window.Handle) return;
                var active = reader.CaptureConversationIdentity(window.Handle);
                if (active is null || (active.ConversationTitleHash != Identity.ConversationTitleHash
                    && active.ConversationTitleHash != ConversationTargetIdentity.Hash(candidate.Current.Name))) return;
                ((InvokePattern)original.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                var timer = Stopwatch.StartNew();
                while (timer.Elapsed < TimeSpan.FromSeconds(5))
                {
                    if (LastInput() != inputStamp) return;
                    if (reader.IsConversationStillActive(window.Handle, originalIdentity)) break;
                    Thread.Sleep(150);
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { }
            finally
            {
                if (LastInput() == inputStamp && GetForegroundWindow() == window.Handle && IsWindow(foreground))
                    SetForegroundWindow(foreground);
            }
        }
    }

    private static uint LastInput()
    {
        var data = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref data)) throw new InvalidOperationException("Input activity unavailable");
        return data.Tick;
    }
    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { public uint Size; public uint Tick; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo data);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
}

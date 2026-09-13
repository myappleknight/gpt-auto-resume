using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;
using GPTAutoResume.Core;

namespace GPTAutoResume.Automation;

public interface IResumeInteractionHost
{
    bool IsStillValid(TargetWindow target);
    void ActivateTargetWindow(nint hwnd);
    nint GetForegroundWindow();
    IChatInputController? FindChatInput(nint hwnd);
    void FallbackTypeText(string text);
    void SendEnter();
}

public interface IChatInputController
{
    bool SupportsValuePattern { get; }
    void SetFocus();
    void SetValue(string text);
    string? ReadText();
}

public sealed class ResumeSender : IResumeSender
{
    private readonly IResumeInteractionHost _host;
    private readonly IProductionEventLog _trace;
    private readonly IConversationIdentityProvider? _identityProvider;

    public ResumeSender(IWindowScanner scanner, IUiAutomationReader reader)
        : this(new UiaResumeInteractionHost(scanner, reader), new GPTAutoResume.Core.ProductionEventLog())
    {
        _identityProvider = reader as IConversationIdentityProvider;
    }

    public ResumeSender(IResumeInteractionHost host, GPTAutoResume.Core.IProductionEventLog? trace = null)
    {
        _host = host;
        _trace = trace ?? new GPTAutoResume.Core.NullProductionEventLog();
    }

    private void Trace(string stage, string outcome, TargetWindow target) =>
        _trace.Write(new GPTAutoResume.Core.ProductionTrace(DateTimeOffset.Now, stage, outcome, target.ProcessId, target.Handle.ToString()));

    public bool VerifyTarget(TargetWindow target)
    {
        if (!_host.IsStillValid(target))
        {
            Trace("SENDER_WINDOW", "FAIL", target);
            return false;
        }

        Trace("ACTIVATE_TARGET", "START", target);
        _host.ActivateTargetWindow(target.Handle);
        Trace("ACTIVATE_TARGET", "RETURNED", target);
        Thread.Sleep(600);
        if (_host.GetForegroundWindow() != target.Handle)
        {
            Trace("FOREGROUND", "FAIL", target);
            return false;
        }

        var input = _host.FindChatInput(target.Handle);
        Trace("COMPOSER_FOUND", input is null ? "FAIL" : "PASS", target);
        return input is not null;
    }

    public bool TrySend(TargetWindow target, string text, bool dryRun, bool sendEnter)
    {
        var identity = _identityProvider?.CaptureConversationIdentity(target.Handle);
        return TrySend(target, text, dryRun, sendEnter, () => _identityProvider is null
            || identity is not null && _identityProvider.IsConversationStillActive(target.Handle, identity));
    }

    public bool TrySend(TargetWindow target, string text, bool dryRun, bool sendEnter, Func<bool> verifyConversation)
    {
        if (!verifyConversation() || !VerifyTarget(target) || !verifyConversation())
        {
            return false;
        }

        if (dryRun)
        {
            Trace("DRY_RUN_ABORT", "SAFETY BLOCKED", target);
            return true;
        }

        var input = _host.FindChatInput(target.Handle);
        if (input is null)
        {
            return false;
        }
        try
        {
            input.SetFocus();
            Thread.Sleep(150);
            if (_host.GetForegroundWindow() != target.Handle)
            {
                return false;
            }

            var sameConversation = verifyConversation();
            var validWindow = _host.IsStillValid(target);
            var foreground = _host.GetForegroundWindow();
            if (!sameConversation || !validWindow || foreground != target.Handle)
            {
                Trace("PRE_INPUT_GUARD", $"ABORT;SameConversation={sameConversation};ValidWindow={validWindow};Foreground={foreground}", target);
                return false;
            }
            var draft = input.ReadText();
            var normalizedDraft = draft?.TrimEnd('\r', '\n');
            var alreadyInsertedAuthorizedDraft = string.Equals(normalizedDraft, text, StringComparison.Ordinal);
            if (draft is null || (!string.IsNullOrWhiteSpace(draft) && !alreadyInsertedAuthorizedDraft))
            {
                Trace("PRE_INPUT_GUARD", draft is null ? "ABORT_DRAFT_UNREADABLE" : "ABORT_DRAFT_NOT_EMPTY", target);
                return false;
            }

            var verified = alreadyInsertedAuthorizedDraft;
            if (alreadyInsertedAuthorizedDraft)
            {
                Trace("PRE_INPUT_GUARD", "AUTHORIZED_DRAFT_ALREADY_PRESENT", target);
            }
            else if (input.SupportsValuePattern)
            {
                input.SetValue(text);
                Trace("INPUT_PROVIDER_RETURNED", "NOT_YET_VISUALLY_VERIFIED", target);
            }
            else
            {
                return false;
            }

            for (var attempt = 0; !verified && attempt < 20; attempt++)
            {
                if (string.Equals(input.ReadText()?.TrimEnd('\r', '\n'), text, StringComparison.Ordinal))
                { verified = true; break; }
                Thread.Sleep(100);
            }

            if (!verified)
            {
                var afterProvider = input.ReadText();
                if (afterProvider is null || !string.IsNullOrWhiteSpace(afterProvider))
                {
                    Trace("INPUT_FALLBACK", afterProvider is null ? "ABORT_DRAFT_UNREADABLE" : "ABORT_DRAFT_CHANGED", target);
                    return false;
                }

                if (!verifyConversation() || !_host.IsStillValid(target) || _host.GetForegroundWindow() != target.Handle)
                {
                    Trace("INPUT_FALLBACK", "ABORT_PRECONDITION_CHANGED", target);
                    return false;
                }

                input.SetFocus();
                Thread.Sleep(100);
                _host.FallbackTypeText(text);
                Trace("INPUT_FALLBACK", "PROVIDER_RETURNED", target);

                for (var attempt = 0; attempt < 20; attempt++)
                {
                    if (string.Equals(input.ReadText()?.TrimEnd('\r', '\n'), text, StringComparison.Ordinal))
                    { verified = true; break; }
                    Thread.Sleep(100);
                }
            }

            Trace("INPUT_READBACK", verified ? "PASS" : "FAIL", target);
            if (!verified || !verifyConversation()) return false;

            if (sendEnter)
            {
                if (_host.GetForegroundWindow() != target.Handle)
                {
                    return false;
                }

                _host.SendEnter();
                Trace("ENTER_PROVIDER_RETURNED", "NOT_YET_POST_SUBMIT_VERIFIED", target);
            }

            return true;
        }
        catch (Exception ex)
        {
            Trace("INPUT_EXCEPTION", $"{ex.GetType().Name};HResult={ex.HResult:X8}", target);
            return false;
        }
    }

    public TargetWindow? FirstTarget()
    {
        return _host is UiaResumeInteractionHost uiaHost ? uiaHost.FirstTarget() : null;
    }

    public bool TryClearMatchingDraft(TargetWindow target, string expected, Func<bool> verifyConversation)
    {
        if (string.IsNullOrEmpty(expected) || !VerifyTarget(target)) return false;
        try
        {
            var input = _host.FindChatInput(target.Handle);
            if (input is null || !input.SupportsValuePattern) return false;
            input.SetFocus();
            if (!verifyConversation() || _host.GetForegroundWindow() != target.Handle
                || input.ReadText()?.TrimEnd('\r', '\n') != expected) return false;
            input.SetValue("");
            for (var i = 0; i < 10; i++)
            {
                if (input.ReadText() is { } value && string.IsNullOrWhiteSpace(value)) return true;
                Thread.Sleep(100);
            }
        }
        catch (Exception ex) { Trace("CLEAR_TEST_DRAFT", $"{ex.GetType().Name};{ex.HResult:X8}", target); }
        return false;
    }

    private sealed class UiaResumeInteractionHost(IWindowScanner scanner, IUiAutomationReader reader) : IResumeInteractionHost
    {
        public bool IsStillValid(TargetWindow target) => scanner.IsStillValid(target);
        public void ActivateTargetWindow(nint hwnd) => ResumeSender.ActivateTargetWindow(hwnd);
        public nint GetForegroundWindow() => ResumeSender.GetForegroundWindow();
        public IChatInputController? FindChatInput(nint hwnd)
        {
            var element = reader.FindChatInput(hwnd);
            return element is null ? null : new UiaChatInputController(element);
        }

        public void FallbackTypeText(string text)
        {
            System.Windows.IDataObject? previousClipboard = null;
            try { previousClipboard = System.Windows.Clipboard.GetDataObject(); }
            catch { }

            try
            {
                System.Windows.Clipboard.SetText(text);
                SendKeys.SendWait("^v");
                Thread.Sleep(100);
            }
            finally
            {
                if (previousClipboard is not null)
                {
                    try { System.Windows.Clipboard.SetDataObject(previousClipboard, true); }
                    catch { }
                }
            }
        }
        public void SendEnter() => SendKeys.SendWait("{ENTER}");
        public TargetWindow? FirstTarget() => scanner.FindTargets().FirstOrDefault();
    }

    private sealed class UiaChatInputController(AutomationElement element) : IChatInputController
    {
        public string? ReadText()
        {
            string? result = null;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var text) && text is TextPattern textPattern)
                result = textPattern.DocumentRange.GetText(-1);
            else if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && value is ValuePattern valuePattern)
                result = valuePattern.Current.Value;
            var walker = TreeWalker.RawViewWalker;
            var child = walker.GetFirstChild(element);
            var placeholder = child is not null && walker.GetNextSibling(child) is null
                && child.Current.ClassName.Split(' ').Contains("placeholder", StringComparer.Ordinal)
                && string.IsNullOrWhiteSpace(child.Current.Name);
            return ComposerText.Normalize(result, element.Current.Name, placeholder, element.Current.ClassName);
        }
        public bool SupportsValuePattern => element.TryGetCurrentPattern(ValuePattern.Pattern, out _);
        public void SetFocus() => element.SetFocus();
        public void SetValue(string text)
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var raw) && raw is ValuePattern valuePattern)
            {
                valuePattern.SetValue(text);
            }
        }
    }

    private static void ActivateTargetWindow(nint hwnd)
    {
        ShowWindow(hwnd, 9);
        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(hwnd, out _);

        _ = AttachThreadInput(currentThread, targetThread, true);
        _ = AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            _ = BringWindowToTop(hwnd);
            _ = SetActiveWindow(hwnd);
            _ = SetForegroundWindow(hwnd);
        }
        finally
        {
            _ = AttachThreadInput(currentThread, foregroundThread, false);
            _ = AttachThreadInput(currentThread, targetThread, false);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint SetActiveWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}

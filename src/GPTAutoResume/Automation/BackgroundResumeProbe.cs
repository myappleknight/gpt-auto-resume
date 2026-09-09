using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;

namespace GPTAutoResume.Automation;

public static class BackgroundResumeProbe
{
    public static string Run(string root)
    {
        var diagnosticsDir = Path.Combine(root, "diagnostics");
        Directory.CreateDirectory(diagnosticsDir);
        var path = Path.Combine(diagnosticsDir, "background-resume-probe.txt");
        File.WriteAllText(path, "BackgroundSetValue: STARTED");
        try
        {
            RunCore(path);
        }
        catch (Exception ex)
        {
            File.AppendAllText(path, $"{Environment.NewLine}BackgroundSetValue: FAIL{Environment.NewLine}ErrorType: {ex.GetType().FullName}{Environment.NewLine}ErrorMessage: {ex.Message}{Environment.NewLine}GlobalKeyboardUsed: NO{Environment.NewLine}SubmitInvoked: NO{Environment.NewLine}");
        }

        return path;
    }

    private static void RunCore(string path)
    {
        var scanner = new WindowScanner();
        var target = scanner.FindTargets().FirstOrDefault();
        if (target is null)
        {
            File.WriteAllText(path, "Target: NOT FOUND");
            return;
        }

        File.WriteAllText(path,
            $"""
            Target: FOUND
            Process: {target.ProcessName}
            ProcessId: {target.ProcessId}
            HWND: {target.Handle}
            Title: {target.Title}
            """);

        File.AppendAllText(path, $"{Environment.NewLine}Stage: BEFORE_FOREGROUND_CAPTURE{Environment.NewLine}");
        var inputSummary = "NOT FOUND";
        var sendButtonSummary = "NOT FOUND";
        var foregroundBefore = GetForegroundWindow();
        var mouseBefore = Cursor.Position;
        File.AppendAllText(path, $"ForegroundBefore: {foregroundBefore}{Environment.NewLine}MouseBefore: {mouseBefore.X},{mouseBefore.Y}{Environment.NewLine}Input: NOT TESTED - background FindChatInput avoided after probe instability{Environment.NewLine}Stage: BEFORE_SEND_BUTTON_DISCOVERY{Environment.NewLine}");
        var sendButton = FindSendButton(target.Handle);
        sendButtonSummary = DescribeSendButton(sendButton);
        File.AppendAllText(path, $"SendButton: {sendButtonSummary}{Environment.NewLine}Stage: BACKGROUND_SET_VALUE_SKIPPED_AFTER_PREVIOUS_TIMEOUT{Environment.NewLine}");

        var foregroundAfter = GetForegroundWindow();
        var mouseAfter = Cursor.Position;
        File.AppendAllText(path,
            $"""

            InputFinal: {inputSummary}
            BackgroundSetValue: PARTIAL_NOT_EXECUTED_AFTER_PREVIOUS_TIMEOUT
            Cleanup: NOT NEEDED
            ForegroundBefore: {foregroundBefore}
            ForegroundAfter: {foregroundAfter}
            ForegroundUnchanged: {foregroundBefore == foregroundAfter}
            MouseBefore: {mouseBefore.X},{mouseBefore.Y}
            MouseAfter: {mouseAfter.X},{mouseAfter.Y}
            MouseUnchanged: {mouseBefore == mouseAfter}
            GlobalKeyboardUsed: NO
            SendButtonFinal: {sendButtonSummary}
            SubmitInvoked: NO
            """);
    }

    private static AutomationElement? FindSendButton(nint hwnd)
    {
        var root = AutomationElement.FromHandle(hwnd);
        if (root is null)
        {
            return null;
        }

        return root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
            .Cast<AutomationElement>()
            .Where(button => button.Current.IsEnabled)
            .FirstOrDefault(button =>
                button.Current.Name.Contains("send", StringComparison.OrdinalIgnoreCase)
                || button.Current.Name.Contains("送出", StringComparison.OrdinalIgnoreCase)
                || button.Current.Name.Contains("送信", StringComparison.OrdinalIgnoreCase)
                || button.Current.AutomationId.Contains("send", StringComparison.OrdinalIgnoreCase));
    }

    private static string DescribeSendButton(AutomationElement? button)
    {
        if (button is null)
        {
            return "NOT FOUND";
        }

        try
        {
            return $"ControlType={button.Current.ControlType.ProgrammaticName}; Class={button.Current.ClassName}; AutomationId={button.Current.AutomationId}; Name={button.Current.Name}; IsEnabled={button.Current.IsEnabled}; InvokePattern={button.TryGetCurrentPattern(InvokePattern.Pattern, out _)}";
        }
        catch (ElementNotAvailableException)
        {
            return "NOT FOUND - element expired";
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}

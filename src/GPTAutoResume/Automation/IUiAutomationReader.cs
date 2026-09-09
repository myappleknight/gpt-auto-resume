using System.Windows.Automation;

namespace GPTAutoResume.Automation;

public interface IUiAutomationReader
{
    string ReadVisibleText(nint hwnd, out bool hasWarningRole);
    AutomationElement? FindChatInput(nint hwnd);
    string DumpTree(nint hwnd);
    string DescribeElement(AutomationElement? element);
}

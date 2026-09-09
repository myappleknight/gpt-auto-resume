using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GPTAutoResume.Automation;

public sealed class WindowScanner : IWindowScanner
{
    private static readonly string[] ProcessHints = ["ChatGPT", "Codex", "OpenAI"];
    private static readonly string[] TitleHints = ["ChatGPT", "Codex"];

    public IReadOnlyList<TargetWindow> FindTargets()
    {
        var windows = new List<TargetWindow>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            var title = GetTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out var pid);
            Process? process = null;
            try
            {
                process = Process.GetProcessById((int)pid);
            }
            catch
            {
                return true;
            }

            if (IsTarget(process.ProcessName, title))
            {
                windows.Add(new TargetWindow(process.Id, hwnd, title, process.ProcessName));
            }

            return true;
        }, nint.Zero);

        return windows;
    }

    public bool IsStillValid(TargetWindow target)
    {
        try
        {
            var process = Process.GetProcessById(target.ProcessId);
            return !process.HasExited
                && IsWindow(target.Handle)
                && IsWindowVisible(target.Handle)
                && IsTarget(process.ProcessName, GetTitle(target.Handle));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTarget(string processName, string title) =>
        ProcessHints.Any(h => processName.Contains(h, StringComparison.OrdinalIgnoreCase))
        && TitleHints.Any(h => title.Contains(h, StringComparison.OrdinalIgnoreCase));

    private static string GetTitle(nint hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length == 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        _ = GetWindowText(hwnd, buffer, buffer.Length);
        return new string(buffer).TrimEnd('\0');
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, char[] lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hWnd);
}

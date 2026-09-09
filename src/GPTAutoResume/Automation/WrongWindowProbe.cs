using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GPTAutoResume.Automation;

public static class WrongWindowProbe
{
    public static string Run(string root)
    {
        var diagnosticsDir = Path.Combine(root, "diagnostics");
        Directory.CreateDirectory(diagnosticsDir);
        var outputPath = Path.Combine(diagnosticsDir, "wrong-window-probe.txt");
        Process? notepad = null;

        try
        {
            var scanner = new WindowScanner();
            var reader = new UiAutomationReader();
            var sender = new ResumeSender(scanner, reader);
            var target = scanner.FindTargets().FirstOrDefault();
            if (target is null)
            {
                File.WriteAllText(outputPath, "Result: FAIL\nReason: No ChatGPT/Codex target window found.");
                return outputPath;
            }

            notepad = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
            Thread.Sleep(800);
            var notepadWindow = notepad?.MainWindowHandle ?? nint.Zero;
            if (notepadWindow != nint.Zero)
            {
                SetForegroundWindow(notepadWindow);
                Thread.Sleep(250);
            }

            var foregroundBefore = GetForegroundWindow();
            var result = sender.TrySend(target, "請繼續", dryRun: true, sendEnter: false);
            var foregroundAfter = GetForegroundWindow();

            File.WriteAllText(outputPath,
                $"""
                Scenario: ChatGPT + Notepad foreground
                TargetProcess: {target.ProcessName}
                TargetProcessId: {target.ProcessId}
                TargetHWND: {target.Handle}
                TargetTitle: {target.Title}
                ForegroundBefore: {foregroundBefore}
                ForegroundAfter: {foregroundAfter}
                ReturnedToTarget: {foregroundAfter == target.Handle}
                Result: {(result && foregroundAfter == target.Handle ? "PASS" : "FAIL")}
                EnterSent: NO
                TextInserted: NO
                """);
        }
        catch (Exception ex)
        {
            File.WriteAllText(outputPath,
                $"""
                Result: FAIL
                ErrorType: {ex.GetType().FullName}
                ErrorMessage: {ex.Message}
                EnterSent: NO
                TextInserted: NO
                """);
        }
        finally
        {
            try
            {
                if (notepad is not null && !notepad.HasExited)
                {
                    notepad.Kill();
                }
            }
            catch
            {
                // Probe cleanup should never mask the probe result.
            }
        }

        return outputPath;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}

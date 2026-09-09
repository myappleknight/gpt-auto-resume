using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GPTAutoResume.Automation;

// A UIA provider can block inside a COM call. Only an owned process gives the caller
// a cancellation boundary without abandoning a live scan on another thread.
internal static class CompletionScanWorker
{
    private sealed record Request(int ProcessId, long Handle, ConversationTargetIdentity Identity);

    internal static WorkCompletionAudit? Read(TargetWindow target, ConversationTargetIdentity identity, CancellationToken token)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "GPTAutoResume.exe");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            return Task.Run(async () =>
            {
                using var process = new Process { StartInfo = new ProcessStartInfo(executable, "--completion-scan-worker")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                }};
                process.Start();
                var stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new Request(target.ProcessId,
                        target.Handle.ToInt64(), identity)).AsMemory(), timeout.Token);
                    process.StandardInput.Close();
                    var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                    await process.WaitForExitAsync(timeout.Token);
                    return process.ExitCode == 0 ? JsonSerializer.Deserialize<WorkCompletionAudit>(output) : null;
                }
                finally
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                    await stderr;
                }
            }, timeout.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or JsonException
            or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    internal static int Run()
    {
        var line = Console.ReadLine();
        if (line is null || line.Length > 8192) return 1;
        var request = JsonSerializer.Deserialize<Request>(line);
        if (request is null) return 1;
        var target = new WindowScanner().FindTargets().SingleOrDefault(t => t.ProcessId == request.ProcessId
            && t.Handle.ToInt64() == request.Handle);
        if (target is null) return 1;
        var report = new UiAutomationWorkCompletionProvider(new UiAutomationReader())
            .ReadAuditInProcess(target, request.Identity);
        Console.Write(JsonSerializer.Serialize(report));
        return 0;
    }
}

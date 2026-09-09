using System.Windows;
using System.IO;
using GPTAutoResume.Automation;
using GPTAutoResume.Core;

namespace GPTAutoResume;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _singleInstance;

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is MainWindow mainWindow)
        {
            mainWindow.DisposeViewModel();
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--completion-scan-worker", StringComparer.Ordinal))
        {
            try { Shutdown(CompletionScanWorker.Run()); }
            catch { Shutdown(1); }
            return;
        }
        Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "diagnostics"));
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog(args.Exception);
            args.Handled = true;
            Shutdown(1);
        };
        if (e.Args.Length > 0)
        {
            File.WriteAllLines(
                Path.Combine(Environment.CurrentDirectory, "diagnostics", $"startup-args-{DateTimeOffset.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.txt"),
                e.Args);
        }

        if (e.Args.Contains("--work-completion-probe", StringComparer.OrdinalIgnoreCase)
            || e.Args.Contains("--footer-audit", StringComparer.OrdinalIgnoreCase))
        {
            var reader = new UiAutomationReader();
            var provider = new UiAutomationWorkCompletionProvider(reader);
            var reports = new List<object>();
            foreach (var target in new WindowScanner().FindTargets())
            {
                var identity = reader.CaptureConversationIdentity(target.Handle);
                var audit = identity is null ? null : provider.ReadAudit(target, identity);
                reports.Add(new { target.ProcessId, Hwnd = target.Handle.ToString(),
                    Selected = new JsonWorkSelectionStore().IsAutoResumeEnabled(identity),
                    Evidence = identity is null ? AssistantCompletionEvidence.Unknown("NO_IDENTITY") : audit!.Evidence,
                    Audit = audit });
            }
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "diagnostics", "work-completion-probe.json"),
                System.Text.Json.JsonSerializer.Serialize(new { ReadOnly = true, CapturedAt = DateTimeOffset.Now, Targets = reports },
                    options));
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--real-limit-uia-audit", StringComparer.OrdinalIgnoreCase))
        {
            ProductionLimitAudit.Capture(Path.Combine(Environment.CurrentDirectory, "diagnostics"));
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--uia-discovery", StringComparer.OrdinalIgnoreCase))
        {
            var report = DiscoveryTool.CaptureToFile(Environment.CurrentDirectory);
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "diagnostics", "uia-summary.txt"),
                $"""
                WindowFound: {report.WindowFound}
                Process: {report.ProcessName}
                ProcessId: {report.ProcessId}
                HWND: {report.WindowHandle}
                Title: {report.WindowTitle}
                EditableControl: {(report.EditableControlFound ? "FOUND" : "NOT FOUND")}
                EditableControlDetails: {report.EditableControlSummary}
                MessageTextAccess: {report.MessageTextAccessStatus}
                TreeDump: {report.TreeDumpPath}
                """);
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--capture-limit-message", StringComparer.OrdinalIgnoreCase))
        {
            LimitMessageCapture.Capture(Environment.CurrentDirectory);
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--wrong-window-probe", StringComparer.OrdinalIgnoreCase))
        {
            WrongWindowProbe.Run(Environment.CurrentDirectory);
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--background-resume-probe", StringComparer.OrdinalIgnoreCase))
        {
            BackgroundResumeProbe.Run(Environment.CurrentDirectory);
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--quota-audit", StringComparer.OrdinalIgnoreCase))
        {
            QuotaAudit.Run(Environment.CurrentDirectory);
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--quota-probe", StringComparer.OrdinalIgnoreCase))
        {
            RunQuotaProbe(Environment.CurrentDirectory);
            Shutdown(0);
            return;
        }

        if (e.Args.Any(arg => arg.StartsWith("--simulate-quota", StringComparison.OrdinalIgnoreCase)))
        {
            RunQuotaSimulation(Environment.CurrentDirectory, e.Args);
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--work-discovery", StringComparer.OrdinalIgnoreCase))
        {
            RecentWorkDiscovery.CaptureToFile(Environment.CurrentDirectory);
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--checked-work-monitor-audit", StringComparer.OrdinalIgnoreCase))
        {
            CheckedWorkMonitorAudit.Run(Path.Combine(Environment.CurrentDirectory, "diagnostics"));
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--work-navigation-audit", StringComparer.OrdinalIgnoreCase))
        {
            WorkNavigationAudit.Run(Path.Combine(Environment.CurrentDirectory, "diagnostics"),
                e.Args.Contains("--inspect-checked", StringComparer.OrdinalIgnoreCase));
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--stable-work-identity-audit", StringComparer.OrdinalIgnoreCase))
        {
            StableWorkIdentityAudit.Run(Path.Combine(Environment.CurrentDirectory, "diagnostics"));
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--desktop-navigation-surface-audit", StringComparer.OrdinalIgnoreCase))
        {
            DesktopNavigationSurfaceAudit.Run(Path.Combine(Environment.CurrentDirectory, "diagnostics"));
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--sidebar-uia-audit", StringComparer.OrdinalIgnoreCase))
        {
            SidebarUiaAudit.Run(Environment.CurrentDirectory);
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--active-work-probe", StringComparer.OrdinalIgnoreCase))
        {
            ActiveWorkProbe.Run(Environment.CurrentDirectory);
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--active-work-e2e-audit", StringComparer.OrdinalIgnoreCase))
        {
            ActiveWorkE2EAudit.Run(Path.Combine(Environment.CurrentDirectory, "diagnostics"));
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--developer-test", StringComparer.OrdinalIgnoreCase))
        {
            RunDeveloperTest(Environment.CurrentDirectory);
            Shutdown(0);
            return;
        }

        if (e.Args.Any(arg => arg.Equals("--dry-run-verify", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--resume-probe=dry", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--insert-test-no-enter", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--resume-probe=insert", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--resume-probe=clear", StringComparison.OrdinalIgnoreCase)))
        {
            RunResumeProbe(Environment.CurrentDirectory, insertText: e.Args.Any(arg =>
                arg.Equals("--insert-test-no-enter", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--resume-probe=insert", StringComparison.OrdinalIgnoreCase)),
                clearText: e.Args.Any(arg => arg.Equals("--resume-probe=clear", StringComparison.OrdinalIgnoreCase)));
            Shutdown(0);
            return;
        }

        _singleInstance = SingleInstanceGuard.TryAcquire();
        if (!_singleInstance.Acquired)
        {
            Shutdown(0);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
        base.OnStartup(e);
    }

    private static void WriteCrashLog(Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "diagnostics"));
            File.WriteAllText(
                Path.Combine(Environment.CurrentDirectory, "diagnostics", $"crash-{DateTimeOffset.Now:yyyyMMddHHmmssfff}.txt"),
                exception?.ToString() ?? "Unknown exception");
        }
        catch
        {
            // Last-resort crash logging must never create a second crash.
        }
    }

    private static void RunDeveloperTest(string root)
    {
        var detector = new UsageLimitDetector(PatternCatalog.LoadDefault(), new RetryTimeParser());
        var now = new DateTimeOffset(2026, 8, 31, 20, 0, 0, TimeSpan.FromHours(8));
        var samples = new[]
        {
            "你已達使用上限，請於晚上 8:35 再試一次",
            "You've reached your usage limit. Try again after Aug 31, 2026 at 8:35 PM.",
            "使用上限に到達しました。20:35 に再試行してください。"
        };
        Directory.CreateDirectory(Path.Combine(root, "diagnostics"));
        var lines = samples.Select(sample =>
        {
            var result = detector.Analyze(sample, now, hasWarningRole: true);
            return $"{result.DetectedLanguage ?? "unknown"} | {result.Kind} | confidence={result.Confidence} | retry={result.RetryAt:yyyy/MM/dd HH:mm}";
        });
        File.WriteAllLines(Path.Combine(root, "diagnostics", "developer-test.txt"), lines);
    }

    private static void RunQuotaSimulation(string root, string[] args)
    {
        Directory.CreateDirectory(Path.Combine(root, "diagnostics"));
        var percent = ReadIntArg(args, "--simulate-quota", 0);
        var reset = ReadStringArg(args, "--reset", "清晨7:28");
        var weeklyPercent = ReadIntArg(args, "--simulate-weekly-quota", 6);
        var weeklyReset = ReadStringArg(args, "--weekly-reset", "9月7日");
        var now = DateTimeOffset.Now;
        var result = QuotaSimulationRunner.Run(percent, reset, weeklyPercent, weeklyReset, now);
        File.WriteAllText(Path.Combine(root, "diagnostics", "quota-simulation.txt"),
            string.Join(Environment.NewLine, new[]
            {
                "Simulation only: YES",
                "Real quota capture: NO",
                "Uses MonitorService: YES",
                "Input:",
                result.InputText,
                "",
                $"ShortWindowRemainingPercent: {result.Snapshot.ShortWindowRemainingPercent?.ToString() ?? "NOT READ"}",
                $"ShortWindowResetAt: {result.Snapshot.ShortWindowResetAt?.ToString("yyyy/MM/dd HH:mm") ?? "NOT READ"}",
                $"WeeklyRemainingPercent: {result.Snapshot.WeeklyRemainingPercent?.ToString() ?? "NOT READ"}",
                $"WeeklyResetAt: {result.Snapshot.WeeklyResetAt?.ToString("yyyy/MM/dd HH:mm") ?? "NOT READ"}",
                $"MonitorServiceState: {result.State}",
                $"EffectiveRetryAt: {result.RetryAt?.ToString("yyyy/MM/dd HH:mm") ?? "NOT BLOCKED"}"
            }));
    }

    private static void RunQuotaProbe(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "diagnostics"));
        var result = QuotaProbe.Run(allowUiInteraction: true, allowControlledForeground: true, CancellationToken.None);
        File.WriteAllText(Path.Combine(root, "diagnostics", "quota-probe.txt"),
            string.Join(Environment.NewLine, new[]
            {
                "Manual quota probe path: YES",
                "Source: Codex signed-in account / account/rateLimits/read",
                "UIA menu expansion: NO",
                "Controlled foreground fallback allowed: NO",
                $"AccountQuotaReadable: {(result.Snapshot is not null ? "YES" : "NO")}",
                $"Status: {result.Status}",
                $"ShortWindowRemainingPercent: {result.Snapshot?.ShortWindowRemainingPercent?.ToString() ?? "NOT READ"}",
                $"ShortWindowResetAt: {result.Snapshot?.ShortWindowResetAt?.ToString("yyyy/MM/dd HH:mm") ?? "NOT READ"}",
                $"WeeklyRemainingPercent: {result.Snapshot?.WeeklyRemainingPercent?.ToString() ?? "NOT READ"}",
                $"WeeklyResetAt: {result.Snapshot?.WeeklyResetAt?.ToString("yyyy/MM/dd HH:mm") ?? "NOT READ"}",
                $"ForegroundRestoredOrUnchanged: {(result.ForegroundUnchanged ? "PASS" : "FAIL")}",
                $"MouseUnchanged: {(result.MouseUnchanged ? "PASS" : "FAIL")}",
                $"UsedUnrelatedDesktopText: {(result.UsedUnrelatedDesktopText ? "YES" : "NO")}",
                "KeyboardSimulationUsed: NO",
                "MouseSimulationUsed: NO",
                $"CapturedAt: {result.CapturedAt:yyyy/MM/dd HH:mm:ss zzz}"
            }));
    }

    private static int ReadIntArg(string[] args, string name, int fallback)
    {
        var value = ReadStringArg(args, name, fallback.ToString());
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string ReadStringArg(string[] args, string name, string fallback)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            var prefix = name + "=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..];
            }
        }

        return fallback;
    }

    private static void RunResumeProbe(string root, bool insertText, bool clearText)
    {
        Directory.CreateDirectory(Path.Combine(root, "diagnostics"));
        var path = Path.Combine(root, "diagnostics", clearText ? "clear-input.txt" : insertText ? "insert-test-no-enter.txt" : "dry-run-verify.txt");
        File.WriteAllText(path, "Probe started\n");
        try
        {
            var scanner = new WindowScanner();
            var reader = new UiAutomationReader();
            var sender = new ResumeSender(scanner, reader);
            var config = new ConfigStore().Load();
            var selections = new JsonWorkSelectionStore();
            var matches = scanner.FindTargets().Select(window => new
            {
                Window = window,
                Identity = reader.CaptureConversationIdentity(window.Handle)
            }).Where(item => item.Identity is not null && selections.IsAutoResumeEnabled(item.Identity)).ToArray();
            var target = matches.Length == 1 ? matches[0].Window : null;
            File.AppendAllText(path, "Target discovery returned\n");
            if (target is null)
            {
                File.WriteAllText(path, "Result: ABORT - no unique active selected Work\nEnterSent: NO");
                return;
            }
            if (clearText)
            {
                var cleared = sender.TryClearMatchingDraft(target, config.ResumeText,
                    () => selections.IsAutoResumeEnabled(matches[0].Identity)
                        && reader.IsConversationStillActive(target.Handle, matches[0].Identity!));
                File.WriteAllText(path, $"Matching test draft cleared: {cleared}\nEnterSent: NO");
                return;
            }

            File.AppendAllText(path, "Composer search started\n");
            var input = reader.FindChatInput(target.Handle);
            File.AppendAllText(path, "Composer search returned\n");
            var inputSummary = reader.DescribeElement(input);
            if (input is not null)
            {
                var value = input.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out var vp)
                    ? ((System.Windows.Automation.ValuePattern)vp).Current.Value : null;
                var documentText = input.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out var tp)
                    ? ((System.Windows.Automation.TextPattern)tp).DocumentRange.GetText(-1) : null;
                new ProductionEventLog().Write(new ProductionTrace(DateTimeOffset.Now, "COMPOSER_VALUE_DIAGNOSTIC",
                    $"ValueLength={value?.Length};TextLength={documentText?.Length};TextTrimEqualsName={documentText?.Trim() == input.Current.Name.Trim()}"));
                var walker = System.Windows.Automation.TreeWalker.RawViewWalker;
                for (var child = walker.GetFirstChild(input); child is not null; child = walker.GetNextSibling(child))
                    new ProductionEventLog().Write(new ProductionTrace(DateTimeOffset.Now, "COMPOSER_CHILD",
                        $"Type={child.Current.ControlType.ProgrammaticName};Class={child.Current.ClassName};Empty={string.IsNullOrWhiteSpace(child.Current.Name)};MatchesPlaceholder={child.Current.Name.Trim() == input.Current.Name.Trim()}"));
            }
            File.AppendAllText(path, "Sender started\n");
            var identity = matches[0].Identity!;
            var ok = sender.TrySend(target, config.ResumeText, dryRun: !insertText, sendEnter: false,
                verifyConversation: () => selections.IsAutoResumeEnabled(identity)
                    && reader.IsConversationStillActive(target.Handle, identity));
            File.WriteAllText(path,
                $"""
                Target: FOUND
                Process: {target.ProcessName}
                ProcessId: {target.ProcessId}
                HWND: {target.Handle}
                Title: {target.Title}
                Editable: {inputSummary}
                Mode: {(clearText ? "CLEAR_INPUT" : insertText ? "INSERT_TEXT_NO_ENTER" : "DRY_RUN")}
                Result: {(ok ? "PASS" : "FAIL")}
                EnterSent: NO
                """);
        }
        catch (Exception ex)
        {
            File.WriteAllText(path,
                $"""
                Target: UNKNOWN
                Mode: {(clearText ? "CLEAR_INPUT" : insertText ? "INSERT_TEXT_NO_ENTER" : "DRY_RUN")}
                Result: FAIL
                EnterSent: NO
                ErrorType: {ex.GetType().FullName}
                ErrorMessage: {ex.Message}
                """);
        }
    }
}

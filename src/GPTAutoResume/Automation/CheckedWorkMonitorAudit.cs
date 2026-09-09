using System.IO;
using System.Text.Json;
using GPTAutoResume.Core;

namespace GPTAutoResume.Automation;

public static class CheckedWorkMonitorAudit
{
    public static void Run(string directory)
    {
        var config = new AppConfig { AutoResume = true, DryRun = true, SendEnter = false, AllowRealSubmit = false,
            RequireWorkSelection = true, ResumePolicy = ResumePolicy.AutomaticForegroundResume, ResumeDelaySeconds = 0 };
        var scanner = new WindowScanner();
        var reader = new UiAutomationReader();
        var trace = new AuditLog();
        var monitor = new MonitorService(config, scanner, reader, new ResumeSender(scanner, reader),
            new InMemoryEventStore(), new NullPossibleLimitDiagnosticStore(), new JsonWorkSelectionStore(),
            PatternCatalog.LoadDefault(), new RetryTimeParser(), trace);
        var observations = new List<object>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        for (var i = 0; i < 2; i++)
        {
            monitor.Tick(DateTimeOffset.Now, deadline.Token);
            observations.Add(new { State = monitor.State.ToString(), monitor.ResumeStatusText, monitor.RetryAt });
            if (i == 0 && deadline.Token.WaitHandle.WaitOne(5500)) deadline.Token.ThrowIfCancellationRequested();
        }
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "checked-work-monitor-audit.json"), JsonSerializer.Serialize(new
        {
            CapturedAt = DateTimeOffset.Now, RealUia = true, config.DryRun, config.SendEnter, config.AllowRealSubmit,
            TestDelaySeconds = 0, Journal = "InMemoryOnly", Observations = observations, Events = trace.Events
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class AuditLog : IProductionEventLog
    {
        public List<ProductionTrace> Events { get; } = [];
        public void Write(ProductionTrace trace) => Events.Add(trace);
    }
}

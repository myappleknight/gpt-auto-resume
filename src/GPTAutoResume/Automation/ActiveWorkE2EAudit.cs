using System.IO;
using System.Text.Json;
using GPTAutoResume.Core;

namespace GPTAutoResume.Automation;

public static class ActiveWorkE2EAudit
{
    public static void Run(string directory)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        Directory.CreateDirectory(directory);
        var config = new ConfigStore().Load();
        var scanner = new WindowScanner();
        var reader = new UiAutomationReader();
        var selections = new JsonWorkSelectionStore();
        var completion = new UiAutomationWorkCompletionProvider(reader);
        var quota = CodexAccountQuotaProvider.Shared.Read(forceRefresh: true, timeout.Token);
        var targets = new List<object>();

        foreach (var target in scanner.FindTargets())
        {
            var identity = reader.CaptureConversationIdentity(target.Handle);
            var selected = selections.IsAutoResumeEnabled(identity);
            AssistantCompletionEvidence evidence = AssistantCompletionEvidence.Unknown("NO_IDENTITY");
            if (identity is not null)
                evidence = completion.Read(target, identity, timeout.Token);
            var quotaAvailable = quota is { ShortWindowRemainingPercent: > 0, WeeklyRemainingPercent: > 0 };
            var wouldAttempt = selected && quotaAvailable && evidence.IsIncomplete
                && config.AutoResume && config.ResumePolicy == ResumePolicy.AutomaticForegroundResume;
            targets.Add(new
            {
                target.ProcessId,
                Hwnd = target.Handle.ToString(),
                target.Title,
                ActiveWorkIdentityHash = identity is null ? null : JsonWorkSelectionStore.BuildHash(identity),
                ActiveWorkPermitted = selected,
                QuotaAvailable = quotaAvailable,
                evidence.LastTurnState,
                evidence.Footer,
                evidence.Stopped,
                evidence.IsIncomplete,
                evidence.Reason,
                WouldAttemptResumeIfSafetyFlagsEnabled = wouldAttempt,
                BlockingReason = BlockingReason(config, selected, quotaAvailable, evidence)
            });
        }

        File.WriteAllText(Path.Combine(directory, "active-work-e2e-audit.json"), JsonSerializer.Serialize(new
        {
            CapturedAt = DateTimeOffset.Now,
            ReadOnly = true,
            InputPerformed = false,
            EnterPerformed = false,
            RealSubmitLocked = config.DryRun || !config.SendEnter || !config.AllowRealSubmit,
            RuntimeConfig = new
            {
                config.AutoResume,
                config.ResumePolicy,
                config.RequireWorkSelection,
                config.DryRun,
                config.SendEnter,
                config.AllowRealSubmit
            },
            Quota = quota is null ? null : new
            {
                quota.ShortWindowRemainingPercent,
                quota.ShortWindowResetAt,
                quota.WeeklyRemainingPercent,
                quota.WeeklyResetAt,
                quota.CapturedAt
            },
            Targets = targets,
            Scope = "v0.1 Active Work only. Checked non-active Work is not navigated automatically."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string BlockingReason(AppConfig config, bool selected, bool quotaAvailable, AssistantCompletionEvidence evidence)
    {
        if (!config.AutoResume) return "AUTO_RESUME_OFF";
        if (config.ResumePolicy != ResumePolicy.AutomaticForegroundResume) return "POLICY_NOT_AUTOMATIC";
        if (!selected) return "ACTIVE_WORK_NOT_PERMITTED";
        if (!quotaAvailable) return "QUOTA_NOT_AVAILABLE";
        if (!evidence.IsIncomplete) return evidence.LastTurnState switch
        {
            LastTurnState.Running => "ACTIVE_WORK_RUNNING",
            LastTurnState.Completed => "ACTIVE_WORK_COMPLETED",
            _ => $"ACTIVE_WORK_NOT_CONFIRMED_INCOMPLETE:{evidence.Reason}"
        };
        if (config.DryRun || !config.SendEnter || !config.AllowRealSubmit) return "REAL_SUBMIT_LOCKED";
        return "READY_FOR_PRODUCTION_RESUME_PATH";
    }
}

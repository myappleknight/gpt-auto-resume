using System.IO;
using System.Text.Json;
using GPTAutoResume.Core;

namespace GPTAutoResume.Automation;

public static class WorkNavigationAudit
{
    public static void Run(string directory, bool inspectChecked = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var reports = new List<object>();
        var reader = new UiAutomationReader();
        var navigator = new UiAutomationWorkNavigator(reader);
        foreach (var target in new WindowScanner().FindTargets())
        {
            var rows = UiAutomationWorkNavigator.Rows(target.Handle, timeout.Token).Select(row => new
            {
                Title = row.Current.Name,
                row.Current.AutomationId,
                row.Current.IsOffscreen,
                RuntimeId = UiAutomationWorkNavigator.RowId(row),
                Patterns = row.GetSupportedPatterns().Select(p => p.ProgrammaticName).ToArray()
            }).ToArray();
            var inspections = new List<object>();
            if (inspectChecked)
            {
                foreach (var work in new JsonWorkSelectionStore().Load().Where(w => w.AutoResumeEnabled && !w.IsStale))
                {
                    var original = reader.CaptureConversationIdentity(target.Handle);
                    using (var visit = navigator.TryVisit(target, work, timeout.Token))
                    {
                        var audit = visit is { IsCurrent: true }
                            ? new UiAutomationWorkCompletionProvider(reader).ReadAudit(target, visit.Identity, timeout.Token) : null;
                        inspections.Add(new { work.ConversationIdentityHash,
                            CandidateLocated = visit is not null,
                            SavedIdentityVerified = visit?.SelectionIdentityVerified ?? false,
                            CandidateStillCurrent = visit?.IsCurrent ?? false,
                            Audit = audit });
                    }
                    inspections.Add(new { OriginalWorkRestored = original is not null
                        && reader.IsConversationStillActive(target.Handle, original) });
                }
            }
            reports.Add(new { target.ProcessId, Hwnd = target.Handle.ToString(), Rows = rows, Inspections = inspections });
        }
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "work-navigation-audit.json"), JsonSerializer.Serialize(new
        {
            CapturedAt = DateTimeOffset.Now, Navigates = inspectChecked,
            InputPerformed = false, EnterPerformed = false, Targets = reports
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

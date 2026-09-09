using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using GPTAutoResume.Core;

namespace GPTAutoResume.Automation;

// Read-only evidence probe: never writes chat text, opens menus, or invokes input.
public static class ProductionLimitAudit
{
    public static void Capture(string directory)
    {
        var now = DateTimeOffset.Now;
        var reader = new UiAutomationReader();
        var config = new ConfigStore().Load();
        var detector = new UsageLimitDetector(PatternCatalog.LoadDefault(), new RetryTimeParser());
        var reports = new List<object>();
        foreach (var target in new WindowScanner().FindTargets())
        {
            var text = reader.ReadVisibleText(target.Handle, out var warning);
            var result = detector.AnalyzeForAutomation(text, now, warning);
            var nodes = new List<object>();
            var root = AutomationElement.FromHandle(target.Handle);
            var queue = new Queue<(AutomationElement Element, int Depth, int Parent)>();
            queue.Enqueue((root, 0, -1));
            var timer = Stopwatch.StartNew();
            var visited = 0;
            while (queue.Count > 0 && visited < 5000 && timer.Elapsed < TimeSpan.FromSeconds(8))
            {
                var (element, depth, parent) = queue.Dequeue();
                var index = visited++;
                try
                {
                    var current = element.Current;
                    // Store only matched vocabulary/time, never the surrounding conversation.
                    var fragments = Regex.Matches(current.Name ?? "", @"你的?\s*Codex\s*和工作使用量已用完|你已達使用上限|使用量已用完|usage limit|out of (?:Codex and Work )?usage|用量重置|再試一次|wait for usage to reset|(?:清晨|凌晨|上午|下午|晚上)\s*\d{1,2}:\d{2}", RegexOptions.IgnoreCase)
                        .Select(match => match.Value).Take(8).ToArray();
                    if (fragments.Length > 0 && current.ControlType != ControlType.Edit)
                        nodes.Add(new { Index = index, Parent = parent, Depth = depth, ControlType = current.ControlType.ProgrammaticName,
                            current.AutomationId, current.ClassName, current.IsOffscreen,
                            Bounds = current.BoundingRectangle.ToString(), MatchedFragments = fragments,
                            Evidence = "REAL UIA OBSERVATION; context unverified, not an approved production fixture" });
                    if (depth >= 32 || current.ControlType == ControlType.Edit) continue;
                    var child = TreeWalker.RawViewWalker.GetFirstChild(element);
                    while (child is not null && queue.Count < 5000 && timer.Elapsed < TimeSpan.FromSeconds(8))
                    {
                        queue.Enqueue((child, depth + 1, index));
                        child = TreeWalker.RawViewWalker.GetNextSibling(child);
                    }
                }
                catch (ElementNotAvailableException) { }
            }
            var identity = reader.CaptureConversationIdentity(target.Handle);
            reports.Add(new { target.ProcessId, Hwnd = target.Handle.ToString(), target.ProcessName,
                WorkIdentityHash = identity is null ? null : JsonWorkSelectionStore.BuildHash(identity),
                Permission = new JsonWorkSelectionStore().IsAutoResumeEnabled(identity),
                ProductionReaderCharacters = text.Length, ProductionResult = result, Visited = visited,
                Truncated = queue.Count > 0, Nodes = nodes });
        }
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"real-limit-{now:yyyyMMdd-HHmmss}.json"),
            JsonSerializer.Serialize(new { CapturedAt = now, ReadOnly = true,
                SavedConfigNotMemoryInspection = new { config.DryRun, config.SendEnter, config.AllowRealSubmit, config.ResumePolicy },
                Targets = reports }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

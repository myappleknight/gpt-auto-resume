using System.IO;
using System.Text.Json;
using GPTAutoResume.Core;

namespace GPTAutoResume.Automation;

public static class ActiveWorkProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Run(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "diagnostics"));
        var path = Path.Combine(root, "diagnostics", "active-work-probe.json");
        var scanner = new WindowScanner();
        var reader = new UiAutomationReader();
        var target = scanner.FindTargets().FirstOrDefault();
        if (target is null)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                CapturedAt = DateTimeOffset.Now,
                TargetFound = false
            }, JsonOptions));
            return path;
        }

        var input = reader.FindChatInputForDiscovery(target.Handle);
        var identity = reader.CaptureConversationIdentity(target.Handle);
        var displayName = reader.GetActiveWorkDisplayName(target.Handle);
        var payload = new
        {
            CapturedAt = DateTimeOffset.Now,
            TargetFound = true,
            target.ProcessName,
            target.ProcessId,
            Hwnd = target.Handle.ToInt64(),
            target.Title,
            ActiveWorkDisplayName = string.IsNullOrWhiteSpace(displayName) ? "__unnamed_work__" : displayName,
            IdentityHash = identity is null ? "" : JsonWorkSelectionStore.BuildHash(identity),
            IdentityUsable = identity?.HasUsableSignal ?? false,
            Input = reader.DescribeElement(input)
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
        return path;
    }
}

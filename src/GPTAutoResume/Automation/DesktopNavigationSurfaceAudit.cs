using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using GPTAutoResume.Core;
using Microsoft.Win32;

namespace GPTAutoResume.Automation;

public static class DesktopNavigationSurfaceAudit
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var scanner = new WindowScanner();
        var reader = new UiAutomationReader();
        var targets = scanner.FindTargets().Select(target =>
        {
            var identity = reader.CaptureConversationIdentity(target.Handle);
            var rows = SafeRows(target.Handle).Take(12).ToArray();
            return new
            {
                target.ProcessId,
                Hwnd = target.Handle.ToString(),
                target.Title,
                CurrentIdentity = identity is null ? null : new
                {
                    identity.SurfaceType,
                    identity.ConversationTitleHash,
                    identity.SelectedItemIdentity,
                    identity.ContainerAutomationId,
                    identity.ContainerNameHash,
                    identity.ComposerIdentity,
                    StableHash = JsonWorkSelectionStore.BuildHash(identity)
                },
                NavigationSurfaces = SafeNavigationSurfaces(target.Handle),
                SidebarRows = rows.Select(row => new
                {
                    row.Title,
                    row.TitleHash,
                    row.ControlType,
                    row.FrameworkId,
                    row.AutomationId,
                    row.HelpTextHash,
                    row.ItemStatusHash,
                    row.ItemTypeHash,
                    row.IsOffscreen,
                    row.BoundingRectangle,
                    row.RuntimeId,
                    row.Patterns
                }).ToArray()
            };
        }).ToArray();

        File.WriteAllText(Path.Combine(directory, "desktop-navigation-surface-audit.json"),
            JsonSerializer.Serialize(new
            {
                CapturedAt = DateTimeOffset.Now,
                ReadOnly = true,
                ConversationBodyRead = false,
                CookiesOrTokensRead = false,
                PrivateEndpointsUsed = false,
                TitleUsedAsIdentity = false,
                RuntimeIdPersistedAsIdentity = false,
                RegistryProtocols = ProtocolRegistrationAudit.FindCandidates(),
                DesktopTargets = targets,
                Findings = new
                {
                    RegisteredDeepLinkScheme = "SEE RegistryProtocols",
                    ConversationSpecificDeepLink = "NOT FOUND",
                    CurrentStableIdentitySurface = targets.Any(target => target.CurrentIdentity is not null
                        && !string.IsNullOrWhiteSpace(target.CurrentIdentity.SelectedItemIdentity)) ? "PARTIAL" : "NOT FOUND",
                    SafeNonActiveWorkNavigation = "FAIL",
                    Reason = "No conversation-specific deep link or stable current conversation id was found in allowed local surfaces."
                }
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IReadOnlyList<SidebarRowSurface> SafeRows(nint hwnd)
    {
        try
        {
            return UiAutomationWorkNavigator.Rows(hwnd)
                .Select(row => new SidebarRowSurface(
                    row.Current.Name,
                    ConversationTargetIdentity.Hash(row.Current.Name),
                    row.Current.ControlType.ProgrammaticName,
                    SafeString(row, AutomationElement.FrameworkIdProperty),
                    row.Current.AutomationId,
                    ConversationTargetIdentity.Hash(SafeString(row, AutomationElement.HelpTextProperty)),
                    ConversationTargetIdentity.Hash(SafeString(row, AutomationElement.ItemStatusProperty)),
                    ConversationTargetIdentity.Hash(SafeString(row, AutomationElement.ItemTypeProperty)),
                    row.Current.IsOffscreen,
                    RectText(row.Current.BoundingRectangle),
                    UiAutomationWorkNavigator.RowId(row),
                    row.GetSupportedPatterns().Select(pattern => pattern.ProgrammaticName).ToArray()))
                .ToArray();
        }
        catch { return []; }
    }

    private static IReadOnlyList<object> SafeNavigationSurfaces(nint hwnd)
    {
        try
        {
            var results = new List<object>();
            var root = AutomationElement.FromHandle(hwnd);
            var walker = TreeWalker.RawViewWalker;
            var queue = new Queue<(AutomationElement Element, int Depth)>();
            queue.Enqueue((root, 0));
            var count = 0;
            while (queue.Count > 0 && count++ < 900)
            {
                var (element, depth) = queue.Dequeue();
                if (depth > 8) continue;
                var type = element.Current.ControlType;
                if (type == ControlType.Window || type == ControlType.Pane || type == ControlType.Document
                    || type == ControlType.Tab || type == ControlType.TabItem || type == ControlType.Custom)
                {
                    results.Add(new
                    {
                        Depth = depth,
                        ControlType = type.ProgrammaticName,
                        NameHash = ConversationTargetIdentity.Hash(element.Current.Name),
                        element.Current.AutomationId,
                        element.Current.ClassName,
                        FrameworkId = SafeString(element, AutomationElement.FrameworkIdProperty),
                        HelpTextHash = ConversationTargetIdentity.Hash(SafeString(element, AutomationElement.HelpTextProperty)),
                        ItemStatusHash = ConversationTargetIdentity.Hash(SafeString(element, AutomationElement.ItemStatusProperty)),
                        IsOffscreen = element.Current.IsOffscreen,
                        BoundingRectangle = RectText(element.Current.BoundingRectangle),
                        Patterns = element.GetSupportedPatterns().Select(pattern => pattern.ProgrammaticName).ToArray()
                    });
                }

                for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
                    queue.Enqueue((child, depth + 1));
            }
            return results;
        }
        catch { return []; }
    }

    private static string SafeString(AutomationElement element, AutomationProperty property)
    {
        try
        {
            return element.GetCurrentPropertyValue(property, true) as string ?? "";
        }
        catch { return ""; }
    }

    private static string RectText(Rect rect) => $"{rect.Left:0},{rect.Top:0},{rect.Width:0},{rect.Height:0}";

    private sealed record SidebarRowSurface(
        string Title,
        string TitleHash,
        string ControlType,
        string FrameworkId,
        string AutomationId,
        string HelpTextHash,
        string ItemStatusHash,
        string ItemTypeHash,
        bool IsOffscreen,
        string BoundingRectangle,
        string RuntimeId,
        IReadOnlyList<string> Patterns);
}

public static class ProtocolRegistrationAudit
{
    private static readonly string[] Needles = ["chatgpt", "openai", "codex"];

    public static IReadOnlyList<ProtocolRegistration> FindCandidates()
    {
        var results = new List<ProtocolRegistration>();
        foreach (var name in Registry.ClassesRoot.GetSubKeyNames())
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80) continue;
            using var key = Registry.ClassesRoot.OpenSubKey(name);
            if (key is null || key.GetValue("URL Protocol") is null) continue;
            var description = key.GetValue("") as string ?? "";
            var command = key.OpenSubKey(@"shell\open\command")?.GetValue("") as string ?? "";
            if (!Matches(name) && !Matches(description) && !Matches(command)) continue;
            results.Add(new ProtocolRegistration(
                name,
                ConversationTargetIdentity.Hash(description),
                CommandKind(command),
                Matches(command) ? ConversationTargetIdentity.Hash(command) : ""));
        }
        return results.OrderBy(result => result.Scheme, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool LooksRelevant(string scheme, string description, string command) =>
        Matches(scheme) || Matches(description) || Matches(command);

    private static bool Matches(string value) =>
        Needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string CommandKind(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "";
        if (command.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) return "packaged-app";
        if (command.Contains(".exe", StringComparison.OrdinalIgnoreCase)) return "executable";
        return "registered-command";
    }
}

public sealed record ProtocolRegistration(
    string Scheme,
    string DescriptionHash,
    string CommandKind,
    string CommandHash);

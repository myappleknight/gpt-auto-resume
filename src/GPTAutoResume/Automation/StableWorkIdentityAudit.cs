using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using GPTAutoResume.Core;

namespace GPTAutoResume.Automation;

public static class StableWorkIdentityAudit
{
    public static void Run(string directory)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        Directory.CreateDirectory(directory);

        var reader = new UiAutomationReader();
        var targetRows = new List<IReadOnlyList<SidebarRowReport>>();
        var targets = new WindowScanner().FindTargets().Select(target =>
        {
            var activeIdentity = reader.CaptureConversationIdentity(target.Handle);
            var rows = SafeRows(target.Handle, timeout.Token);
            targetRows.Add(rows);
            return new
            {
                target.ProcessId,
                Hwnd = target.Handle.ToString(),
                target.Title,
                ActiveTitleHash = activeIdentity?.ConversationTitleHash,
                ActiveSelectionEnabled = new JsonWorkSelectionStore().IsAutoResumeEnabled(activeIdentity),
                SidebarRows = rows.Select(row => new
                {
                    Title = row.Title,
                    TitleHash = ConversationTargetIdentity.Hash(row.Title),
                    row.ControlType,
                    row.ClassName,
                    row.FrameworkId,
                    row.AutomationId,
                    row.HelpText,
                    row.ItemStatus,
                    row.ItemType,
                    row.AccessKey,
                    row.AcceleratorKey,
                    row.IsOffscreen,
                    row.BoundingRectangle,
                    row.RuntimeId,
                    row.Patterns
                }).ToArray()
            };
        }).ToArray();

        var selections = new JsonWorkSelectionStore().Load();
        var searchTerms = selections.Select(work => SearchToken(work.DisplayTitle))
            .Concat(targetRows.SelectMany(row => row).Select(row => SearchToken(row.Title)))
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Where(title => title.Any(char.IsAsciiLetterOrDigit))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        var provider = new CodexAppServerThreadProvider();
        var appServer = provider.ReadThreads(searchTerms, timeout.Token);
        var mappings = BuildMappings(selections, targetRows.SelectMany(row => row).ToArray(), appServer.Threads);

        var stableIdAvailable = mappings.Any(mapping => mapping.AppServerMatches.Count == 1
            && !string.IsNullOrWhiteSpace(mapping.AppServerMatches[0].Id));
        var canMapAllChecked = selections.Where(work => work.AutoResumeEnabled && !work.IsStale)
            .All(work => mappings.Any(mapping => mapping.ConversationIdentityHash == work.ConversationIdentityHash
                && mapping.AppServerMatches.Count == 1));

        File.WriteAllText(Path.Combine(directory, "stable-work-identity-audit.json"), JsonSerializer.Serialize(new
        {
            CapturedAt = DateTimeOffset.Now,
            ReadOnly = true,
            ConversationBodyRead = false,
            CookiesOrTokensRead = false,
            TitleUsedAsIdentity = false,
            OfficialSurface = "Codex App Server thread/list + thread/loaded/list metadata",
            AppServerThreadListReadable = appServer.ThreadListReadable,
            AppServerLoadedListReadable = appServer.LoadedListReadable,
            AppServerError = appServer.Error,
            AppServerSearchTerms = searchTerms,
            AppServerThreadCount = appServer.Threads.Count,
            LoadedThreadIds = appServer.LoadedThreadIds,
            DesktopTargetCount = targets.Length,
            CanEnumerateWorksOrThreads = appServer.ThreadListReadable && appServer.Threads.Count > 0 ? "YES" : "NO",
            StableImmutableIdAvailable = stableIdAvailable ? "PARTIAL" : "NO",
            IdCanMapBackToDesktopWork = canMapAllChecked ? "YES" : stableIdAvailable ? "PARTIAL" : "NO",
            CanActivateOrOpenWorkById = "NOT TESTED - read-only audit",
            RequiresForegroundUiInteraction = "NO for this audit",
            Targets = targets,
            AppServerThreads = appServer.Threads.Select(thread => new
            {
                thread.Id,
                thread.SessionId,
                thread.Name,
                NameHash = ConversationTargetIdentity.Hash(thread.Name),
                PreviewHash = ConversationTargetIdentity.Hash(thread.Preview),
                thread.SourceKind,
                thread.StatusType,
                thread.CreatedAt,
                thread.UpdatedAt
            }).ToArray(),
            SelectionMappings = mappings
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IReadOnlyList<SidebarRowReport> SafeRows(nint hwnd, CancellationToken cancellationToken)
    {
        try
        {
            return UiAutomationWorkNavigator.Rows(hwnd, cancellationToken)
                .Select(row => new SidebarRowReport(
                    row.Current.Name,
                    SafeCurrent(row, AutomationElement.ControlTypeProperty)?.ToString() ?? "",
                    SafeCurrent(row, AutomationElement.ClassNameProperty) as string ?? "",
                    SafeCurrent(row, AutomationElement.FrameworkIdProperty) as string ?? "",
                    row.Current.AutomationId,
                    SafeCurrent(row, AutomationElement.HelpTextProperty) as string ?? "",
                    SafeCurrent(row, AutomationElement.ItemStatusProperty) as string ?? "",
                    SafeCurrent(row, AutomationElement.ItemTypeProperty) as string ?? "",
                    SafeCurrent(row, AutomationElement.AccessKeyProperty) as string ?? "",
                    SafeCurrent(row, AutomationElement.AcceleratorKeyProperty) as string ?? "",
                    row.Current.IsOffscreen,
                    RectText(row.Current.BoundingRectangle),
                    UiAutomationWorkNavigator.RowId(row),
                    row.GetSupportedPatterns().Select(pattern => pattern.ProgrammaticName).ToArray()))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<SelectionMappingReport> BuildMappings(
        IReadOnlyList<WorkSelectionRecord> selections,
        IReadOnlyList<SidebarRowReport> rows,
        IReadOnlyList<CodexThreadRecord> threads)
    {
        return selections.Select(work =>
        {
            var rowMatches = rows.Where(row => string.Equals(row.Title.Trim(), work.DisplayTitle.Trim(), StringComparison.Ordinal))
                .Select(row => new SidebarMatchReport(row.Title, ConversationTargetIdentity.Hash(row.Title),
                    row.IsOffscreen, row.RuntimeId))
                .ToArray();

            var appMatches = threads.Where(thread => !string.IsNullOrWhiteSpace(thread.Name)
                    && string.Equals(thread.Name.Trim(), work.DisplayTitle.Trim(), StringComparison.Ordinal))
                .Select(thread => new ThreadMatchReport(thread.Id, thread.SessionId, thread.Name,
                    ConversationTargetIdentity.Hash(thread.Name), thread.SourceKind, thread.StatusType))
                .ToArray();

            return new SelectionMappingReport(
                work.ConversationIdentityHash,
                work.DisplayTitle,
                work.AutoResumeEnabled,
                work.IsStale,
                rowMatches,
                appMatches,
                rowMatches.Length == 1,
                appMatches.Length == 1,
                appMatches.Length == 1 && rowMatches.Length == 1 ? "CANDIDATE_ONLY_NEEDS_ACTIVE_ID_REVALIDATION" :
                appMatches.Length == 0 ? "NO_APP_SERVER_THREAD_MATCH" :
                appMatches.Length > 1 ? "AMBIGUOUS_APP_SERVER_TITLE_MATCH" :
                rowMatches.Length == 0 ? "NO_DESKTOP_ROW_MATCH" :
                "AMBIGUOUS_DESKTOP_ROW_MATCH");
        }).ToArray();
    }

    private static string SearchToken(string value)
    {
        var trimmed = value.Trim();
        var asciiToken = Regex.Matches(trimmed, "[A-Za-z0-9][A-Za-z0-9_-]{3,23}")
            .Select(match => match.Value)
            .FirstOrDefault(token => token.Contains("unnamed", StringComparison.OrdinalIgnoreCase) == false);
        if (!string.IsNullOrWhiteSpace(asciiToken)) return asciiToken;
        return "";
    }

    private sealed record SidebarRowReport(
        string Title,
        string ControlType,
        string ClassName,
        string FrameworkId,
        string AutomationId,
        string HelpText,
        string ItemStatus,
        string ItemType,
        string AccessKey,
        string AcceleratorKey,
        bool IsOffscreen,
        string BoundingRectangle,
        string RuntimeId,
        IReadOnlyList<string> Patterns);

    private sealed record SidebarMatchReport(
        string Title,
        string TitleHash,
        bool IsOffscreen,
        string RuntimeId);

    private sealed record ThreadMatchReport(
        string Id,
        string? SessionId,
        string? Name,
        string NameHash,
        string? SourceKind,
        string? StatusType);

    private sealed record SelectionMappingReport(
        string ConversationIdentityHash,
        string DisplayTitle,
        bool AutoResumeEnabled,
        bool IsStale,
        IReadOnlyList<SidebarMatchReport> DesktopRowMatches,
        IReadOnlyList<ThreadMatchReport> AppServerMatches,
        bool UniqueDesktopTitleCandidate,
        bool UniqueAppServerTitleCandidate,
        string MappingStatus);

    private static object? SafeCurrent(AutomationElement element, AutomationProperty property)
    {
        try { return element.GetCurrentPropertyValue(property, true); }
        catch { return null; }
    }

    private static string RectText(Rect rect) => $"{rect.Left:0},{rect.Top:0},{rect.Width:0},{rect.Height:0}";
}

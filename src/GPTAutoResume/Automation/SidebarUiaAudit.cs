using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace GPTAutoResume.Automation;

public static class SidebarUiaAudit
{
    private static readonly string[] ProbeTitles =
    [
        "Example Work A",
        "Example Work B",
        "GPT Auto Resume"
    ];

    public static string Run(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        Directory.CreateDirectory(Path.Combine(root, "diagnostics"));

        var scanner = new WindowScanner();
        var targets = scanner.FindTargets();
        var target = targets.FirstOrDefault();
        var reportPath = Path.Combine(root, "docs", "sidebar-uia-audit.md");
        var rawPath = Path.Combine(root, "diagnostics", "sidebar-uia-tree.txt");

        if (target is null)
        {
            File.WriteAllText(reportPath, BuildNoWindowReport());
            return reportPath;
        }

        var rootElement = AutomationElement.FromHandle(target.Handle);
        if (rootElement is null)
        {
            File.WriteAllText(reportPath, BuildNoRootReport(target));
            return reportPath;
        }

        var rootBounds = SafeBounds(rootElement);
        var elements = new List<SidebarElementSnapshot>();
        var visited = 0;
        Walk(rootElement, null, 0, maxDepth: 48, maxElements: 12_000, rootBounds, elements, ref visited);

        var sidebarElements = elements
            .Where(element => element.IsSidebarCandidate)
            .ToList();
        var namedSidebarElements = sidebarElements
            .Where(element => !string.IsNullOrWhiteSpace(element.Name))
            .ToList();
        var titleProbeResults = ProbeTitles
            .Select(title => new TitleProbeResult(title, elements.Where(element => ContainsTitle(element.Name, title)).ToList()))
            .ToList();
        var selected = sidebarElements.Where(element => element.HasSelectionItemPattern && element.IsSelected == true).ToList();
        var expandable = sidebarElements
            .Where(element => element.HasExpandCollapsePattern)
            .Where(element => IsLikelySidebarDataRow(element.Name) || element.Name.StartsWith("專案：", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var invokeRows = sidebarElements.Where(element => element.HasInvokePattern).ToList();

        var probableProjectRows = namedSidebarElements
            .Where(element => element.Name.StartsWith("專案：", StringComparison.OrdinalIgnoreCase)
                || element.Name.StartsWith("Project:", StringComparison.OrdinalIgnoreCase)
                || element.ControlType.Contains("TreeItem", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(element => element.Identity)
            .Take(20)
            .ToList();
        var probableConversationRows = namedSidebarElements
            .Where(element => !element.HasExpandCollapsePattern)
            .Where(element => element.HasSelectionItemPattern
                || element.HasInvokePattern
                || element.ControlType.Contains("ListItem", StringComparison.OrdinalIgnoreCase)
                || element.ControlType.Contains("Button", StringComparison.OrdinalIgnoreCase)
                || element.ControlType.Contains("Text", StringComparison.OrdinalIgnoreCase))
            .Where(element => IsLikelyConversationTitle(element.Name))
            .DistinctBy(element => element.Identity)
            .Take(40)
            .ToList();

        File.WriteAllLines(rawPath, sidebarElements.Select(element => element.ToDiagnosticLine()));
        File.WriteAllText(reportPath, BuildReport(
            target,
            rootBounds,
            visited,
            sidebarElements,
            namedSidebarElements,
            probableProjectRows,
            probableConversationRows,
            expandable,
            selected,
            invokeRows,
            titleProbeResults,
            rawPath));
        return reportPath;
    }

    private static void Walk(
        AutomationElement element,
        SidebarElementSnapshot? parent,
        int depth,
        int maxDepth,
        int maxElements,
        Rect rootBounds,
        List<SidebarElementSnapshot> output,
        ref int visited)
    {
        if (depth > maxDepth || visited >= maxElements)
        {
            return;
        }

        visited++;
        SidebarElementSnapshot? snapshot = null;
        try
        {
            snapshot = ToSnapshot(element, parent, depth, rootBounds);
            output.Add(snapshot);
        }
        catch (ElementNotAvailableException)
        {
            return;
        }

        var walker = TreeWalker.RawViewWalker;
        for (var child = walker.GetFirstChild(element); child is not null && visited < maxElements; child = walker.GetNextSibling(child))
        {
            Walk(child, snapshot, depth + 1, maxDepth, maxElements, rootBounds, output, ref visited);
        }
    }

    private static SidebarElementSnapshot ToSnapshot(AutomationElement element, SidebarElementSnapshot? parent, int depth, Rect rootBounds)
    {
        var bounds = SafeBounds(element);
        var controlType = element.Current.ControlType.ProgrammaticName;
        var name = SafeName(element.Current.Name);
        var automationId = element.Current.AutomationId ?? "";
        var className = element.Current.ClassName ?? "";
        var isSidebar = IsSidebarCandidate(bounds, rootBounds, controlType, name);
        var hasSelection = element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionRaw);
        var isSelected = hasSelection && selectionRaw is SelectionItemPattern selection ? selection.Current.IsSelected : (bool?)null;
        var hasInvoke = element.TryGetCurrentPattern(InvokePattern.Pattern, out _);
        var hasExpand = element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _);

        return new SidebarElementSnapshot(
            Depth: depth,
            ControlType: controlType,
            Name: name,
            AutomationId: automationId,
            ClassName: className,
            IsOffscreen: element.Current.IsOffscreen,
            IsEnabled: element.Current.IsEnabled,
            IsKeyboardFocusable: element.Current.IsKeyboardFocusable,
            Bounds: bounds,
            ParentControlType: parent?.ControlType ?? "",
            ParentName: parent?.Name ?? "",
            HasSelectionItemPattern: hasSelection,
            IsSelected: isSelected,
            HasInvokePattern: hasInvoke,
            HasExpandCollapsePattern: hasExpand,
            IsSidebarCandidate: isSidebar);
    }

    private static bool IsSidebarCandidate(Rect bounds, Rect rootBounds, string controlType, string name)
    {
        if (bounds.IsEmpty || rootBounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        var relativeLeft = bounds.Left - rootBounds.Left;
        var relativeRight = bounds.Right - rootBounds.Left;
        var sidebarMaxLeft = Math.Min(260, rootBounds.Width * 0.35);
        var sidebarMaxRight = Math.Min(360, rootBounds.Width * 0.45);
        var leftBand = relativeLeft >= -8 && relativeLeft <= sidebarMaxLeft;
        var notHeaderOnly = bounds.Top > rootBounds.Top + 32;
        var reasonableHeight = bounds.Height is >= 8 and <= 140;
        var likelyInteractiveOrText = controlType.Contains("Text", StringComparison.OrdinalIgnoreCase)
            || controlType.Contains("Button", StringComparison.OrdinalIgnoreCase)
            || controlType.Contains("ListItem", StringComparison.OrdinalIgnoreCase)
            || controlType.Contains("TreeItem", StringComparison.OrdinalIgnoreCase)
            || controlType.Contains("MenuItem", StringComparison.OrdinalIgnoreCase)
            || controlType.Contains("Hyperlink", StringComparison.OrdinalIgnoreCase)
            || controlType.Contains("Group", StringComparison.OrdinalIgnoreCase)
            || controlType.Contains("Pane", StringComparison.OrdinalIgnoreCase);

        return leftBand
            && relativeRight <= sidebarMaxRight
            && notHeaderOnly
            && reasonableHeight
            && likelyInteractiveOrText
            && (!string.IsNullOrWhiteSpace(name) || !controlType.Contains("Text", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLikelyConversationTitle(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        if (trimmed.Length is < 3 or > 80)
        {
            return false;
        }

        return IsLikelySidebarDataRow(trimmed);
    }

    private static bool IsLikelySidebarDataRow(string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("<redacted", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var excludedExact = new[]
        {
            "Search",
            "搜尋",
            "New chat",
            "新對話",
            "新增",
            "Settings",
            "設定",
            "More",
            "更多",
            "查看活動",
            "已排程",
            "外掛程式",
            "開啟個人檔案選單",
            "開啟說明選單",
            "對話動作",
            "ChatGPT",
            "Codex"
        };

        if (excludedExact.Any(value => string.Equals(trimmed, value, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var excludedPrefixes = new[]
        {
            "切換模式",
            "Open profile",
            "Open help",
            "Conversation actions"
        };

        return !excludedPrefixes.Any(value => trimmed.StartsWith(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildReport(
        TargetWindow target,
        Rect rootBounds,
        int visited,
        IReadOnlyList<SidebarElementSnapshot> sidebarElements,
        IReadOnlyList<SidebarElementSnapshot> namedSidebarElements,
        IReadOnlyList<SidebarElementSnapshot> probableProjectRows,
        IReadOnlyList<SidebarElementSnapshot> probableConversationRows,
        IReadOnlyList<SidebarElementSnapshot> expandable,
        IReadOnlyList<SidebarElementSnapshot> selected,
        IReadOnlyList<SidebarElementSnapshot> invokeRows,
        IReadOnlyList<TitleProbeResult> titleProbeResults,
        string rawPath)
    {
        var exposedTitles = titleProbeResults.Count(result => result.Matches.Count > 0);
        var builder = new StringBuilder();
        builder.AppendLine("# Sidebar UIA Audit");
        builder.AppendLine();
        builder.AppendLine($"Date: {DateTimeOffset.Now:O}");
        builder.AppendLine("Scope: ChatGPT Desktop sidebar enumeration evidence. This diagnostic does not click sidebar items, send keyboard input, or save conversation body text.");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine($"Sidebar root found: {(sidebarElements.Count > 0 ? "YES" : "NO")}");
        builder.AppendLine($"Visible sidebar titles tested: {titleProbeResults.Count}");
        builder.AppendLine($"Titles exposed through UIA: {exposedTitles}");
        builder.AppendLine($"Projects discovered: {probableProjectRows.Count}");
        builder.AppendLine($"Conversations discovered: {probableConversationRows.Count}");
        builder.AppendLine($"ExpandCollapse available: {(expandable.Count > 0 ? "YES" : "NO")}");
        builder.AppendLine($"Selected conversation identifiable: {(selected.Count > 0 ? "PARTIAL" : "NO")}");
        builder.AppendLine($"Sidebar -> Active conversation binding: {(selected.Count > 0 ? "PARTIAL" : "NO")}");
        builder.AppendLine($"Recent 5 enumeration: {(probableConversationRows.Count >= 5 ? "PARTIAL" : "NO")}");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Target Window");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine($"Process: {target.ProcessName}");
        builder.AppendLine($"PID: {target.ProcessId}");
        builder.AppendLine($"HWND: {target.Handle}");
        builder.AppendLine($"Title: {target.Title}");
        builder.AppendLine($"Root bounds: {FormatBounds(rootBounds)}");
        builder.AppendLine($"Visited UIA elements: {visited}");
        builder.AppendLine($"Sidebar candidate elements: {sidebarElements.Count}");
        builder.AppendLine($"Named sidebar elements: {namedSidebarElements.Count}");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Visible Title Probes");
        builder.AppendLine();
        foreach (var probe in titleProbeResults)
        {
            builder.AppendLine($"### {probe.Title}");
            builder.AppendLine();
            builder.AppendLine(probe.Matches.Count == 0 ? "UIA: NOT FOUND" : "UIA: FOUND");
            builder.AppendLine();
            foreach (var match in probe.Matches.Take(5))
            {
                builder.AppendLine("```text");
                builder.AppendLine(match.ToDiagnosticLine());
                builder.AppendLine("```");
                builder.AppendLine();
            }
        }

        builder.AppendLine("## Probable Projects");
        builder.AppendLine();
        AppendElements(builder, probableProjectRows);
        builder.AppendLine("## Probable Conversations");
        builder.AppendLine();
        AppendElements(builder, probableConversationRows.Take(20).ToList());
        builder.AppendLine("## Selected Sidebar Items");
        builder.AppendLine();
        AppendElements(builder, selected.Take(10).ToList());
        builder.AppendLine("## Raw Sanitized Dump");
        builder.AppendLine();
        builder.AppendLine($"Raw sidebar candidate dump: `{Path.GetFullPath(rawPath)}`");
        builder.AppendLine();
        builder.AppendLine("The raw dump contains only sidebar-candidate UIA metadata and short UI element names. It does not include active conversation body text.");
        return builder.ToString();
    }

    private static void AppendElements(StringBuilder builder, IReadOnlyList<SidebarElementSnapshot> elements)
    {
        if (elements.Count == 0)
        {
            builder.AppendLine("_None found._");
            builder.AppendLine();
            return;
        }

        foreach (var element in elements)
        {
            builder.AppendLine("```text");
            builder.AppendLine(element.ToDiagnosticLine());
            builder.AppendLine("```");
            builder.AppendLine();
        }
    }

    private static string BuildNoWindowReport() =>
        """
        # Sidebar UIA Audit

        ```text
        Sidebar root found: NO
        Visible sidebar titles tested: 0
        Titles exposed through UIA: 0
        Projects discovered: 0
        Conversations discovered: 0
        ExpandCollapse available: NO
        Selected conversation identifiable: NO
        Sidebar -> Active conversation binding: NO
        Recent 5 enumeration: NO
        ```

        No ChatGPT/Codex window was found.
        """;

    private static string BuildNoRootReport(TargetWindow target) =>
        $"""
        # Sidebar UIA Audit

        ```text
        Sidebar root found: NO
        Process: {target.ProcessName}
        PID: {target.ProcessId}
        HWND: {target.Handle}
        Title: {target.Title}
        ```

        The target window did not expose a UI Automation root element.
        """;

    private static bool ContainsTitle(string value, string title) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(title, StringComparison.OrdinalIgnoreCase);

    private static string SafeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        foreach (var probe in ProbeTitles)
        {
            if (trimmed.Contains(probe, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Length <= 90 ? trimmed : probe;
            }
        }

        return trimmed.Length <= 60 ? trimmed : $"<redacted length={trimmed.Length}>";
    }

    private static Rect SafeBounds(AutomationElement element)
    {
        try
        {
            return element.Current.BoundingRectangle;
        }
        catch (ElementNotAvailableException)
        {
            return Rect.Empty;
        }
    }

    private static string FormatBounds(Rect bounds) =>
        bounds.IsEmpty
            ? "<empty>"
            : $"X={bounds.X:0}; Y={bounds.Y:0}; W={bounds.Width:0}; H={bounds.Height:0}";

    private sealed record TitleProbeResult(string Title, IReadOnlyList<SidebarElementSnapshot> Matches);

    private sealed record SidebarElementSnapshot(
        int Depth,
        string ControlType,
        string Name,
        string AutomationId,
        string ClassName,
        bool IsOffscreen,
        bool IsEnabled,
        bool IsKeyboardFocusable,
        Rect Bounds,
        string ParentControlType,
        string ParentName,
        bool HasSelectionItemPattern,
        bool? IsSelected,
        bool HasInvokePattern,
        bool HasExpandCollapsePattern,
        bool IsSidebarCandidate)
    {
        public string Identity => string.Join("|", Depth, ControlType, Name, AutomationId, ClassName, FormatBounds(Bounds));

        public string ToDiagnosticLine() =>
            $"Depth={Depth}; ControlType={ControlType}; Name={NameOrEmpty(Name)}; AutomationId={NameOrEmpty(AutomationId)}; ClassName={NameOrEmpty(ClassName)}; IsOffscreen={IsOffscreen}; IsEnabled={IsEnabled}; IsKeyboardFocusable={IsKeyboardFocusable}; Bounds={FormatBounds(Bounds)}; Parent={NameOrEmpty(ParentControlType)} {NameOrEmpty(ParentName)}; SelectionItemPattern={HasSelectionItemPattern}; IsSelected={IsSelected?.ToString() ?? "n/a"}; InvokePattern={HasInvokePattern}; ExpandCollapsePattern={HasExpandCollapsePattern}";

        private static string NameOrEmpty(string value) => string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
    }
}

using System.Windows.Automation;

namespace GPTAutoResume.Automation;

internal sealed class AccountQuotaExpansionScope : IDisposable
{
    private readonly ExpandCollapsePattern? _pattern;
    private readonly InvokePattern? _invokePattern;
    private readonly bool _collapseOnDispose;
    private bool _disposed;

    public AccountQuotaExpansionScope(
        bool found,
        bool expanded,
        bool openedByProbe,
        string detail,
        ExpandCollapsePattern? pattern,
        InvokePattern? invokePattern = null)
    {
        Found = found;
        Expanded = expanded;
        OpenedByProbe = openedByProbe;
        Detail = detail;
        _pattern = pattern;
        _invokePattern = invokePattern;
        _collapseOnDispose = openedByProbe;
    }

    public bool Found { get; }

    public bool Expanded { get; }

    public bool OpenedByProbe { get; }

    public string Detail { get; }

    public bool RestoreAttempted { get; private set; }

    public bool Restored { get; private set; } = true;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_collapseOnDispose)
        {
            return;
        }

        RestoreAttempted = true;
        try
        {
            if (_pattern is not null)
            {
                _pattern.Collapse();
            }
            else if (_invokePattern is not null)
            {
                _invokePattern.Invoke();
            }
            else
            {
                Restored = false;
                return;
            }

            AccountQuotaUiaExpander.WaitBriefly(CancellationToken.None, 250);
            Restored = _pattern is null
                || AccountQuotaUiaExpander.SafeExpandCollapseState(_pattern) == ExpandCollapseState.Collapsed;
        }
        catch
        {
            Restored = false;
        }
    }
}

internal static class AccountQuotaUiaExpander
{
    public static AccountQuotaExpansionScope TryExpandQuotaSection(AutomationElement root, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = EnumerateBounded(root, maxDepth: 18, maxElements: 2_500)
                .Where(IsQuotaSectionCandidate)
                .Select(ToCandidate)
                .Where(candidate => candidate.Pattern is not null || candidate.InvokePattern is not null)
                .OrderBy(candidate => candidate.State == ExpandCollapseState.Collapsed ? 0 : 1)
                .ThenBy(candidate => candidate.Pattern is not null ? 0 : 1)
                .ThenBy(candidate => candidate.BoundsTop)
                .FirstOrDefault();

            if (candidate is null)
            {
                return new AccountQuotaExpansionScope(false, false, false, "Quota section expander: NOT FOUND", null);
            }

            var pattern = candidate.Pattern;
            if (candidate.Element is null || (pattern is null && candidate.InvokePattern is null))
            {
                return new AccountQuotaExpansionScope(false, false, false, "Quota section expander: NOT FOUND", null);
            }

            if (candidate.State == ExpandCollapseState.Expanded)
            {
                return new AccountQuotaExpansionScope(true, true, false, "Quota section expander: ALREADY EXPANDED", pattern);
            }

            if (pattern is null)
            {
                candidate.InvokePattern!.Invoke();
                WaitBriefly(cancellationToken, 700);
                return new AccountQuotaExpansionScope(true, true, true, "Quota section menu item: INVOKED", null, candidate.InvokePattern);
            }

            if (candidate.State != ExpandCollapseState.Collapsed)
            {
                if (candidate.InvokePattern is null)
                {
                    return new AccountQuotaExpansionScope(true, false, false, $"Quota section expander: NOT EXPANDABLE ({candidate.State})", pattern);
                }

                candidate.InvokePattern.Invoke();
                WaitBriefly(cancellationToken, 700);
                return new AccountQuotaExpansionScope(true, true, true, "Quota section menu item: INVOKED", null, candidate.InvokePattern);
            }

            pattern.Expand();
            WaitBriefly(cancellationToken, 700);
            return new AccountQuotaExpansionScope(true, true, true, "Quota section expander: EXPANDED", pattern);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new AccountQuotaExpansionScope(false, false, false, "Quota section expander: FAILED", null);
        }
    }

    internal static ExpandCollapseState SafeExpandCollapseState(ExpandCollapsePattern pattern)
    {
        try
        {
            return pattern.Current.ExpandCollapseState;
        }
        catch
        {
            return ExpandCollapseState.LeafNode;
        }
    }

    internal static void WaitBriefly(CancellationToken cancellationToken, int milliseconds)
    {
        if (cancellationToken.WaitHandle.WaitOne(milliseconds))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static QuotaSectionCandidate ToCandidate(AutomationElement element)
    {
        var pattern = element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var raw)
            && raw is ExpandCollapsePattern expandCollapse
                ? expandCollapse
                : null;
        var invokePattern = element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeRaw)
            && invokeRaw is InvokePattern invoke
                ? invoke
                : null;

        var state = pattern is null ? ExpandCollapseState.LeafNode : SafeExpandCollapseState(pattern);
        var bounds = element.Current.BoundingRectangle;
        return new QuotaSectionCandidate(element, pattern, invokePattern, state, bounds.IsEmpty ? double.MaxValue : bounds.Top);
    }

    private static bool IsQuotaSectionCandidate(AutomationElement element)
    {
        try
        {
            if (element.Current.IsOffscreen)
            {
                return false;
            }

            var name = (element.Current.Name ?? "").Trim();
            return IsAccountQuotaHeading(name);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool IsAccountQuotaHeading(string value) =>
        new[] { "剩餘用量", "剩余用量", "remaining usage", "usage remaining" }
            .Any(heading => value.Equals(heading, StringComparison.OrdinalIgnoreCase)
                || value.StartsWith(heading + " ", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<AutomationElement> EnumerateBounded(AutomationElement root, int maxDepth, int maxElements)
    {
        var results = new List<AutomationElement>();
        var visited = 0;
        WalkElements(root, 0, maxDepth, maxElements, results, ref visited);
        return results;
    }

    private static void WalkElements(
        AutomationElement element,
        int depth,
        int maxDepth,
        int maxElements,
        List<AutomationElement> output,
        ref int visited)
    {
        if (depth > maxDepth || visited >= maxElements)
        {
            return;
        }

        visited++;
        output.Add(element);
        try
        {
            var walker = TreeWalker.RawViewWalker;
            for (var child = walker.GetFirstChild(element); child is not null && visited < maxElements; child = walker.GetNextSibling(child))
            {
                WalkElements(child, depth + 1, maxDepth, maxElements, output, ref visited);
            }
        }
        catch (ElementNotAvailableException)
        {
            // Profile popup content can disappear while ChatGPT updates the menu.
        }
    }

    private sealed record QuotaSectionCandidate(
        AutomationElement? Element,
        ExpandCollapsePattern? Pattern,
        InvokePattern? InvokePattern,
        ExpandCollapseState State,
        double BoundsTop);
}

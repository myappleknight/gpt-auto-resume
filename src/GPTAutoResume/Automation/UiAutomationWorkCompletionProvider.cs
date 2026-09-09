using System.Diagnostics;
using System.Windows.Automation;

namespace GPTAutoResume.Automation;

public interface IWorkCompletionProvider
{
    AssistantCompletionEvidence Read(TargetWindow target, ConversationTargetIdentity identity, CancellationToken cancellationToken = default);
}

public sealed record WorkCompletionAudit(
    bool ReadOnly,
    DateTimeOffset CapturedAt,
    string ViewMode,
    bool IdentityMatchedBefore,
    bool IdentityMatchedAfter,
    bool DocumentFound,
    bool Complete,
    int NodeCount,
    int MaxDepth,
    string Failure,
    AssistantCompletionEvidence Evidence,
    IReadOnlyList<FooterActionAudit> FooterActions)
{
    public LastTurnState LastTurnState => Evidence.LastTurnState;
    public FooterPresence FooterPresence => Evidence.Footer;
    public bool ObservationReliable => Complete && IdentityMatchedBefore && IdentityMatchedAfter
        && Failure.Length == 0 && Evidence.AssistantReplyIdentity.Length > 0;
    public bool LatestReplyContainerFound { get; init; }
    public IReadOnlyList<StructuralControlAudit> StructuralControls { get; init; } = [];
    public bool StopControlFound => StructuralControls.Any(n => n.InComposer && n.Token == "Running");
    public bool ProgressSignalFound => StructuralControls.Any(n => n.InLatestReply && n.Visible
        && n.ControlType == "ControlType.ProgressBar");
    public bool LimitInterruptionSignalFound =>
        StructuralControls.Any(n => n.InLatestReply && n.Visible && n.Token == "LimitInterruption");
    public string GeneratingSignal => StopControlFound ? "STOP_OR_QUEUE_CONTROL"
        : ProgressSignalFound ? "PROGRESS_PRESENT_UNVERIFIED" : "UNKNOWN";
    public string[] EvidenceReasons => [Evidence.Reason, ObservationReliable ? "BOUNDED_IDENTITY_CHECKED_SCAN" : "OBSERVATION_UNRELIABLE"];
}

public sealed record StructuralControlAudit(int NodeId, int ParentId, int Depth, string Token,
    string ControlType, string AutomationIdDigest, string ClassNameDigest, string LocalizedControlType,
    bool Visible, bool Enabled, bool Virtualized, bool InLatestReply, bool InComposer)
{
    public string ControlLabel { get; init; } = "";
    public string ClassName { get; init; } = "";
}

public sealed record FooterActionAudit(
    int NodeId,
    string Token,
    int Depth,
    bool InLatestReply,
    bool IsOffscreen,
    bool Visible,
    bool Enabled,
    string ControlType,
    string NameDigest,
    string AutomationId,
    string ClassName,
    string LocalizedControlType,
    string BoundingRectangle,
    string ParentToken);

public sealed class UiAutomationWorkCompletionProvider(UiAutomationReader reader) : IWorkCompletionProvider
{
    public AssistantCompletionEvidence Read(TargetWindow target, ConversationTargetIdentity identity, CancellationToken cancellationToken = default)
        => ReadAudit(target, identity, cancellationToken).Evidence;

    public WorkCompletionAudit ReadAudit(TargetWindow target, ConversationTargetIdentity identity, CancellationToken cancellationToken = default)
        => CompletionScanWorker.Read(target, identity, cancellationToken)
            ?? UnknownAudit(DateTimeOffset.Now, "SCAN_WORKER_CANCELLED_OR_FAILED");

    internal WorkCompletionAudit ReadAuditInProcess(TargetWindow target, ConversationTargetIdentity identity, CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        bool Expired() => cancellationToken.IsCancellationRequested || timer.Elapsed > TimeSpan.FromSeconds(5);
        var capturedAt = DateTimeOffset.Now;
        try
        {
            if (Expired()) return UnknownAudit(capturedAt, "SCAN_CANCELLED_OR_EXPIRED");
            if (!reader.IsConversationStillActive(target.Handle, identity))
                return UnknownAudit(capturedAt, "IDENTITY_CHANGED", identityBefore: false);
            var composer = reader.FindChatInputForDiscovery(target.Handle);
            if (composer is null) return UnknownAudit(capturedAt, "NO_SAFE_COMPOSER");
            var document = composer;
            for (var depth = 0; depth < 48 && document is not null && document.Current.ControlType != ControlType.Document; depth++)
                document = TreeWalker.RawViewWalker.GetParent(document);
            if (document is null || document.Current.ControlType != ControlType.Document || document.Current.ProcessId != target.ProcessId)
                return UnknownAudit(capturedAt, "NO_TARGET_DOCUMENT");

            var nodes = new List<CompletionNode>();
            var auditNodes = new List<FooterActionAudit>();
            var structuralNodes = new List<StructuralControlAudit>();
            var complete = true;
            var maxDepth = 0;
            var bounds = document.Current.BoundingRectangle;
            // Fetch the target document once; per-node cross-process walks exhaust the scan budget.
            var cache = new CacheRequest
            {
                TreeScope = TreeScope.Subtree,
                TreeFilter = System.Windows.Automation.Automation.RawViewCondition
            };
            foreach (var property in new[] { AutomationElement.ControlTypeProperty, AutomationElement.NameProperty,
                         AutomationElement.LocalizedControlTypeProperty,
                         AutomationElement.IsOffscreenProperty, AutomationElement.IsEnabledProperty,
                         AutomationElement.BoundingRectangleProperty, AutomationElement.ProcessIdProperty,
                         AutomationElement.AutomationIdProperty, AutomationElement.ClassNameProperty,
                         AutomationElement.IsVirtualizedItemPatternAvailableProperty }) cache.Add(property);

            void Visit(AutomationElement element, int parent, int depth)
            {
                if (nodes.Count >= 5000 || Expired()) { complete = false; return; }
                maxDepth = Math.Max(maxDepth, depth);
                var data = element.Cached;
                if (data.ProcessId != target.ProcessId) { complete = false; return; }
                var id = nodes.Count;
                var isComposer = data.ControlType == ControlType.Edit && System.Windows.Automation.Automation.Compare(element, composer);
                var token = isComposer ? CompletionToken.Composer : Token(data.ControlType.ProgrammaticName,
                    data.Name, "", data.LocalizedControlType);
                var rect = data.BoundingRectangle;
                var visible = !data.IsOffscreen && !rect.IsEmpty && rect.Width > 0 && rect.Height > 0 && bounds.Contains(rect);
                nodes.Add(new(id, parent, token, visible, data.IsEnabled,
                    token == CompletionToken.Content ? ConversationTargetIdentity.Hash(data.Name) : token.ToString()));
                // Keep structure and semantic control tokens only; no message text or draft values.
                if (data.ControlType != ControlType.Text || token == CompletionToken.LimitInterruption)
                    structuralNodes.Add(new(id, parent, depth, token.ToString(), data.ControlType.ProgrammaticName,
                        ConversationTargetIdentity.Hash(data.AutomationId), ConversationTargetIdentity.Hash(data.ClassName),
                        data.LocalizedControlType, visible, data.IsEnabled,
                        (bool)element.GetCachedPropertyValue(AutomationElement.IsVirtualizedItemPatternAvailableProperty), false, false)
                    {
                        ControlLabel = data.ControlType == ControlType.Button
                            && token is CompletionToken.Copy or CompletionToken.Rate or CompletionToken.Branch
                                or CompletionToken.Running or CompletionToken.Idle or CompletionToken.Continue
                            && data.Name.Length <= 80 ? data.Name : "",
                        ClassName = data.ClassName
                    });
                if (token is CompletionToken.Copy or CompletionToken.Rate or CompletionToken.Branch)
                {
                    var parentToken = parent >= 0 && parent < nodes.Count ? nodes[parent].Token.ToString() : "";
                    auditNodes.Add(new(id, token.ToString(), depth, false, data.IsOffscreen, visible, data.IsEnabled,
                        data.ControlType.ProgrammaticName, ConversationTargetIdentity.Hash(data.Name),
                        data.AutomationId, data.ClassName, data.LocalizedControlType,
                        $"{rect.Left:0},{rect.Top:0},{rect.Width:0},{rect.Height:0}", parentToken));
                }
                // Composer drafts are never read or hashed by this observer.
                if (data.ControlType == ControlType.Edit) return;
                var children = element.CachedChildren;
                if (depth >= 48 && children.Count > 0) { complete = false; return; }
                foreach (AutomationElement child in children)
                {
                    if (!complete) break;
                    Visit(child, id, depth + 1);
                }
                if (Expired()) complete = false;
            }

            Visit(document.GetUpdatedCache(cache), -1, 0);
            var evidence = WorkCompletionAnalyzer.Analyze(nodes, complete);
            auditNodes = MarkLatestReplyActions(nodes, auditNodes).ToList();
            var composerNode = nodes.SingleOrDefault(n => n.Token == CompletionToken.Composer);
            var assistantIndex = composerNode is null ? -1 : nodes.FindLastIndex(composerNode.Id,
                n => n.Token == CompletionToken.AssistantHeading);
            bool InComposer(StructuralControlAudit n)
            {
                var parent = n.ParentId;
                for (var i = 0; i < 64 && parent >= 0 && parent < nodes.Count; i++)
                {
                    if (parent == composerNode?.Parent) return true;
                    parent = nodes[parent].Parent;
                }
                return false;
            }
            var structures = structuralNodes.Select(n => n with {
                InLatestReply = assistantIndex >= 0 && n.NodeId > assistantIndex && n.NodeId < composerNode!.Id,
                InComposer = InComposer(n)
            }).Where(n => n.InLatestReply || n.InComposer || n.NodeId == (assistantIndex >= 0 ? nodes[assistantIndex].Parent : -2)).ToArray();
            var identityAfter = reader.IsConversationStillActive(target.Handle, identity);
            if (!identityAfter)
                return new(true, capturedAt, "RawView", true, false, true, complete, nodes.Count, maxDepth,
                    "IDENTITY_CHANGED_DURING_SCAN", AssistantCompletionEvidence.Unknown("IDENTITY_CHANGED_DURING_SCAN"), auditNodes);
            if (Expired()) return new(true, capturedAt, "RawView", true, identityAfter, true, false, nodes.Count, maxDepth,
                "SCAN_CANCELLED_OR_EXPIRED", AssistantCompletionEvidence.Unknown("SCAN_CANCELLED_OR_EXPIRED"), auditNodes);
            return new(true, capturedAt, "RawView", true, identityAfter, true, complete, nodes.Count, maxDepth, "",
                evidence, auditNodes) { StructuralControls = structures,
                    LatestReplyContainerFound = assistantIndex >= 0 && nodes[assistantIndex].Parent >= 0 };
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            return UnknownAudit(capturedAt, "UIA_UNAVAILABLE");
        }
    }

    private static WorkCompletionAudit UnknownAudit(DateTimeOffset capturedAt, string reason, bool identityBefore = true) =>
        new(true, capturedAt, "RawView", identityBefore, false, false, false, 0, 0, reason,
            AssistantCompletionEvidence.Unknown(reason), []);

    private static IEnumerable<FooterActionAudit> MarkLatestReplyActions(
        IReadOnlyList<CompletionNode> nodes,
        IReadOnlyList<FooterActionAudit> actions)
    {
        var composerIndex = nodes.ToList().FindIndex(n => n.Token == CompletionToken.Composer);
        if (composerIndex < 0) return actions;
        var lastAssistant = nodes.ToList().FindLastIndex(composerIndex, n => n.Token == CompletionToken.AssistantHeading);
        if (lastAssistant < 0) return actions;
        return actions.Select(action => action with
        {
            InLatestReply = action.NodeId > lastAssistant && action.NodeId < composerIndex
        });
    }

    public static CompletionToken Token(string type, string name, string role, string localizedType)
    {
        name = name.Trim();
        bool Matches(params string[] labels) => labels.Contains(name, StringComparer.OrdinalIgnoreCase);
        if (IsLimitInterruptionText(name)) return CompletionToken.LimitInterruption;
        var heading = role == "heading" || localizedType is "標題" or "heading" or "見出し";
        if (heading && Matches("ChatGPT 說：", "ChatGPT said:", "ChatGPT says:", "ChatGPT:", "ChatGPT の発言："))
            return CompletionToken.AssistantHeading;
        if (heading && Matches("你說：", "You said:", "You:", "あなた:", "あなた：")) return CompletionToken.UserHeading;
        if (role == "status" || type == "ControlType.StatusBar" || localizedType is "狀態" or "status" or "ステータス") return CompletionToken.Status;
        if (type == "ControlType.Button")
        {
            if (Matches("複製", "Copy", "コピーする", "コピー")) return CompletionToken.Copy;
            if (Matches("評價回覆", "Rate response", "回答を評価")) return CompletionToken.Rate;
            if (Matches("從此處分支對話", "Branch conversation from here", "Branch in new chat", "ここから会話を分岐")) return CompletionToken.Branch;
            if (Matches("停止", "Stop", "Stop generating", "生成を停止", "加入佇列", "Queue message")) return CompletionToken.Running;
            if (Matches("傳送", "送出", "Send", "Send message", "送信")) return CompletionToken.Idle;
            if (Matches("繼續", "Continue")) return CompletionToken.Continue;
        }
        return type == "ControlType.Text" && name.Length > 0 ? CompletionToken.Content : CompletionToken.Other;
    }

    private static bool IsLimitInterruptionText(string name)
    {
        if (name.Length < 4 || name.Length > 220) return false;
        return name.Contains("你已達使用上限", StringComparison.OrdinalIgnoreCase)
            || name.Contains("你的 Codex 和工作使用量已用完", StringComparison.OrdinalIgnoreCase)
            || name.Contains("你的Codex和工作使用量已用完", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Codex 和工作使用量已用完", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Codex和工作使用量已用完", StringComparison.OrdinalIgnoreCase)
            || name.Contains("You've reached your usage limit", StringComparison.OrdinalIgnoreCase)
            || name.Contains("You're out of Codex and Work usage", StringComparison.OrdinalIgnoreCase)
            || name.Contains("out of Codex and Work usage", StringComparison.OrdinalIgnoreCase)
            || name.Contains("使用上限に到達", StringComparison.OrdinalIgnoreCase);
    }
}

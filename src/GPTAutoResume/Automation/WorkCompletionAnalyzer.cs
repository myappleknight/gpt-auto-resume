namespace GPTAutoResume.Automation;

public enum CompletionToken { Other, UserHeading, AssistantHeading, Status, Content, Composer, Copy, Rate, Branch, Running, Idle, LimitInterruption, Continue }
public enum FooterPresence { Present, Absent, Unknown }
public enum LastTurnState { Running, Completed, IncompleteStopped, Unknown }

// Only semantic labels and digests cross the UIA boundary; never conversation bodies.
public sealed record CompletionNode(int Id, int Parent, CompletionToken Token, bool Visible, bool Enabled, string Digest);
public sealed record AssistantCompletionEvidence(string AssistantReplyIdentity, bool? Stopped, FooterPresence Footer, string Reason)
{
    public LastTurnState LastTurnState => AssistantReplyIdentity.Length == 0 ? LastTurnState.Unknown
        : Stopped == false ? LastTurnState.Running
        : Stopped == true && Footer == FooterPresence.Present ? LastTurnState.Completed
        : Stopped == true && Footer == FooterPresence.Absent ? LastTurnState.IncompleteStopped
        : LastTurnState.Unknown;
    public bool IsIncomplete => Stopped == true && Footer == FooterPresence.Absent && AssistantReplyIdentity.Length > 0;
    public static AssistantCompletionEvidence Unknown(string reason) => new("", null, FooterPresence.Unknown, reason);
}

public static class WorkCompletionAnalyzer
{
    public static AssistantCompletionEvidence Analyze(IReadOnlyList<CompletionNode> nodes, bool complete)
    {
        if (!complete) return AssistantCompletionEvidence.Unknown("INCOMPLETE_TREE");
        var composers = nodes.Where(n => n.Token == CompletionToken.Composer).ToArray();
        if (composers.Length != 1 || !composers[0].Visible || !composers[0].Enabled)
            return AssistantCompletionEvidence.Unknown("COMPOSER_UNAVAILABLE");
        var composer = composers[0];
        var indexed = nodes.ToList();
        var composerIndex = indexed.IndexOf(composer);
        var lastAssistant = indexed.FindLastIndex(composerIndex, n => n.Token == CompletionToken.AssistantHeading);
        if (lastAssistant < 0) return AssistantCompletionEvidence.Unknown("NO_ASSISTANT_BOUNDARY");
        var lastUser = indexed.FindLastIndex(composerIndex, n => n.Token == CompletionToken.UserHeading);
        if (lastUser < 0 || lastUser > lastAssistant)
            return AssistantCompletionEvidence.Unknown("NO_LAST_ASSISTANT_AFTER_USER");

        var heading = nodes[lastAssistant];
        var reply = nodes.Skip(lastAssistant + 1).Take(composerIndex - lastAssistant - 1).ToArray();
        // A markdown heading quoting "ChatGPT says" is not an assistant-role boundary.
        if (!reply.Any(n => n.Parent == heading.Parent && n.Token == CompletionToken.Status))
            return AssistantCompletionEvidence.Unknown("UNVERIFIED_ASSISTANT_BOUNDARY");
        if (reply.LastOrDefault(n => n.Token is CompletionToken.Content or CompletionToken.LimitInterruption)?.Visible != true)
            return AssistantCompletionEvidence.Unknown("LAST_REPLY_NOT_VISIBLE");

        var byId = nodes.ToDictionary(n => n.Id);
        bool Within(CompletionNode node, int parent)
        {
            for (var i = 0; i < 64 && node.Parent >= 0; i++)
            {
                if (node.Parent == parent) return true;
                if (!byId.TryGetValue(node.Parent, out node!)) break;
            }
            return false;
        }
        // Stop/send controls must be in the composer container, not in a message or sidebar.
        var composerControls = nodes.Where(n => Within(n, composer.Parent)).ToArray();
        var nativeContinue = composerControls.Any(n => n.Token == CompletionToken.Continue && n.Visible && n.Enabled);
        bool? stopped = composerControls.Any(n => n.Token == CompletionToken.Running) ? false
            : nativeContinue || composerControls.Any(n => n.Token == CompletionToken.Idle && n.Visible && n.Enabled) ? true : null;
        static int FooterCount(IEnumerable<CompletionNode> slice) => slice.Select(n => n.Token)
            .Where(t => t is CompletionToken.Copy or CompletionToken.Rate or CompletionToken.Branch).Distinct().Count();
        var footerTokens = FooterCount(reply);
        // An enabled native Continue action is positive interruption evidence. An empty scan alone is not.
        var footer = footerTokens == 3 ? FooterPresence.Present
            : footerTokens == 0 && nativeContinue && stopped == true ? FooterPresence.Absent : FooterPresence.Unknown;
        // Stable across a provider refresh/restart. Identical content may conservatively share a claim.
        var fingerprint = ConversationTargetIdentity.Hash(string.Join("|", nodes.Skip(lastUser).Take(composerIndex - lastUser)
            .Where(n => n.Token is CompletionToken.UserHeading or CompletionToken.AssistantHeading
                or CompletionToken.Content or CompletionToken.LimitInterruption)
            .Select(n => $"{n.Token}:{n.Digest}")));
        return new(fingerprint, stopped, footer, stopped == false ? "RUNNING" : footer == FooterPresence.Present ? "COMPLETED_FOOTER"
            : footer == FooterPresence.Absent ? "NATIVE_CONTINUE_WITHOUT_COMPLETED_FOOTER" : "FOOTER_ABSENCE_UNVERIFIED");
    }
}

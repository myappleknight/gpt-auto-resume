namespace GPTAutoResume.Core;

using GPTAutoResume.Automation;

// Nullable facts distinguish unavailable evidence from confirmed absence.
public sealed record WorkCompletionObservation(
    string ConversationIdentity, string AssistantReplyIdentity, DateTimeOffset CapturedAt,
    bool Selected, bool? FreshAccountQuotaAvailable, bool? Stopped, FooterPresence Footer);

public sealed class IncompleteWorkGate
{
    private readonly Dictionary<string, WorkCompletionObservation> _previous = new();
    private readonly HashSet<string> _confirmed = new(StringComparer.Ordinal);
    private static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(60);

    public bool Observe(WorkCompletionObservation current, DateTimeOffset now)
    {
        foreach (var key in _previous.Where(pair => now - pair.Value.CapturedAt > MaximumAge)
                     .Select(pair => pair.Key).ToArray())
            Forget(key);

        if (string.IsNullOrWhiteSpace(current.ConversationIdentity)) return false;
        if (!current.Selected || current.FreshAccountQuotaAvailable != true || current.Stopped != true
            || current.Footer != FooterPresence.Absent || string.IsNullOrWhiteSpace(current.AssistantReplyIdentity)
            || current.CapturedAt > now || now - current.CapturedAt > MaximumAge)
        {
            Forget(current.ConversationIdentity);
            return false;
        }

        if (!_previous.TryGetValue(current.ConversationIdentity, out var previous)
            || previous.AssistantReplyIdentity != current.AssistantReplyIdentity)
        {
            _confirmed.Remove(current.ConversationIdentity);
            _previous[current.ConversationIdentity] = current;
            return false;
        }

        // A cached snapshot or rapid repeat is not an independent confirmation.
        if (current.CapturedAt < previous.CapturedAt)
        {
            Forget(current.ConversationIdentity);
            return false;
        }
        if (current.CapturedAt - previous.CapturedAt < MinimumGap) return _confirmed.Contains(current.ConversationIdentity);
        _previous[current.ConversationIdentity] = current;
        _confirmed.Add(current.ConversationIdentity);
        return true;
    }

    public void Reset()
    {
        _previous.Clear();
        _confirmed.Clear();
    }

    private void Forget(string key)
    {
        _previous.Remove(key);
        _confirmed.Remove(key);
    }

    public void RetainOnly(IReadOnlySet<string> identities)
    {
        foreach (var key in _previous.Keys.Where(key => !identities.Contains(key)).ToArray())
            Forget(key);
    }
}

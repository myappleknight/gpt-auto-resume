namespace GPTAutoResume.Core;

public sealed class InMemoryEventStore : IEventStore
{
    private readonly HashSet<string> _attempted = [];

    public bool HasResumeAttempt(string eventId) => _attempted.Contains(eventId);

    public void MarkResumeAttempted(string eventId, DateTimeOffset attemptedAt, bool sent) =>
        _attempted.Add(eventId);
}

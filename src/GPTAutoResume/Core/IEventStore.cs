namespace GPTAutoResume.Core;

public interface IEventStore
{
    bool HasResumeAttempt(string eventId);
    void MarkResumeAttempted(string eventId, DateTimeOffset attemptedAt, bool sent);
}

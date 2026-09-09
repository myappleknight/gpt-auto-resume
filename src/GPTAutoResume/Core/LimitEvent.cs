using GPTAutoResume.Automation;

namespace GPTAutoResume.Core;

public sealed record LimitEvent(
    string EventId,
    int ProcessId,
    nint WindowHandle,
    string WindowTitle,
    DateTimeOffset DetectedAt,
    DateTimeOffset RetryAt,
    string SourceText,
    int Confidence)
{
    public bool ResumeAttempted { get; init; }
    public DateTimeOffset? ResumeSentAt { get; init; }
    public ConversationTargetIdentity? ConversationTarget { get; init; }
    public string? AssistantReplyIdentity { get; init; }
}

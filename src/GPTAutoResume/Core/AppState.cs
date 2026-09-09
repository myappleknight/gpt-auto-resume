namespace GPTAutoResume.Core;

public enum AppState
{
    WaitingForTarget,
    Monitoring,
    LimitDetected,
    WaitingForRetry,
    ReadyToResume,
    PreResumeVerify,
    ResumeSending,
    Verifying,
    ResumeSuccess,
    NeedsAttention,
    Paused,
    Error
}

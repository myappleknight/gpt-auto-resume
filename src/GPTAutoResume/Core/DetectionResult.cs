namespace GPTAutoResume.Core;

public enum DetectionKind
{
    Ignore,
    PossibleLimit,
    LimitDetected
}

public sealed record DetectionResult(
    DetectionKind Kind,
    int Confidence,
    DateTimeOffset? RetryAt,
    string? DetectedLanguage,
    IReadOnlyList<string> MatchedPatternIds)
{
    public static DetectionResult Ignore(int confidence = 0) =>
        new(DetectionKind.Ignore, confidence, null, null, Array.Empty<string>());
}

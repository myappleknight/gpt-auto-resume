using GPTAutoResume.Automation;

namespace GPTAutoResume.Core;

public interface IPossibleLimitDiagnosticStore
{
    void Save(TargetWindow window, string visibleText, DetectionResult result, bool hasWarningRole, string rejectionReason, DateTimeOffset capturedAt);
}

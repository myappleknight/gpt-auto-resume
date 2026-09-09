namespace GPTAutoResume.Automation;

public interface IResumeSender
{
    bool VerifyTarget(TargetWindow target);
    bool TrySend(TargetWindow target, string text, bool dryRun, bool sendEnter);
    bool TrySend(TargetWindow target, string text, bool dryRun, bool sendEnter, Func<bool> verifyTargetState) =>
        verifyTargetState() && TrySend(target, text, dryRun, sendEnter);
}

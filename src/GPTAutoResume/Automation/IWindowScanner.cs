namespace GPTAutoResume.Automation;

public interface IWindowScanner
{
    IReadOnlyList<TargetWindow> FindTargets();
    bool IsStillValid(TargetWindow target);
}

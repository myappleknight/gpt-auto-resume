using GPTAutoResume.Core;

namespace GPTAutoResume.Automation;

public interface IWorkVisit : IDisposable
{
    ConversationTargetIdentity Identity { get; }
    bool IsCurrent { get; }
    // A matching display title or a runtime id acquired after switching is not saved permission.
    bool SelectionIdentityVerified => false;
}

public interface IWorkNavigator
{
    IWorkVisit? TryVisit(TargetWindow window, WorkSelectionRecord work, CancellationToken cancellationToken);
}

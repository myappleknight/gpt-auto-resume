namespace GPTAutoResume.Automation;

public interface ILimitSurfaceProvider
{
    IReadOnlyList<LimitSurfaceCandidate> FindLimitSurfaces(TargetWindow target);
}

public sealed class NullLimitSurfaceProvider : ILimitSurfaceProvider
{
    public IReadOnlyList<LimitSurfaceCandidate> FindLimitSurfaces(TargetWindow target) => [];
}

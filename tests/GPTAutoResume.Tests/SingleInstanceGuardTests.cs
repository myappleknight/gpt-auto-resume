using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void SecondGuardWithSameNameDoesNotAcquire()
    {
        var name = $"GPTAutoResume.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceGuard.TryAcquire(name);
        using var second = SingleInstanceGuard.TryAcquire(name);

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
    }
}

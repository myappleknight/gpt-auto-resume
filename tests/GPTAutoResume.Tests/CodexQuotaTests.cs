using GPTAutoResume.Automation;
using Xunit;

namespace GPTAutoResume.Tests;

public class CodexQuotaTests
{
    [Fact]
    public void MultiBucketResponseDoesNotBorrowAnotherQuota()
    {
        Assert.Null(CodexAccountQuotaProvider.Parse("""
            {"rateLimitsByLimitId":{"other":{"primary":{"usedPercent":100,"windowDurationMins":300}}},"rateLimits":{"primary":{"usedPercent":100,"windowDurationMins":300}}}
            """, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MissingResetAndShortWindowStayUnknown()
    {
        var snapshot = CodexAccountQuotaProvider.Parse("""
            {"rateLimits":{"primary":{"usedPercent":99.5,"windowDurationMins":10080}}}
            """, DateTimeOffset.UtcNow);
        Assert.NotNull(snapshot);
        Assert.Null(snapshot.ShortWindowRemainingPercent);
        Assert.Null(snapshot.WeeklyResetAt);
        Assert.Equal(1, snapshot.WeeklyRemainingPercent);
    }

    [Fact]
    public void NullOrUnexpectedWindowFieldsAreUnknown()
    {
        Assert.Null(CodexAccountQuotaProvider.Parse("""
            {"rateLimits":{"primary":{"usedPercent":null,"windowDurationMins":300},"secondary":{"usedPercent":10,"windowDurationMins":15}}}
            """, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void OfficialQuotaUsesWindowDurationAndConvertsUsedToRemaining()
    {
        var snapshot = CodexAccountQuotaProvider.Parse("""
            {"rateLimits":{"limitId":"codex","primary":{"usedPercent":72,"windowDurationMins":300,"resetsAt":1788847139},"secondary":{"usedPercent":58,"windowDurationMins":10080,"resetsAt":1789374595}}}
            """, DateTimeOffset.UtcNow);
        Assert.NotNull(snapshot);
        Assert.Equal(28, snapshot.ShortWindowRemainingPercent);
        Assert.Equal(42, snapshot.WeeklyRemainingPercent);
        Assert.Equal(1788847139, snapshot.ShortWindowResetAt!.Value.ToUnixTimeSeconds());
    }
}

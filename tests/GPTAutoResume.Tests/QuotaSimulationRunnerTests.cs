using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class QuotaSimulationRunnerTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 7, 6, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void ExhaustedShortWindowDrivesMonitorServiceWaitingState()
    {
        var result = QuotaSimulationRunner.Run(0, "清晨7:28", 6, "9月14日", _now);

        Assert.Equal(AppState.WaitingForRetry, result.State);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 7, 28, 0, TimeSpan.FromHours(8)), result.RetryAt);
        Assert.Equal("Account quota exhausted", result.UsageLimitText);
    }

    [Fact]
    public void AvailableQuotaDrivesMonitorServiceMonitoringState()
    {
        var result = QuotaSimulationRunner.Run(74, "晚上7:28", 6, "9月14日", _now);

        Assert.Equal(AppState.Monitoring, result.State);
        Assert.Null(result.RetryAt);
    }
}

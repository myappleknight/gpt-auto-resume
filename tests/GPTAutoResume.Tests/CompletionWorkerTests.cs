using System.Diagnostics;
using GPTAutoResume.Automation;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class CompletionWorkerTests
{
    [Fact]
    public void CancelledScanReturnsUnknownWithoutOpeningTarget()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var clock = Stopwatch.StartNew();
        var result = new UiAutomationWorkCompletionProvider(new UiAutomationReader()).ReadAudit(
            new TargetWindow(-1, 0, "", ""), new("", "", "", "", "", ""), cancellation.Token);
        Assert.Equal(FooterPresence.Unknown, result.FooterPresence);
        Assert.False(result.ObservationReliable);
        Assert.Equal("SCAN_WORKER_CANCELLED_OR_FAILED", result.Evidence.Reason);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
    }
}

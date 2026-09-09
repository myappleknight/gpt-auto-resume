using GPTAutoResume.Core;
using GPTAutoResume.Automation;
using Xunit;

namespace GPTAutoResume.Tests;

public class IncompleteWorkGateTests
{
    [Fact]
    public void ConfirmedReplyRemainsConfirmedDuringRapidFreshPolling()
    {
        var gate = new IncompleteWorkGate();
        var now = DateTimeOffset.UtcNow;
        var observation = new WorkCompletionObservation("work-a", "reply-a", now, true, true, true, FooterPresence.Absent);
        Assert.False(gate.Observe(observation, now));
        Assert.True(gate.Observe(observation with { CapturedAt = now.AddSeconds(5) }, now.AddSeconds(5)));
        Assert.True(gate.Observe(observation with { CapturedAt = now.AddSeconds(6) }, now.AddSeconds(6)));
    }
    [Theory]
    [InlineData(false, true, true, FooterPresence.Absent)]
    [InlineData(true, false, true, FooterPresence.Absent)]
    [InlineData(true, null, true, FooterPresence.Absent)]
    [InlineData(true, true, false, FooterPresence.Absent)]
    [InlineData(true, true, null, FooterPresence.Absent)]
    [InlineData(true, true, true, FooterPresence.Present)]
    [InlineData(true, true, true, FooterPresence.Unknown)]
    public void DisqualifyingOrUnknownEvidenceResetsConfirmation(bool selected, bool? quota, bool? stopped, FooterPresence footer)
    {
        var gate = new IncompleteWorkGate();
        var now = DateTimeOffset.UtcNow;
        var good = new WorkCompletionObservation("work-a", "reply-a", now, true, true, true, FooterPresence.Absent);
        Assert.False(gate.Observe(good, now));
        Assert.False(gate.Observe(good with { CapturedAt = now.AddSeconds(5), Selected = selected,
            FreshAccountQuotaAvailable = quota, Stopped = stopped, Footer = footer }, now.AddSeconds(5)));
        Assert.False(gate.Observe(good with { CapturedAt = now.AddSeconds(10) }, now.AddSeconds(10)));
    }

    [Fact]
    public void DifferentReplyWorkOrExpiredObservationCannotReuseConfirmation()
    {
        var gate = new IncompleteWorkGate();
        var now = DateTimeOffset.UtcNow;
        var good = new WorkCompletionObservation("work-a", "reply-a", now, true, true, true, FooterPresence.Absent);
        Assert.False(gate.Observe(good, now));
        Assert.False(gate.Observe(good with { ConversationIdentity = "work-b", CapturedAt = now.AddSeconds(10) }, now.AddSeconds(10)));
        Assert.False(gate.Observe(good with { AssistantReplyIdentity = "reply-b", CapturedAt = now.AddSeconds(10) }, now.AddSeconds(10)));
        Assert.False(gate.Observe(good with { CapturedAt = now.AddMinutes(2) }, now.AddMinutes(2)));
    }

    [Fact]
    public void RequiresTwoIndependentObservationsOfSameReply()
    {
        var gate = new IncompleteWorkGate();
        var now = DateTimeOffset.UtcNow;
        var observation = new WorkCompletionObservation("work-a", "reply-a", now, true, true, true, FooterPresence.Absent);
        Assert.False(gate.Observe(observation, now));
        Assert.False(gate.Observe(observation, now.AddSeconds(10)));
        Assert.True(gate.Observe(observation with { CapturedAt = now.AddSeconds(10) }, now.AddSeconds(10)));
    }
}

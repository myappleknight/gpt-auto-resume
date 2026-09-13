using System.IO;
using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class JsonEventStoreTests
{
    [Fact]
    public void CorruptJournalFailsClosedInsteadOfForgettingAttempts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-events-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ this is not valid json");
        var store = new JsonEventStore(path);

        Assert.True(store.HasResumeAttempt("any-event-id"));
        Assert.Throws<InvalidDataException>(() => store.MarkResumeAttempted("any-event-id", DateTimeOffset.Now, sent: false));
    }

    [Fact]
    public void MarkResumeAttemptedWritesThroughTemporaryFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-events-{Guid.NewGuid():N}.json");
        var store = new JsonEventStore(path);

        store.MarkResumeAttempted("event-a", new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.FromHours(8)), sent: true);
        var reloaded = new JsonEventStore(path);

        Assert.True(reloaded.HasResumeAttempt("event-a"));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ExistingStoreSeesAttemptsWrittenByAnotherStoreInstance()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-events-{Guid.NewGuid():N}.json");
        var first = new JsonEventStore(path);
        var second = new JsonEventStore(path);

        first.MarkResumeAttempted("event-a", DateTimeOffset.Now, sent: false);

        Assert.True(second.HasResumeAttempt("event-a"));
    }

    [Fact]
    public void SecondPreInputClaimForSameEventIsBlocked()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-events-{Guid.NewGuid():N}.json");
        var first = new JsonEventStore(path);
        var second = new JsonEventStore(path);

        first.MarkResumeAttempted("event-a", DateTimeOffset.Now, sent: false);

        Assert.Throws<InvalidOperationException>(() =>
            second.MarkResumeAttempted("event-a", DateTimeOffset.Now, sent: false));
    }

    [Fact]
    public void OldUnconfirmedAttemptDoesNotBlockRetryForever()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-events-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            """
            {
              "event-a": {
                "ResumeAttempted": true,
                "AttemptedAt": "2026-09-07T08:00:00+08:00",
                "Sent": false
              }
            }
            """);
        var store = new JsonEventStore(path);

        Assert.False(store.HasResumeAttempt("event-a"));
    }

    [Fact]
    public void SentAttemptStillBlocksRetryPermanently()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-events-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            """
            {
              "event-a": {
                "ResumeAttempted": true,
                "AttemptedAt": "2026-09-07T08:00:00+08:00",
                "Sent": true
              }
            }
            """);
        var store = new JsonEventStore(path);

        Assert.True(store.HasResumeAttempt("event-a"));
    }
}

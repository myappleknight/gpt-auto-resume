using System.Windows.Automation;
using GPTAutoResume.Automation;
using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class MonitorServiceTests
{
    private readonly DateTimeOffset _now = new(2026, 8, 31, 19, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void SelectedWorkWithUnobservableCompletionShowsAttentionAndKeepsChecking()
    {
        var h = CompletionService();
        h.Completion.Evidence = new("reply-a", null, FooterPresence.Unknown, "FOOTER_ABSENCE_UNVERIFIED");
        h.Service.Tick(_now);
        Assert.Equal(AppState.NeedsAttention, h.Service.State);
        Assert.Contains("FOOTER_ABSENCE_UNVERIFIED", h.Service.ResumeStatusText);
        Assert.Equal(0, h.Sender.SendCount);

        h.Completion.Evidence = new("reply-a", true, FooterPresence.Absent, "INCOMPLETE_REPLY");
        h.Service.Tick(_now.AddSeconds(10));
        Assert.Equal(0, h.Sender.SendCount);
        h.Service.Tick(_now.AddSeconds(20));
        Assert.Equal(1, h.Sender.SendCount);
    }

    [Fact]
    public void RunningSelectedWorkWithUnknownFooterIsNotAnAttentionFailure()
    {
        var h = CompletionService();
        h.Completion.Evidence = new("reply-a", false, FooterPresence.Unknown, "RUNNING");
        h.Service.Tick(_now);
        Assert.Equal(AppState.Monitoring, h.Service.State);
        Assert.Equal(0, h.Sender.SendCount);
    }

    [Fact]
    public void ConversationLimitWordingWithUnknownFooterCannotBypassCompletionGate()
    {
        var h = CompletionService();
        h.Completion.Evidence = new("reply-a", true, FooterPresence.Unknown,
            "FOOTER_ABSENCE_UNVERIFIED:latest reply discusses 使用上限 / Codex 和工作使用量已用完");

        h.Service.Tick(_now);
        h.Service.Tick(_now.AddSeconds(10));

        Assert.Equal(AppState.NeedsAttention, h.Service.State);
        Assert.Equal(0, h.Sender.SendCount);
        Assert.Contains("FOOTER_ABSENCE_UNVERIFIED", h.Service.ResumeStatusText);
    }

    [Fact]
    public void TrustedLimitSurfaceWithUnknownFooterQualifiesActiveSelectedWorkAfterTwoConfirmations()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var identity = Identity("work-a");
        var sender = new FakeSender();
        var completion = new MutableCompletion
        {
            Evidence = new("reply-a", true, FooterPresence.Unknown, "FOOTER_ABSENCE_UNVERIFIED")
        };
        var service = new MonitorService(
            new AppConfig { AutoResume = true, ResumeDelaySeconds = 0,
                ResumePolicy = ResumePolicy.AutomaticForegroundResume, RequireWorkSelection = true },
            new FakeScanner([target]),
            new FakeReader([]) { ConversationIdentity = identity },
            sender,
            new InMemoryEventStore(),
            new NullPossibleLimitDiagnosticStore(),
            new FakeWorkSelectionStore([identity]),
            TestCatalog(),
            new RetryTimeParser(),
            accountQuotaProvider: new MutableAccountQuota
            { Snapshot = new(100, _now.AddHours(5), 72, _now.AddDays(6), _now) },
            workCompletionProvider: completion,
            limitSurfaceProvider: new FakeLimitSurfaceProvider([
                Candidate("You're out of Codex and Work usage. Add credits or wait for usage to reset.", LimitSurfaceKind.Banner, depth: 30)
            ]));

        service.Tick(_now);
        Assert.Equal(0, sender.SendCount);
        service.Tick(_now.AddSeconds(10));

        Assert.Equal(1, sender.SendCount);
        Assert.Equal(AppState.Verifying, service.State);
        Assert.Equal(2, completion.ReadCount);
    }

    [Fact]
    public void TrustedLimitSurfaceCanOverrideStaleRunningControlAfterTwoConfirmations()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var identity = Identity("work-a");
        var sender = new FakeSender();
        var completion = new MutableCompletion
        {
            Evidence = new("reply-a", false, FooterPresence.Unknown, "RUNNING")
        };
        var service = new MonitorService(
            new AppConfig { AutoResume = true, ResumeDelaySeconds = 0,
                ResumePolicy = ResumePolicy.AutomaticForegroundResume, RequireWorkSelection = true },
            new FakeScanner([target]),
            new FakeReader([]) { ConversationIdentity = identity },
            sender,
            new InMemoryEventStore(),
            new NullPossibleLimitDiagnosticStore(),
            new FakeWorkSelectionStore([identity]),
            TestCatalog(),
            new RetryTimeParser(),
            accountQuotaProvider: new MutableAccountQuota
            { Snapshot = new(100, _now.AddHours(5), 72, _now.AddDays(6), _now) },
            workCompletionProvider: completion,
            limitSurfaceProvider: new FakeLimitSurfaceProvider([
                Candidate("You're out of Codex and Work usage. Reset usage or wait for usage to reset.", LimitSurfaceKind.Banner, depth: 30)
            ]));

        service.Tick(_now);
        Assert.Equal(0, sender.SendCount);
        service.Tick(_now.AddSeconds(10));

        Assert.Equal(1, sender.SendCount);
    }

    [Fact]
    public void ActiveTitleRefreshTransfersUniqueExistingPermissionBeforeCompletionCheck()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var oldIdentity = Identity("old-volatile-title");
        var refreshedIdentity = Identity("stable-header-title");
        var selections = new FakeWorkSelectionStore([oldIdentity]) { Title = "升級 Facebook Token 前台工具" };
        var sender = new FakeSender();
        var service = new MonitorService(
            new AppConfig { AutoResume = true, ResumeDelaySeconds = 0,
                ResumePolicy = ResumePolicy.AutomaticForegroundResume, RequireWorkSelection = true },
            new FakeScanner([target]),
            new FakeReader([]) { ConversationIdentity = refreshedIdentity, ActiveTitle = "升級 Facebook Token 前台工具" },
            sender,
            new InMemoryEventStore(),
            new NullPossibleLimitDiagnosticStore(),
            selections,
            TestCatalog(),
            new RetryTimeParser(),
            accountQuotaProvider: new MutableAccountQuota
            { Snapshot = new(100, _now.AddHours(5), 72, _now.AddDays(6), _now) },
            workCompletionProvider: new MutableCompletion
            { Evidence = new("reply-a", true, FooterPresence.Absent, "INCOMPLETE") });

        service.Tick(_now);
        service.Tick(_now.AddSeconds(10));

        Assert.True(selections.IsAutoResumeEnabled(refreshedIdentity));
        Assert.Equal(1, sender.SendCount);
    }

    [Fact]
    public void ConversationBodyLimitSurfaceCannotQualifyUnknownFooter()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var identity = Identity("work-a");
        var sender = new FakeSender();
        var completion = new MutableCompletion
        {
            Evidence = new("reply-a", true, FooterPresence.Unknown, "FOOTER_ABSENCE_UNVERIFIED")
        };
        var service = new MonitorService(
            new AppConfig { AutoResume = true, ResumeDelaySeconds = 0,
                ResumePolicy = ResumePolicy.AutomaticForegroundResume, RequireWorkSelection = true },
            new FakeScanner([target]),
            new FakeReader([]) { ConversationIdentity = identity },
            sender,
            new InMemoryEventStore(),
            new NullPossibleLimitDiagnosticStore(),
            new FakeWorkSelectionStore([identity]),
            TestCatalog(),
            new RetryTimeParser(),
            accountQuotaProvider: new MutableAccountQuota
            { Snapshot = new(100, _now.AddHours(5), 72, _now.AddDays(6), _now) },
            workCompletionProvider: completion,
            limitSurfaceProvider: new FakeLimitSurfaceProvider([
                Candidate("You're out of Codex and Work usage. Add credits or wait for usage to reset.", LimitSurfaceKind.ConversationBody, depth: 30)
            ]));

        service.Tick(_now);
        service.Tick(_now.AddSeconds(10));

        Assert.Equal(0, sender.SendCount);
        Assert.Equal(AppState.NeedsAttention, service.State);
        Assert.Contains("FOOTER_ABSENCE_UNVERIFIED", service.ResumeStatusText);
    }

    [Fact]
    public void AccountAvailableNeedsTwoStoppedIncompleteObservationsBeforeResume()
    {
        var quota = new MutableAccountQuota { Snapshot = new(28, _now.AddHours(1), 42, _now.AddDays(1), _now) };
        var sender = new FakeSender();
        var reader = new FakeReader([]) { ConversationIdentity = Identity("work-a") };
        var completion = new MutableCompletion();
        var service = new MonitorService(new AppConfig { AutoResume = true, ResumeDelaySeconds = 0,
            ResumePolicy = ResumePolicy.AutomaticForegroundResume }, new FakeScanner([new(10, 100, "ChatGPT", "ChatGPT")]),
            reader, sender, new InMemoryEventStore(), new NullPossibleLimitDiagnosticStore(),
            new FakeWorkSelectionStore([reader.ConversationIdentity]), TestCatalog(), new RetryTimeParser(),
            accountQuotaProvider: quota, workCompletionProvider: completion);
        service.Tick(_now);
        Assert.Equal(0, sender.SendCount);
        service.Tick(_now.AddSeconds(10));
        Assert.Equal(1, sender.SendCount);
        service.Tick(_now.AddSeconds(40));
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(0, reader.TextReads);
    }

    [Fact]
    public void SenderStateGuardDoesNotRepeatHeavyCompletionScanAfterPreInputVerification()
    {
        var h = CompletionService();

        h.Service.Tick(_now);
        h.Service.Tick(_now.AddSeconds(10));

        Assert.Equal(1, h.Sender.SendCount);
        Assert.Equal(3, h.Completion.ReadCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CheckedInactiveWorkIsVisitedTwiceAndRestoredAfterResumeCheck(bool selectionVerified)
    {
        var original = Identity("unselected-running");
        var selected = Identity("checked-stopped");
        var reader = new FakeReader([]) { ConversationIdentity = original };
        var navigator = new FakeWorkNavigator(reader, [selected], selectionVerified);
        var sender = new FakeSender();
        var completion = new MutableCompletion { ReadOverride = () => reader.ConversationIdentity == selected
            ? new("selected-reply", true, FooterPresence.Absent, "INCOMPLETE")
            : new("original-reply", false, FooterPresence.Unknown, "RUNNING") };
        var service = new MonitorService(new AppConfig { AutoResume = true, ResumeDelaySeconds = 0,
            ResumePolicy = ResumePolicy.AutomaticForegroundResume }, new FakeScanner([new(10, 100, "ChatGPT", "ChatGPT")]),
            reader, sender, new InMemoryEventStore(), new NullPossibleLimitDiagnosticStore(), new FakeWorkSelectionStore([selected]),
            TestCatalog(), new RetryTimeParser(), accountQuotaProvider: new MutableAccountQuota
            { Snapshot = new(30, _now.AddHours(1), 40, _now.AddDays(1), _now) }, workCompletionProvider: completion, workNavigator: navigator);
        service.Tick(_now);
        Assert.Equal(1, navigator.Visits);
        Assert.Equal(original, reader.ConversationIdentity);
        Assert.Equal(0, sender.SendCount);
        service.Tick(_now.AddSeconds(10));
        Assert.Equal(selectionVerified ? 1 : 0, sender.SendCount);
        Assert.Equal(original, reader.ConversationIdentity);
        Assert.Equal(0, reader.TextReads);
    }

    private sealed class FakeWorkNavigator(FakeReader reader, IReadOnlyList<ConversationTargetIdentity> identities, bool selectionVerified = true) : IWorkNavigator
    {
        public int Visits { get; private set; }
        public IWorkVisit? TryVisit(TargetWindow window, WorkSelectionRecord work, CancellationToken cancellationToken)
        {
            var identity = identities.SingleOrDefault(i => JsonWorkSelectionStore.BuildHash(i) == work.ConversationIdentityHash);
            if (identity is null) return null;
            Visits++;
            var original = reader.ConversationIdentity;
            reader.ConversationIdentity = identity;
            return new FakeVisit(reader, original, identity, selectionVerified);
        }
        private sealed class FakeVisit(FakeReader reader, ConversationTargetIdentity? original, ConversationTargetIdentity identity, bool selectionVerified) : IWorkVisit
        {
            public ConversationTargetIdentity Identity => identity;
            public bool IsCurrent => reader.ConversationIdentity == identity;
            public bool SelectionIdentityVerified => selectionVerified;
            public void Dispose() => reader.ConversationIdentity = original;
        }
    }

    private sealed class MutableCompletion : IWorkCompletionProvider
    {
        public AssistantCompletionEvidence Evidence { get; set; } = new("reply-a", true, FooterPresence.Absent, "INCOMPLETE_REPLY");
        public Func<AssistantCompletionEvidence>? ReadOverride { get; set; }
        public int ReadCount { get; private set; }
        public AssistantCompletionEvidence Read(TargetWindow target, ConversationTargetIdentity identity, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return ReadOverride?.Invoke() ?? Evidence;
        }
    }

    private (MonitorService Service, FakeSender Sender, MutableAccountQuota Quota, MutableCompletion Completion,
        FakeWorkSelectionStore Selections) CompletionService(ResumePolicy policy = ResumePolicy.AutomaticForegroundResume,
            InMemoryEventStore? store = null, int delaySeconds = 0)
    {
        var identity = Identity("work-a");
        var quota = new MutableAccountQuota { Snapshot = new(28, _now.AddHours(1), 42, _now.AddDays(1), _now) };
        var completion = new MutableCompletion();
        var sender = new FakeSender();
        var selections = new FakeWorkSelectionStore([identity]);
        var service = new MonitorService(new AppConfig { AutoResume = true, ResumeDelaySeconds = delaySeconds,
            RequireWorkSelection = false, ResumePolicy = policy },
            new FakeScanner([new(10, 100, "ChatGPT", "ChatGPT")]), new FakeReader([]) { ConversationIdentity = identity },
            sender, store ?? new InMemoryEventStore(), new NullPossibleLimitDiagnosticStore(), selections,
            TestCatalog(), new RetryTimeParser(), accountQuotaProvider: quota, workCompletionProvider: completion);
        return (service, sender, quota, completion, selections);
    }

    [Fact]
    public void RapidPollingDoesNotRestartTheSafetyDelay()
    {
        var h = CompletionService(delaySeconds: 30);
        for (var seconds = 0; seconds <= 40; seconds++) h.Service.Tick(_now.AddSeconds(seconds));
        Assert.Equal(1, h.Sender.SendCount);
    }

    [Fact]
    public void PauseInvalidatesConfirmationAndPreInputVerification()
    {
        var h = CompletionService();
        h.Service.Tick(_now);
        h.Service.Pause();
        h.Service.Start();
        h.Service.Tick(_now.AddSeconds(10));
        Assert.Equal(0, h.Sender.SendCount);
        var reads = 0;
        h.Completion.ReadOverride = () =>
        {
            if (++reads == 2) h.Service.Pause();
            return h.Completion.Evidence;
        };
        h.Service.Tick(_now.AddSeconds(20));
        Assert.Equal(0, h.Sender.SendCount);
    }

    [Fact]
    public void CancelledCompletionScanCannotClaimOrSend()
    {
        var h = CompletionService();
        h.Service.Tick(_now);
        using var source = new CancellationTokenSource();
        h.Completion.ReadOverride = () => { source.Cancel(); return h.Completion.Evidence; };
        Assert.Throws<OperationCanceledException>(() => h.Service.Tick(_now.AddSeconds(10), source.Token));
        Assert.Equal(0, h.Sender.SendCount);
    }

    [Fact]
    public void PreviouslyAttemptedWindowDoesNotStarveAnotherSelectedWork()
    {
        var store = new InMemoryEventStore();
        var first = CompletionService(store: store);
        first.Service.Tick(_now);
        first.Service.Tick(_now.AddSeconds(10));
        var a = Identity("work-a");
        var b = Identity("work-b");
        var reader = new FakeReader([]) { IdentityByHandle = new() { [100] = a, [200] = b } };
        var sender = new FakeSender();
        var service = new MonitorService(new AppConfig { AutoResume = true, ResumeDelaySeconds = 0,
                ResumePolicy = ResumePolicy.AutomaticForegroundResume },
            new FakeScanner([new(10, 100, "ChatGPT A", "ChatGPT"), new(20, 200, "ChatGPT B", "ChatGPT")]),
            reader, sender, store, new NullPossibleLimitDiagnosticStore(), new FakeWorkSelectionStore([a, b]),
            TestCatalog(), new RetryTimeParser(), accountQuotaProvider: first.Quota, workCompletionProvider: first.Completion);
        service.Tick(_now.AddSeconds(20));
        service.Tick(_now.AddSeconds(30));
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(200, sender.LastTarget?.Handle);
    }

    [Theory]
    [InlineData(false, FooterPresence.Absent)]
    [InlineData(null, FooterPresence.Absent)]
    [InlineData(true, FooterPresence.Present)]
    [InlineData(true, FooterPresence.Unknown)]
    public void RunningCompletedOrUnknownWorkNeverReachesSender(bool? stopped, FooterPresence footer)
    {
        var h = CompletionService();
        h.Completion.Evidence = new("reply-a", stopped, footer, "TEST");
        h.Service.Tick(_now);
        h.Service.Tick(_now.AddSeconds(10));
        Assert.Equal(0, h.Sender.SendCount);
        Assert.NotEqual(AppState.ReadyToResume, h.Service.State);
    }

    [Fact]
    public void ChangedReplyRequiresTwoNewObservations()
    {
        var h = CompletionService();
        h.Service.Tick(_now);
        h.Completion.Evidence = h.Completion.Evidence with { AssistantReplyIdentity = "reply-b" };
        h.Service.Tick(_now.AddSeconds(10));
        Assert.Equal(0, h.Sender.SendCount);
        h.Service.Tick(_now.AddSeconds(20));
        Assert.Equal(1, h.Sender.SendCount);
    }

    [Fact]
    public void UnselectedWorkIsBlockedEvenWhenLegacySelectionRequirementDisabled()
    {
        var h = CompletionService();
        h.Service.Tick(_now);
        h.Selections.SetEnabled(JsonWorkSelectionStore.BuildHash(Identity("work-a")), false);
        h.Service.Tick(_now.AddSeconds(10));
        Assert.Equal(0, h.Sender.SendCount);
        h.Selections.SetEnabled(JsonWorkSelectionStore.BuildHash(Identity("work-a")), true);
        h.Service.Tick(_now.AddSeconds(20));
        Assert.Equal(0, h.Sender.SendCount);
        h.Service.Tick(_now.AddSeconds(30));
        Assert.Equal(1, h.Sender.SendCount);
    }

    [Fact]
    public void QuotaRecoveryStartsConfirmationRatherThanSendingImmediately()
    {
        var h = CompletionService();
        h.Quota.Snapshot = h.Quota.Snapshot! with { ShortWindowRemainingPercent = 0, ShortWindowResetAt = _now.AddSeconds(5) };
        h.Service.Tick(_now);
        Assert.Equal(AppState.WaitingForRetry, h.Service.State);
        h.Quota.Snapshot = h.Quota.Snapshot with { ShortWindowRemainingPercent = 30, CapturedAt = _now.AddSeconds(10) };
        h.Service.Tick(_now.AddSeconds(10));
        Assert.Equal(0, h.Sender.SendCount);
        h.Service.Tick(_now.AddSeconds(20));
        Assert.Equal(1, h.Sender.SendCount);
    }

    [Fact]
    public void StaleQuotaCannotQualifyIncompleteWork()
    {
        var h = CompletionService();
        h.Quota.Snapshot = h.Quota.Snapshot! with { CapturedAt = _now.AddMinutes(-5) };
        h.Service.Tick(_now);
        h.Service.Tick(_now.AddSeconds(10));
        Assert.Equal(0, h.Sender.SendCount);
    }

    [Fact]
    public void FooterAppearingDuringPreInputCheckPreventsClaimAndSend()
    {
        var h = CompletionService();
        var count = 0;
        h.Completion.ReadOverride = () => ++count < 3
            ? new("reply-a", true, FooterPresence.Absent, "INCOMPLETE")
            : new("reply-a", true, FooterPresence.Present, "COMPLETED");
        h.Service.Tick(_now);
        h.Service.Tick(_now.AddSeconds(10));
        Assert.Equal(0, h.Sender.SendCount);
        Assert.Equal(AppState.Error, h.Service.State);
    }

    [Fact]
    public void ConfirmationRevalidatesFooterAndNotifyOnlyCannotSend()
    {
        var h = CompletionService(ResumePolicy.ConfirmBeforeResume);
        h.Service.Tick(_now);
        h.Service.Tick(_now.AddSeconds(10));
        Assert.Equal(AppState.ReadyToResume, h.Service.State);
        h.Completion.Evidence = h.Completion.Evidence with { Footer = FooterPresence.Present };
        h.Service.ConfirmResume(_now.AddSeconds(11));
        Assert.Equal(0, h.Sender.SendCount);
        var n = CompletionService(ResumePolicy.NotifyOnly);
        n.Service.Tick(_now);
        n.Service.Tick(_now.AddSeconds(10));
        n.Service.ConfirmResume(_now.AddSeconds(11));
        Assert.Equal(0, n.Sender.SendCount);
    }

    [Fact]
    public void SameIncompleteReplyAfterRestartCannotSendAgain()
    {
        var store = new InMemoryEventStore();
        var a = CompletionService(store: store);
        a.Service.Tick(_now);
        a.Service.Tick(_now.AddSeconds(10));
        Assert.Equal(1, a.Sender.SendCount);
        var b = CompletionService(store: store);
        b.Service.Tick(_now.AddSeconds(20));
        b.Service.Tick(_now.AddSeconds(30));
        Assert.Equal(0, b.Sender.SendCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AccountSourceNeverUsesStaleConversationEvenWhenAccountReadFails(bool available)
    {
        var quota = new MutableAccountQuota { Snapshot = available ? new(28, _now.AddHours(1), 42, _now.AddDays(1), _now) : null };
        var reader = new FakeReader(new() { [100] = "You've reached your usage limit. Try again after Aug 31, 2026 at 7:01 PM!" });
        var service = new MonitorService(new AppConfig(), new FakeScanner([new(10, 100, "ChatGPT", "ChatGPT")]),
            reader, new FakeSender(), new InMemoryEventStore(), new NullPossibleLimitDiagnosticStore(),
            new AllowAllWorkSelectionStore(), TestCatalog(), new RetryTimeParser(), accountQuotaProvider: quota);
        service.Tick(_now);
        Assert.Equal(AppState.Monitoring, service.State);
        Assert.Null(service.RetryAt);
        Assert.Equal(0, reader.TextReads);
    }

    [Fact]
    public void AccountResetRechecksAndBlocksUnknownThenWaitsForNewReset()
    {
        var quota = new MutableAccountQuota { Snapshot = new(0, _now.AddMinutes(1), 42, _now.AddDays(1), _now) };
        var sender = new FakeSender();
        var reader = new FakeReader([]) { ConversationIdentity = Identity("work-a") };
        var service = new MonitorService(new AppConfig { AutoResume = true, ResumePolicy = ResumePolicy.AutomaticForegroundResume },
            new FakeScanner([new(10, 100, "ChatGPT", "ChatGPT")]), reader, sender, new InMemoryEventStore(),
            new NullPossibleLimitDiagnosticStore(), new AllowAllWorkSelectionStore(), TestCatalog(), new RetryTimeParser(), accountQuotaProvider: quota);
        service.Tick(_now);
        Assert.Equal(AppState.WaitingForRetry, service.State);
        quota.Snapshot = null;
        service.Tick(_now.AddMinutes(2));
        Assert.Equal(0, sender.SendCount);
        Assert.True(quota.Forced);
        quota.Snapshot = new(0, _now.AddHours(2), 42, _now.AddDays(1), _now.AddMinutes(3));
        service.Tick(_now.AddMinutes(3));
        Assert.Equal(AppState.WaitingForRetry, service.State);
        Assert.Equal(_now.AddHours(2), service.RetryAt);
        Assert.Equal(0, reader.TextReads);
    }

    private sealed class MutableAccountQuota : IAccountQuotaProvider
    {
        public QuotaSnapshot? Snapshot { get; set; }
        public bool Forced { get; private set; }
        public QuotaSnapshot? Read(bool forceRefresh = false, CancellationToken cancellationToken = default)
        { Forced |= forceRefresh; return Snapshot; }
    }

    [Fact]
    public void TraceDistinguishesDetectedWaitingAndSafetyBlockedWithoutConversationText()
    {
        var target = new TargetWindow(10, 100, "private title", "ChatGPT");
        var trace = new CapturingTrace();
        var config = new AppConfig { DryRun = true, SendEnter = false, AllowRealSubmit = false,
            RequireWorkSelection = false, ResumePolicy = ResumePolicy.AutomaticForegroundResume };
        var service = new MonitorService(config, new FakeScanner([target]), new FakeReader(new()
        { [100] = "You've reached your usage limit. Try again after Aug 31, 2026 at 7:01 PM! private body" }),
            new FakeSender(), new InMemoryEventStore(), new NullPossibleLimitDiagnosticStore(),
            new AllowAllWorkSelectionStore(), TestCatalog(), new RetryTimeParser(), trace);
        service.Tick(_now);
        Assert.Equal(AppState.WaitingForRetry, service.State);
        service.Tick(_now.AddMinutes(2));
        Assert.Contains(trace.Entries, x => x.Stage == "WAITING_FOR_RETRY" && x.RetryAt is not null);
        Assert.Contains(trace.Entries, x => x.Stage == "PRE_INPUT_CLAIM" && x.Outcome == "PASS");
        Assert.Contains(trace.Entries, x => x.Stage == "INPUT" && x.Outcome == "SAFETY BLOCKED");
        Assert.Contains(trace.Entries, x => x.Stage == "ENTER" && x.Outcome == "SAFETY BLOCKED");
        Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(trace.Entries));
    }

    private sealed class CapturingTrace : IProductionEventLog
    {
        public List<ProductionTrace> Entries { get; } = [];
        public void Write(ProductionTrace entry) => Entries.Add(entry);
    }

    [Fact]
    public void InsertionWithoutEnterDoesNotPersistASubmittedResult()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var store = new RecordingEventStore();
        var config = new AppConfig { DryRun = false, SendEnter = false, AllowRealSubmit = false,
            RequireWorkSelection = false, ResumePolicy = ResumePolicy.AutomaticForegroundResume };
        var service = new MonitorService(config, new FakeScanner([target]), new FakeReader(new()
        { [100] = "You've reached your usage limit. Try again after Aug 31, 2026 at 7:01 PM!" }),
            new FakeSender(), store, TestCatalog(), new RetryTimeParser());
        service.Tick(_now);
        service.Tick(_now.AddMinutes(2));
        Assert.Single(store.Results);
        Assert.False(store.Results[0]);
    }

    private sealed class RecordingEventStore : IEventStore
    {
        public List<bool> Results { get; } = [];
        public bool HasResumeAttempt(string eventId) => Results.Count > 0;
        public void MarkResumeAttempted(string eventId, DateTimeOffset attemptedAt, bool sent) => Results.Add(sent);
    }

    [Fact]
    public void NoTargetEntersWaitingForTargetState()
    {
        var service = NewService(new MutableScanner([]), new FakeReader([]), new FakeSender());

        service.Tick(_now);

        Assert.Equal(AppState.WaitingForTarget, service.State);
        Assert.Null(service.Target);
    }

    [Fact]
    public void TargetAppearingAfterNoTargetRunsDetectorImmediately()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var scanner = new MutableScanner([]);
        var reader = new FakeReader(new Dictionary<nint, string>
        {
            [100] = "You've reached your usage limit. Try again after Aug 31, 2026 at 7:01 PM!"
        });
        var service = NewService(scanner, reader, new FakeSender());

        service.Tick(_now);
        scanner.Targets = [target];
        service.Tick(_now.AddSeconds(10));

        Assert.Equal(AppState.WaitingForRetry, service.State);
        Assert.Equal(100, service.Target?.Handle);
    }

    [Fact]
    public void TargetDisappearingClearsRuntimeTarget()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var scanner = new MutableScanner([target]);
        var service = NewService(scanner, new FakeReader(new Dictionary<nint, string>
        {
            [100] = "Normal conversation"
        }), new FakeSender());

        service.Tick(_now);
        scanner.Targets = [];
        service.Tick(_now.AddSeconds(10));

        Assert.Equal(AppState.WaitingForTarget, service.State);
        Assert.Null(service.Target);
    }

    [Fact]
    public void DuplicateTimerFiringOnlySendsOnce()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var scanner = new FakeScanner([target]);
        var reader = new FakeReader(new Dictionary<nint, string>
        {
            [100] = "你已達使用上限，請於晚上 7:01 再試一次!"
        });
        var sender = new FakeSender();
        var service = NewService(scanner, reader, sender);

        service.Tick(_now);
        service.Tick(_now.AddMinutes(1).AddSeconds(31));
        service.Tick(_now.AddMinutes(1).AddSeconds(40));

        Assert.Equal(1, sender.SendCount);
        Assert.Equal(100, sender.LastTarget?.Handle);
    }

    [Fact]
    public void RestartWithAttemptedEventBlocksSecondSend()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var store = new InMemoryEventStore();

        var firstSender = new FakeSender();
        var first = NewService(
            new FakeScanner([target]),
            new FakeReader(new Dictionary<nint, string> { [100] = "你已達使用上限，請於晚上 7:01 再試一次!" }),
            firstSender,
            store);
        first.Tick(_now);
        first.Tick(_now.AddMinutes(1).AddSeconds(31));

        var secondSender = new FakeSender();
        var second = NewService(
            new FakeScanner([target]),
            new FakeReader(new Dictionary<nint, string> { [100] = "你已達使用上限，請於晚上 7:01 再試一次!" }),
            secondSender,
            store);
        second.Tick(_now.AddSeconds(10));
        second.Tick(_now.AddMinutes(1).AddSeconds(31));

        Assert.Equal(1, firstSender.SendCount);
        Assert.Equal(0, secondSender.SendCount);
    }

    [Fact]
    public void MultiWindowResumeUsesOriginalLimitWindow()
    {
        var windowA = new TargetWindow(10, 100, "ChatGPT A", "ChatGPT");
        var windowB = new TargetWindow(11, 200, "ChatGPT B", "ChatGPT");
        var scanner = new FakeScanner([windowA, windowB]);
        var reader = new FakeReader(new Dictionary<nint, string>
        {
            [100] = "Normal conversation",
            [200] = "You've reached your usage limit. Try again after Aug 31, 2026 at 7:01 PM!"
        });
        var sender = new FakeSender();
        var service = NewService(scanner, reader, sender);

        service.Tick(_now);
        service.Tick(_now.AddMinutes(1).AddSeconds(31));

        Assert.Equal(1, sender.SendCount);
        Assert.Equal(200, sender.LastTarget?.Handle);
    }

    [Fact]
    public void HwndAndPidCanChangeBetweenDiscoveryCycles()
    {
        var firstWindow = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var secondWindow = new TargetWindow(20, 200, "ChatGPT", "ChatGPT");
        var scanner = new MutableScanner([firstWindow]);
        var reader = new FakeReader(new Dictionary<nint, string>
        {
            [100] = "Normal conversation",
            [200] = "You've reached your usage limit. Try again after Aug 31, 2026 at 7:01 PM!"
        });
        var sender = new FakeSender();
        var service = NewService(scanner, reader, sender);

        service.Tick(_now);
        scanner.Targets = [secondWindow];
        service.Tick(_now.AddSeconds(5));
        service.Tick(_now.AddMinutes(1).AddSeconds(31));

        Assert.Equal(1, sender.SendCount);
        Assert.Equal(20, sender.LastTarget?.ProcessId);
        Assert.Equal(200, sender.LastTarget?.Handle);
    }

    [Fact]
    public void FailedInputVerificationAbortsResume()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var sender = new FakeSender { VerifyResult = false, SendResult = false };
        var service = NewService(
            new FakeScanner([target]),
            new FakeReader(new Dictionary<nint, string> { [100] = "You've reached your usage limit. Try again after Aug 31, 2026 at 7:01 PM!" }),
            sender);

        service.Tick(_now);
        service.Tick(_now.AddMinutes(1).AddSeconds(31));

        Assert.Equal(AppState.Error, service.State);
        Assert.Equal(0, sender.SendCount);
    }

    [Fact]
    public void LimitWithoutRetryTimeEntersDetectedStateWithoutResume()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var sender = new FakeSender();
        var diagnostics = new FakePossibleLimitDiagnostics();
        var service = NewService(
            new FakeScanner([target]),
            new FakeReader(new Dictionary<nint, string>
            {
                [100] = "You're out of Codex and Work usage. Add credits or upgrade your plan."
            }),
            sender,
            possibleLimitDiagnostics: diagnostics);

        service.Tick(_now);

        Assert.Equal(AppState.LimitDetected, service.State);
        Assert.Equal(0, sender.SendCount);
        Assert.Single(diagnostics.Records);
        Assert.Equal("limit_detected_no_retry_datetime", diagnostics.Records[0].RejectionReason);
    }

    [Fact]
    public void DeepLimitSurfaceBannerCanCreateWaitingEventWhenVisibleTextIsTooShallow()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var service = NewService(
            new FakeScanner([target]),
            new FakeReader(new Dictionary<nint, string> { [100] = "ChatGPT shell text only" }),
            new FakeSender(),
            limitSurfaceProvider: new FakeLimitSurfaceProvider([
                Candidate("你的 Codex 和工作使用量已用完。新增點數或升級方案，或等到 清晨7:28 用量重置", LimitSurfaceKind.Banner, depth: 30)
            ]));

        service.Tick(_now);

        Assert.Equal(AppState.WaitingForRetry, service.State);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 7, 28, 0, TimeSpan.FromHours(8)), service.RetryAt);
    }

    [Theory]
    [InlineData(LimitSurfaceKind.ConversationBody)]
    [InlineData(LimitSurfaceKind.Composer)]
    [InlineData(LimitSurfaceKind.Sidebar)]
    [InlineData(LimitSurfaceKind.Offscreen)]
    [InlineData(LimitSurfaceKind.UnrelatedProcess)]
    public void DeepLimitSurfaceRejectsUnsafeContexts(LimitSurfaceKind kind)
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var diagnostics = new FakePossibleLimitDiagnostics();
        var candidate = Candidate(
            "你的 Codex 和工作使用量已用完。新增點數或升級方案，或等到 清晨7:28 用量重置",
            kind,
            depth: 30) with { IsOffscreen = kind == LimitSurfaceKind.Offscreen };
        var service = NewService(
            new FakeScanner([target]),
            new FakeReader(new Dictionary<nint, string> { [100] = "ChatGPT shell text only" }),
            new FakeSender(),
            possibleLimitDiagnostics: diagnostics,
            limitSurfaceProvider: new FakeLimitSurfaceProvider([candidate]));

        service.Tick(_now);

        Assert.Equal(AppState.Monitoring, service.State);
        Assert.Null(service.RetryAt);
        Assert.Single(diagnostics.Records);
        Assert.Contains($"limit_surface_rejected:{kind}", diagnostics.Records[0].RejectionReason);
    }

    [Fact]
    public void EligibleLimitSurfaceStillCapturesDiagnosticWhenDetectorRejectsIt()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var diagnostics = new FakePossibleLimitDiagnostics();
        var service = NewService(
            new FakeScanner([target]),
            new FakeReader(new Dictionary<nint, string> { [100] = "ChatGPT shell text only" }),
            new FakeSender(),
            possibleLimitDiagnostics: diagnostics,
            limitSurfaceProvider: new FakeLimitSurfaceProvider([
                Candidate("Please try again after 清晨7:28.", LimitSurfaceKind.Banner, depth: 30)
            ]));

        service.Tick(_now);

        Assert.Equal(AppState.Monitoring, service.State);
        Assert.Single(diagnostics.Records);
        Assert.Contains("limit_surface_detector_rejected", diagnostics.Records[0].RejectionReason);
    }

    [Fact]
    public void SameHwndWrongConversationBlocksResume()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var reader = new FakeReader(new Dictionary<nint, string>
        {
            [100] = "You've reached your usage limit. Try again after Aug 31, 2026 at 7:01 PM!"
        })
        {
            ConversationIdentity = Identity("conversation-b")
        };
        var sender = new FakeSender();
        var service = NewService(new FakeScanner([target]), reader, sender);

        service.Tick(_now);
        reader.ConversationIdentity = Identity("conversation-a");
        service.Tick(_now.AddMinutes(1).AddSeconds(31));

        Assert.Equal(AppState.Error, service.State);
        Assert.Equal(0, sender.SendCount);
    }

    [Fact]
    public void ConfirmBeforeResumeDoesNotSendUntilConfirmed()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var sender = new FakeSender();
        var service = NewService(
            new FakeScanner([target]),
            new FakeReader(new Dictionary<nint, string> { [100] = "You've reached your usage limit. Try again after Aug 31, 2026 at 7:01 PM!" }),
            sender,
            resumePolicy: ResumePolicy.ConfirmBeforeResume);

        service.Tick(_now);
        service.Tick(_now.AddMinutes(1).AddSeconds(31));

        Assert.Equal(AppState.ReadyToResume, service.State);
        Assert.Equal(0, sender.SendCount);

        service.ConfirmResume(_now.AddMinutes(1).AddSeconds(32));

        Assert.Equal(1, sender.SendCount);
        Assert.Equal(AppState.Verifying, service.State);
    }

    [Fact]
    public void UncheckingWorkWhileWaitingBlocksResume()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var identity = Identity("selected-work");
        var reader = new FakeReader(new()
        {
            [100] = "You've reached your usage limit. Try again after Aug 31, 2026 at 7:01 PM!"
        }) { ConversationIdentity = identity };
        var selections = new FakeWorkSelectionStore([identity]);
        var sender = new FakeSender();
        var service = NewService(new FakeScanner([target]), reader, sender,
            workSelectionStore: selections, requireWorkSelection: true);

        service.Tick(_now);
        selections.SetEnabled(JsonWorkSelectionStore.BuildHash(identity), false);
        service.Tick(_now.AddMinutes(2));

        Assert.Equal(0, sender.SendCount);
        Assert.Equal(AppState.Error, service.State);
    }

    [Fact]
    public void EventStoreWriteFailurePreventsInput()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var sender = new FakeSender();
        var service = NewService(new FakeScanner([target]), new FakeReader(new()
        {
            [100] = "You've reached your usage limit. Try again after Aug 31, 2026 at 7:01 PM!"
        }), sender, new FailingEventStore());

        service.Tick(_now);
        var error = Record.Exception(() => service.Tick(_now.AddMinutes(2)));

        Assert.Equal(0, sender.SendCount);
        Assert.Null(error);
        Assert.Equal(AppState.Error, service.State);
    }

    private sealed class FailingEventStore : IEventStore
    {
        public bool HasResumeAttempt(string eventId) => false;
        public void MarkResumeAttempted(string eventId, DateTimeOffset attemptedAt, bool sent) =>
            throw new IOException("Test: event journal unavailable");
    }

    [Fact]
    public void SelectedWorkIsEligibleForAutoResume()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var identity = Identity("selected-work");
        var reader = new FakeReader(new Dictionary<nint, string>
        {
            [100] = "You've reached your usage limit. Try again after Aug 31, 2026 at 7:01 PM!"
        })
        {
            ConversationIdentity = identity
        };
        var sender = new FakeSender();
        var service = NewService(
            new FakeScanner([target]),
            reader,
            sender,
            workSelectionStore: new FakeWorkSelectionStore([identity]),
            requireWorkSelection: true);

        service.Tick(_now);
        service.Tick(_now.AddMinutes(1).AddSeconds(31));

        Assert.Equal(1, sender.SendCount);
        Assert.Equal(AppState.Verifying, service.State);
    }

    [Fact]
    public void UnselectedWorkNeverAutoResumes()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var reader = new FakeReader(new Dictionary<nint, string>
        {
            [100] = "You've reached your usage limit. Try again after Aug 31, 2026 at 7:01 PM!"
        })
        {
            ConversationIdentity = Identity("unselected-work")
        };
        var sender = new FakeSender();
        var service = NewService(
            new FakeScanner([target]),
            reader,
            sender,
            workSelectionStore: new FakeWorkSelectionStore([]),
            requireWorkSelection: true);

        service.Tick(_now);
        service.Tick(_now.AddMinutes(1).AddSeconds(31));

        Assert.Equal(AppState.LimitDetected, service.State);
        Assert.Equal(0, sender.SendCount);
    }

    [Fact]
    public void CheckedWorkThatIsNotAnInspectableTargetDoesNotForceAttentionInActiveWorkScope()
    {
        var active = Identity("active-unselected-work");
        var checkedInactive = Identity("checked-inactive-work");
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var quota = new MutableAccountQuota { Snapshot = new(50, _now.AddHours(1), 60, _now.AddDays(1), _now) };
        var service = new MonitorService(
            new AppConfig { AutoResume = true, DryRun = true, SendEnter = false, ResumeDelaySeconds = 30,
                ResumePolicy = ResumePolicy.AutomaticForegroundResume, RequireWorkSelection = true },
            new FakeScanner([target]),
            new FakeReader([]) { ConversationIdentity = active },
            new FakeSender(),
            new InMemoryEventStore(),
            new NullPossibleLimitDiagnosticStore(),
            new FakeWorkSelectionStore([checkedInactive]),
            TestCatalog(),
            new RetryTimeParser(),
            accountQuotaProvider: quota,
            workCompletionProvider: new MutableCompletion());

        service.Tick(_now);

        Assert.Equal(AppState.Monitoring, service.State);
        Assert.Contains("Other remembered Works are not auto-switched", service.ResumeStatusText);
    }

    [Fact]
    public void AdditionalCheckedWorkOutsideInspectableTargetsDoesNotBlockActiveWorkMonitoring()
    {
        var activeChecked = Identity("active-checked-work");
        var checkedInactive = Identity("checked-inactive-work");
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var quota = new MutableAccountQuota { Snapshot = new(50, _now.AddHours(1), 60, _now.AddDays(1), _now) };
        var service = new MonitorService(
            new AppConfig { AutoResume = true, DryRun = true, SendEnter = false, ResumeDelaySeconds = 30,
                ResumePolicy = ResumePolicy.AutomaticForegroundResume, RequireWorkSelection = true },
            new FakeScanner([target]),
            new FakeReader([]) { ConversationIdentity = activeChecked },
            new FakeSender(),
            new InMemoryEventStore(),
            new NullPossibleLimitDiagnosticStore(),
            new FakeWorkSelectionStore([activeChecked, checkedInactive]),
            TestCatalog(),
            new RetryTimeParser(),
            accountQuotaProvider: quota,
            workCompletionProvider: new MutableCompletion { Evidence = new("reply-a", true, FooterPresence.Present, "COMPLETED") });

        service.Tick(_now);

        Assert.Equal(AppState.Monitoring, service.State);
        Assert.Contains("Other remembered Works are not auto-switched", service.ResumeStatusText);
    }

    [Fact]
    public void QuotaExampleInConversationCannotCreateAccountQuotaEvent()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var service = NewService(new FakeScanner([target]), new FakeReader(new()
        {
            [100] = "Example account panel:\n剩餘用量\n5 小時 0% 晚上8:15\n1 週 6% 9月7日"
        }), new FakeSender());

        service.Tick(_now);

        Assert.Equal(AppState.Monitoring, service.State);
        Assert.Null(service.RetryAt);
    }

    [Fact]
    public void AccountQuotaResetTimeTakesPriorityWhenReadable()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var service = NewService(
            new FakeScanner([target]),
            new FakeReader(new Dictionary<nint, string>
            {
                [100] =
                    """
                    你已達使用上限，請於晚上 7:01 再試一次!
                    剩餘用量
                    5 小時
                    0%
                    晚上 8:15
                    """
            }, accountQuotaPanel: true),
            new FakeSender());

        service.Tick(_now);

        Assert.Equal(new DateTimeOffset(2026, 8, 31, 20, 15, 0, TimeSpan.FromHours(8)), service.RetryAt);
    }

    [Fact]
    public void AccountQuotaZeroCreatesWaitingEventWithoutConversationWarning()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var service = NewService(
            new FakeScanner([target]),
            new FakeReader(new Dictionary<nint, string>
            {
                [100] =
                    """
                    剩餘用量
                    5 小時 0% 清晨7:28
                    1 週 6% 9月7日
                    """
            }, accountQuotaPanel: true),
            new FakeSender());

        service.Tick(_now);

        Assert.Equal(AppState.WaitingForRetry, service.State);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 7, 28, 0, TimeSpan.FromHours(8)), service.RetryAt);
        Assert.Equal("Account quota exhausted", service.UsageLimitText);
    }

    [Fact]
    public void WeeklyQuotaZeroUsesWeeklyReset()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var service = NewService(
            new FakeScanner([target]),
            new FakeReader(new Dictionary<nint, string>
            {
                [100] =
                    """
                    剩餘用量
                    5 小時 42% 清晨7:28
                    1 週 0% 9月7日
                    """
            }, accountQuotaPanel: true),
            new FakeSender());

        service.Tick(new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.FromHours(8)));

        Assert.Equal(AppState.WaitingForRetry, service.State);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.FromHours(8)), service.RetryAt);
    }

    [Fact]
    public void BothQuotaWindowsZeroUsesLaterReset()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var service = NewService(
            new FakeScanner([target]),
            new FakeReader(new Dictionary<nint, string>
            {
                [100] =
                    """
                    剩餘用量
                    5 小時 0% 清晨7:28
                    1 週 0% 9月7日
                    """
            }, accountQuotaPanel: true),
            new FakeSender());

        service.Tick(new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.FromHours(8)));

        Assert.Equal(AppState.WaitingForRetry, service.State);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.FromHours(8)), service.RetryAt);
    }

    [Fact]
    public void RechecksAccountQuotaAtRetryTimeAndDoesNotSendWhenStillExhausted()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var textByHandle = new Dictionary<nint, string>
        {
            [100] =
                """
                剩餘用量
                5 小時 0% 晚上7:01
                1 週 6% 9月7日
                """
        };
        var reader = new FakeReader(textByHandle, accountQuotaPanel: true);
        var sender = new FakeSender();
        var service = NewService(new FakeScanner([target]), reader, sender);

        service.Tick(_now);
        textByHandle[100] =
            """
            剩餘用量
            5 小時 0% 晚上8:15
            1 週 6% 9月7日
            """;
        service.Tick(_now.AddMinutes(1).AddSeconds(31));

        Assert.Equal(0, sender.SendCount);
        Assert.Equal(AppState.WaitingForRetry, service.State);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 20, 15, 0, TimeSpan.FromHours(8)), service.RetryAt);
    }

    [Fact]
    public void RecheckedAccountQuotaAllowsResumeWhenQuotaRecovered()
    {
        var target = new TargetWindow(10, 100, "ChatGPT", "ChatGPT");
        var textByHandle = new Dictionary<nint, string>
        {
            [100] =
                """
                剩餘用量
                5 小時 0% 晚上7:01
                1 週 6% 9月7日
                """
        };
        var reader = new FakeReader(textByHandle, accountQuotaPanel: true);
        var sender = new FakeSender();
        var service = NewService(new FakeScanner([target]), reader, sender);

        service.Tick(_now);
        textByHandle[100] =
            """
            剩餘用量
            5 小時 74% 晚上7:28
            1 週 6% 9月7日
            """;
        service.Tick(_now.AddMinutes(1).AddSeconds(31));

        Assert.Equal(1, sender.SendCount);
        Assert.Equal(AppState.Verifying, service.State);
    }

    [Fact]
    public void SameTitleDifferentIdentityDoesNotShareSelection()
    {
        var selected = Identity("same-title-a");
        var unselected = Identity("same-title-b");
        var store = new FakeWorkSelectionStore([selected]);

        Assert.True(store.IsAutoResumeEnabled(selected));
        Assert.False(store.IsAutoResumeEnabled(unselected));
    }

    [Fact]
    public void StaleWorkIsBlocked()
    {
        var stale = Identity("stale-work");
        var store = new FakeWorkSelectionStore([stale], staleIdentities: [stale]);

        Assert.False(store.IsAutoResumeEnabled(stale));
    }

    private static MonitorService NewService(
        IWindowScanner scanner,
        IUiAutomationReader reader,
        IResumeSender sender,
        IEventStore? store = null,
        IPossibleLimitDiagnosticStore? possibleLimitDiagnostics = null,
        ResumePolicy resumePolicy = ResumePolicy.AutomaticForegroundResume,
        IWorkSelectionStore? workSelectionStore = null,
        bool requireWorkSelection = false,
        ILimitSurfaceProvider? limitSurfaceProvider = null)
    {
        var config = new AppConfig
        {
            AutoResume = true,
            DryRun = true,
            SendEnter = false,
            ResumeDelaySeconds = 30,
            ResumePolicy = resumePolicy,
            RequireWorkSelection = requireWorkSelection
        };

        return new MonitorService(config, scanner, reader, sender, store ?? new InMemoryEventStore(), possibleLimitDiagnostics ?? new NullPossibleLimitDiagnosticStore(), workSelectionStore ?? new AllowAllWorkSelectionStore(), TestCatalog(), new RetryTimeParser(), limitSurfaceProvider: limitSurfaceProvider);
    }

    private static LimitSurfaceCandidate Candidate(string text, LimitSurfaceKind kind, int depth, int confidence = 100) => new(
        Text: text,
        Kind: kind,
        HasWarningRole: kind is LimitSurfaceKind.Banner or LimitSurfaceKind.Alert or LimitSurfaceKind.Status,
        HasWarningIcon: false,
        Confidence: confidence,
        ControlType: "ControlType.Text",
        AutomationId: "",
        ClassName: "",
        Depth: depth,
        IsOffscreen: false,
        Bounds: "300,500,700,620",
        ParentControlType: "ControlType.Group",
        GrandparentControlType: "ControlType.Pane",
        SiblingSummary: "升級方案 | 新增點數");

    private static PatternCatalog TestCatalog() => new()
    {
        Languages =
        [
            new LanguagePattern
            {
                Language = "zh-TW",
                UsageLimit =
                [
                    new PatternEntry { Id = "zh_tw.usage_limit", Text = "使用上限" },
                    new PatternEntry { Id = "zh_tw.usage_exhausted", Text = "使用量已用完" },
                    new PatternEntry { Id = "zh_tw.codex_work_usage_exhausted", Text = "Codex 和工作使用量已用完" }
                ],
                Retry =
                [
                    new PatternEntry { Id = "zh_tw.try_again", Text = "再試一次" },
                    new PatternEntry { Id = "zh_tw.reset", Text = "重置" }
                ]
            },
            new LanguagePattern
            {
                Language = "en",
                UsageLimit = [new PatternEntry { Id = "en.usage_limit", Text = "usage limit" }],
                Retry = [new PatternEntry { Id = "en.try_again", Text = "try again" }]
            }
        ]
    };

    private sealed class FakeScanner(IReadOnlyList<TargetWindow> targets) : IWindowScanner
    {
        public IReadOnlyList<TargetWindow> FindTargets() => targets;
        public bool IsStillValid(TargetWindow target) => targets.Any(t => t == target);
    }

    private sealed class MutableScanner(IReadOnlyList<TargetWindow> targets) : IWindowScanner
    {
        public IReadOnlyList<TargetWindow> Targets { get; set; } = targets;
        public IReadOnlyList<TargetWindow> FindTargets() => Targets;
        public bool IsStillValid(TargetWindow target) => Targets.Any(t => t == target);
    }

    private sealed class FakeReader(Dictionary<nint, string> textByHandle, bool accountQuotaPanel = false) : IUiAutomationReader, IConversationIdentityProvider, IActiveWorkTitleProvider, IAccountQuotaReader
    {
        public int TextReads { get; private set; }
        public ConversationTargetIdentity? ConversationIdentity { get; set; }
        public string ActiveTitle { get; set; } = "";
        public Dictionary<nint, ConversationTargetIdentity> IdentityByHandle { get; set; } = [];

        public string ReadVisibleText(nint hwnd, out bool hasWarningRole)
        {
            TextReads++;
            hasWarningRole = true;
            return textByHandle.TryGetValue(hwnd, out var text) ? text : "";
        }

        public AutomationElement? FindChatInput(nint hwnd) => null;
        public string ReadAccountQuotaSurfaceText(nint hwnd) =>
            accountQuotaPanel && textByHandle.TryGetValue(hwnd, out var text) ? text : "";
        public string DumpTree(nint hwnd) => "";
        public string DescribeElement(AutomationElement? element) => "";
        public ConversationTargetIdentity? CaptureConversationIdentity(nint hwnd) => IdentityByHandle.GetValueOrDefault(hwnd) ?? ConversationIdentity;
        public bool IsConversationStillActive(nint hwnd, ConversationTargetIdentity identity) => CaptureConversationIdentity(hwnd) == identity;
        public string GetActiveWorkDisplayName(nint hwnd) => ActiveTitle;
    }

    private sealed class FakeSender : IResumeSender
    {
        public int SendCount { get; private set; }
        public TargetWindow? LastTarget { get; private set; }
        public bool VerifyResult { get; set; } = true;
        public bool SendResult { get; set; } = true;
        public bool VerifyTarget(TargetWindow target) => VerifyResult;

        public bool TrySend(TargetWindow target, string text, bool dryRun, bool sendEnter)
        {
            if (!VerifyResult)
            {
                return false;
            }

            SendCount++;
            LastTarget = target;
            return SendResult;
        }
    }

    private sealed class FakePossibleLimitDiagnostics : IPossibleLimitDiagnosticStore
    {
        public List<(TargetWindow Window, string RejectionReason)> Records { get; } = [];

        public void Save(TargetWindow window, string visibleText, DetectionResult result, bool hasWarningRole, string rejectionReason, DateTimeOffset capturedAt)
        {
            Records.Add((window, rejectionReason));
        }
    }

    private sealed class FakeLimitSurfaceProvider(IReadOnlyList<LimitSurfaceCandidate> candidates) : ILimitSurfaceProvider
    {
        public IReadOnlyList<LimitSurfaceCandidate> FindLimitSurfaces(TargetWindow target) => candidates;
    }

    private sealed class FakeWorkSelectionStore(
        IReadOnlyList<ConversationTargetIdentity> enabledIdentities,
        IReadOnlyList<ConversationTargetIdentity>? staleIdentities = null) : IWorkSelectionStore
    {
        public string Title { get; init; } = "Fake Work";
        private readonly HashSet<string> _enabled = enabledIdentities.Select(JsonWorkSelectionStore.BuildHash).ToHashSet(StringComparer.Ordinal);
        private readonly HashSet<string> _stale = (staleIdentities ?? []).Select(JsonWorkSelectionStore.BuildHash).ToHashSet(StringComparer.Ordinal);

        public IReadOnlyList<WorkSelectionRecord> Load() =>
            _enabled.Concat(_stale).Distinct(StringComparer.Ordinal).Select(hash => new WorkSelectionRecord(
                hash,
                Title,
                _enabled.Contains(hash),
                DateTimeOffset.Now,
                IsStale: _stale.Contains(hash))).ToList();
        public void Save(IReadOnlyList<WorkSelectionRecord> records) { }
        public void UpsertRecent(string displayTitle, ConversationTargetIdentity identity, DateTimeOffset seenAt)
        {
            var hash = JsonWorkSelectionStore.BuildHash(identity);
            if (_enabled.Count == 1 && !_enabled.Contains(hash)
                && string.Equals(displayTitle, Title, StringComparison.Ordinal))
            {
                foreach (var existing in _enabled) _stale.Add(existing);
                _enabled.Clear();
                _enabled.Add(hash);
            }
        }
        public void RenameDisplayName(string conversationIdentityHash, string displayTitle) { }
        public void SetEnabled(string conversationIdentityHash, bool enabled)
        {
            if (enabled) _enabled.Add(conversationIdentityHash);
            else _enabled.Remove(conversationIdentityHash);
        }

        public bool IsAutoResumeEnabled(ConversationTargetIdentity? identity)
        {
            if (identity is null)
            {
                return false;
            }

            var hash = JsonWorkSelectionStore.BuildHash(identity);
            return _enabled.Contains(hash) && !_stale.Contains(hash);
        }
    }

    private static ConversationTargetIdentity Identity(string value) => new(
        SurfaceType: "ControlType.Document",
        ConversationTitleHash: ConversationTargetIdentity.Hash(value),
        SelectedItemIdentity: "",
        ContainerAutomationId: "RootWebArea",
        ContainerNameHash: "",
        ComposerIdentity: ConversationTargetIdentity.Hash("composer"));
}

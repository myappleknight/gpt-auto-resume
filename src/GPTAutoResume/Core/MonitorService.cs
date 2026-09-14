using GPTAutoResume.Automation;

namespace GPTAutoResume.Core;

public sealed class MonitorService
{
    private readonly AppConfig _config;
    private readonly IWindowScanner _scanner;
    private readonly IUiAutomationReader _reader;
    private readonly IResumeSender _sender;
    private readonly UsageLimitDetector _detector;
    private readonly RetryTimeParser _timeParser;
    private readonly IEventStore _eventStore;
    private readonly IPossibleLimitDiagnosticStore _possibleLimitDiagnostics;
    private readonly IWorkSelectionStore _workSelectionStore;
    private readonly ILimitSurfaceProvider _limitSurfaceProvider;
    private readonly IAccountQuotaProvider? _accountQuotaProvider;
    private readonly IWorkCompletionProvider? _workCompletionProvider;
    private readonly IWorkNavigator? _workNavigator;
    private readonly IncompleteWorkGate _incompleteWorkGate = new();
    private DateTimeOffset? _completionValidatedAt;
    private readonly System.Diagnostics.Stopwatch _operationClock = new();
    private DateTimeOffset _operationStartedAt;
    private CancellationToken _operationCancellation;
    private volatile bool _pauseRequested;
    private volatile bool _completionResetRequested;
    private DateTimeOffset OperationNow => _operationStartedAt + _operationClock.Elapsed;
    public QuotaSnapshot? AccountQuota { get; private set; }
    private LimitEvent? _currentEvent;
    private DateTimeOffset? _verifyUntil;
    private readonly IProductionEventLog _trace;
    private string _completionValidationReason = "";

    public AppState State { get; private set; } = AppState.Monitoring;
    public DateTimeOffset? RetryAt => _currentEvent?.RetryAt;
    public TargetWindow? Target { get; private set; }
    public string UsageLimitText { get; private set; } = "Not detected";
    public string ResumeStatusText { get; private set; } = "";
    public DateTimeOffset LastCheck { get; private set; }

    public MonitorService(AppConfig config)
        : this(
            config,
            new WindowScanner(),
            new UiAutomationReader(),
            new JsonEventStore(),
            new JsonPossibleLimitDiagnosticStore(),
            PatternCatalog.LoadDefault(),
            new RetryTimeParser())
    {
    }

    public MonitorService(
        AppConfig config,
        IWindowScanner scanner,
        IUiAutomationReader reader,
        IEventStore eventStore,
        PatternCatalog catalog,
        RetryTimeParser timeParser)
        : this(config, scanner, reader, new ResumeSender(scanner, reader), eventStore, new NullPossibleLimitDiagnosticStore(), new AllowAllWorkSelectionStore(), catalog, timeParser)
    {
    }

    public MonitorService(
        AppConfig config,
        IWindowScanner scanner,
        IUiAutomationReader reader,
        IEventStore eventStore,
        IPossibleLimitDiagnosticStore possibleLimitDiagnostics,
        PatternCatalog catalog,
        RetryTimeParser timeParser)
        : this(config, scanner, reader, new ResumeSender(scanner, reader), eventStore, possibleLimitDiagnostics, new JsonWorkSelectionStore(), catalog, timeParser, new ProductionEventLog())
    {
    }

    public MonitorService(
        AppConfig config,
        IWindowScanner scanner,
        IUiAutomationReader reader,
        IResumeSender sender,
        IEventStore eventStore,
        PatternCatalog catalog,
        RetryTimeParser timeParser)
        : this(config, scanner, reader, sender, eventStore, new NullPossibleLimitDiagnosticStore(), new AllowAllWorkSelectionStore(), catalog, timeParser)
    {
    }

    public MonitorService(
        AppConfig config,
        IWindowScanner scanner,
        IUiAutomationReader reader,
        IResumeSender sender,
        IEventStore eventStore,
        IPossibleLimitDiagnosticStore possibleLimitDiagnostics,
        IWorkSelectionStore workSelectionStore,
        PatternCatalog catalog,
        RetryTimeParser timeParser,
        IProductionEventLog? trace = null,
        ILimitSurfaceProvider? limitSurfaceProvider = null,
        IAccountQuotaProvider? accountQuotaProvider = null,
        IWorkCompletionProvider? workCompletionProvider = null,
        IWorkNavigator? workNavigator = null)
    {
        _config = config;
        _scanner = scanner;
        _reader = reader;
        _sender = sender;
        _eventStore = eventStore;
        _possibleLimitDiagnostics = possibleLimitDiagnostics;
        _workSelectionStore = workSelectionStore;
        _accountQuotaProvider = accountQuotaProvider ?? (reader is UiAutomationReader ? CodexAccountQuotaProvider.Shared : null);
        _workCompletionProvider = workCompletionProvider ?? (reader is UiAutomationReader uia ? new UiAutomationWorkCompletionProvider(uia) : null);
        // The current UIA navigator can locate diagnostic candidates, but cannot yet bind
        // legacy selections independently of titles. Do not switch user work automatically.
        _workNavigator = workNavigator;
        _limitSurfaceProvider = limitSurfaceProvider ?? (reader is UiAutomationReader ? new UiAutomationLimitSurfaceProvider() : new NullLimitSurfaceProvider());
        _timeParser = timeParser;
        _detector = new UsageLimitDetector(catalog, timeParser);
        _trace = trace ?? new NullProductionEventLog();
        Trace("RUNTIME_CONFIG", $"DryRun={config.DryRun};SendEnter={config.SendEnter};AllowRealSubmit={config.AllowRealSubmit};Policy={config.ResumePolicy}", DateTimeOffset.Now);
    }

    private void Trace(string stage, string outcome, DateTimeOffset now, TargetWindow? target = null, int? confidence = null)
    {
        target ??= Target;
        _trace.Write(new ProductionTrace(now, stage, outcome, target?.ProcessId, target?.Handle.ToString(), RetryAt,
            _currentEvent?.ConversationTarget is { } identity ? JsonWorkSelectionStore.BuildHash(identity) : null, confidence));
    }

    public void Pause()
    {
        _pauseRequested = true;
        _completionResetRequested = true;
        State = AppState.Paused;
    }

    public void Start()
    {
        if (_pauseRequested || State == AppState.Paused)
        {
            _pauseRequested = false;
            State = AppState.Monitoring;
        }
    }

    public void Tick(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        _operationStartedAt = now;
        _operationClock.Restart();
        _operationCancellation = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        if (_pauseRequested || State == AppState.Paused)
        {
            return;
        }

        if (_completionResetRequested)
        {
            _incompleteWorkGate.Reset();
            _completionValidatedAt = null;
            _completionValidationReason = "";
            _completionResetRequested = false;
        }

        LastCheck = now;
        if (_accountQuotaProvider is not null)
        {
            AccountQuota = _accountQuotaProvider.Read(forceRefresh: State == AppState.WaitingForRetry
                && RetryAt is { } retry && now >= retry.AddSeconds(_config.ResumeDelaySeconds), cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Trace("ACCOUNT_QUOTA_SNAPSHOT", AccountQuota is { } q
                ? $"Source=CodexAccount;ShortRemaining={q.ShortWindowRemainingPercent};ShortReset={q.ShortWindowResetAt:O};WeeklyRemaining={q.WeeklyRemainingPercent};WeeklyReset={q.WeeklyResetAt:O};CapturedAt={q.CapturedAt:O}"
                : "NOT_READ", now);
            Trace("TICK", State.ToString(), now);
            TickAccountWork(OperationNow);
            return;
        }
        Trace("TICK", State.ToString(), now);
        if (State == AppState.WaitingForRetry && _currentEvent is not null)
        {
            var readyAt = _currentEvent.RetryAt.AddSeconds(_config.ResumeDelaySeconds);
            Trace("RETRY_TIME_REACHED", now >= readyAt ? "PASS" : "NOT REACHED", now);
            if (now >= readyAt && _config.AutoResume && !_currentEvent.ResumeAttempted && !_eventStore.HasResumeAttempt(_currentEvent.EventId))
            {
                if (_config.ResumePolicy == ResumePolicy.AutomaticForegroundResume)
                {
                    AttemptResume(now);
                }
                else
                {
                    ResumeStatusText = _config.ResumePolicy == ResumePolicy.NotifyOnly
                        ? "Usage appears ready. Notification only."
                        : "Usage appears ready. Waiting for confirmation.";
                    State = AppState.ReadyToResume;
                }
            }

            return;
        }

        if (State == AppState.ReadyToResume)
        {
            return;
        }

        if (State == AppState.Verifying && _verifyUntil is not null && now < _verifyUntil)
        {
            return;
        }

        ScanForLimit(now);
    }

    private static bool FreshQuota(QuotaSnapshot? quota, DateTimeOffset now) => quota is not null
        && quota.CapturedAt <= now.AddSeconds(10) && now - quota.CapturedAt <= TimeSpan.FromSeconds(75);

    private bool AvailableQuota(DateTimeOffset now) => FreshQuota(AccountQuota, now)
        && AccountQuota is { ShortWindowRemainingPercent: > 0 and <= 100, WeeklyRemainingPercent: > 0 and <= 100 };

    private void TickAccountWork(DateTimeOffset now)
    {
        var windows = _scanner.FindTargets();
        if (windows.Count == 0)
        {
            Target = null;
            _currentEvent = null;
            _incompleteWorkGate.Reset();
            _completionValidatedAt = null;
            _completionValidationReason = "";
            State = AppState.WaitingForTarget;
            ResumeStatusText = "Waiting for ChatGPT/Codex.";
            Trace("TARGET_FOUND", "FAIL", now);
            return;
        }
        Target = windows[0];
        if (!AvailableQuota(now))
        {
            _incompleteWorkGate.Reset();
            _completionValidatedAt = null;
            _completionValidationReason = "";
            _currentEvent = null;
            UsageLimitText = "Account quota not verified available";
            if (FreshQuota(AccountQuota, now) && AccountQuota is { } quota && TryGetBlockingQuotaRetryAt(quota, out var reset))
            {
                // This is an account countdown only, never a permission to resume a conversation.
                _currentEvent = new LimitEvent("account-countdown", Target.ProcessId, Target.Handle, Target.Title,
                    now, reset, "Codex account quota", 100);
                UsageLimitText = "Account quota exhausted";
                State = AppState.WaitingForRetry;
            }
            else State = AccountQuota is null ? AppState.Monitoring : AppState.LimitDetected;
            ResumeStatusText = "Waiting for verified account quota.";
            return;
        }

        UsageLimitText = "Not detected";
        var observedKeys = new HashSet<string>(StringComparer.Ordinal);
        var observedSelectedIdentityHashes = new HashSet<string>(StringComparer.Ordinal);
        var checkedWorks = _workSelectionStore.Load().Where(record => record.AutoResumeEnabled && !record.IsStale).Take(5).ToArray();
        var enabledSelectionHashes = _config.RequireWorkSelection
            ? checkedWorks.Select(record => record.ConversationIdentityHash).ToHashSet(StringComparer.Ordinal) : [];
        string? completionBlock = null;
        (TargetWindow Window, ConversationTargetIdentity Identity, AssistantCompletionEvidence Evidence)? eligible = null;
        void ObserveWork(TargetWindow window, ConversationTargetIdentity identity, Func<bool>? visitCurrent = null)
        {
            if (!_config.AutoResume || !_workSelectionStore.IsAutoResumeEnabled(identity)) return;
            var identityHash = JsonWorkSelectionStore.BuildHash(identity);
            observedSelectedIdentityHashes.Add(identityHash);
            var key = $"{window.ProcessId}:{window.Handle}:{identityHash}";
            observedKeys.Add(key);
            var evidence = ReadCompletion(window, identity);
            if (visitCurrent is not null && !visitCurrent())
                evidence = AssistantCompletionEvidence.Unknown("WORK_CHANGED_DURING_OBSERVATION");
            if (evidence.Stopped != false && evidence.Footer != FooterPresence.Present
                && (evidence.Stopped is null || evidence.Footer == FooterPresence.Unknown))
                completionBlock ??= evidence.Reason;
            _operationCancellation.ThrowIfCancellationRequested();
            if (_pauseRequested || _completionResetRequested) return;
            var observedAt = OperationNow;
            var confirmed = _incompleteWorkGate.Observe(new(key, evidence.AssistantReplyIdentity, observedAt,
                true, AvailableQuota(observedAt), evidence.Stopped, evidence.Footer), observedAt);
            Trace("WORK_COMPLETION", $"{evidence.Reason};TwoConfirmations={confirmed}", now, window);
            var candidateEventId = $"incomplete-v1-{JsonWorkSelectionStore.BuildHash(identity)}-{evidence.AssistantReplyIdentity}";
            if (confirmed && eligible is null && !_eventStore.HasResumeAttempt(candidateEventId)) eligible = (window, identity, evidence);
        }
        foreach (var window in windows)
        {
            _operationCancellation.ThrowIfCancellationRequested();
            Trace("TARGET_FOUND", "PASS", now, window);
            var identity = (_reader as IConversationIdentityProvider)?.CaptureConversationIdentity(window.Handle);
            if (identity is not null && _reader is IActiveWorkTitleProvider titleProvider)
            {
                var activeTitle = titleProvider.GetActiveWorkDisplayName(window.Handle);
                if (!string.IsNullOrWhiteSpace(activeTitle))
                {
                    _workSelectionStore.UpsertRecent(activeTitle, identity, OperationNow);
                }
            }
            if (identity is not null) ObserveWork(window, identity);
        }
        if (_workNavigator is not null && _config.AutoResume && _config.ResumePolicy == ResumePolicy.AutomaticForegroundResume)
        {
            foreach (var work in checkedWorks.Where(work => !observedSelectedIdentityHashes.Contains(work.ConversationIdentityHash)))
            {
                foreach (var window in windows)
                {
                    _operationCancellation.ThrowIfCancellationRequested();
                    if (_pauseRequested || _completionResetRequested) return;
                    using var visit = _workNavigator.TryVisit(window, work, _operationCancellation);
                    if (visit is null) continue;
                    if (!visit.IsCurrent || JsonWorkSelectionStore.BuildHash(visit.Identity) != work.ConversationIdentityHash) continue;
                    if (!visit.SelectionIdentityVerified)
                    {
                        Trace("CHECKED_WORK_INSPECTION", "SAVED_IDENTITY_UNVERIFIED", OperationNow, window);
                        continue;
                    }
                    ObserveWork(window, visit.Identity, () => visit.IsCurrent);
                    Trace("CHECKED_WORK_INSPECTION", $"PASS;Work={work.ConversationIdentityHash}", OperationNow, window);
                    break;
                }
            }
        }
        _incompleteWorkGate.RetainOnly(observedKeys);
        if (eligible is not { } candidate)
        {
            _currentEvent = null;
            _completionValidatedAt = null;
            _completionValidationReason = "";
            var hasCheckedButNotInspectableWork = enabledSelectionHashes.Except(observedSelectedIdentityHashes).Any();
            State = completionBlock is null ? AppState.Monitoring : AppState.NeedsAttention;
            ResumeStatusText = completionBlock is null
                ? (hasCheckedButNotInspectableWork
                    ? "Monitoring the active Work. Other remembered Works are not auto-switched in v0.1."
                    : "No twice-confirmed incomplete selected Work.")
                : $"Selected Work cannot be verified: {completionBlock}. Automatic checks will continue.";
            if (hasCheckedButNotInspectableWork) Trace("CHECKED_WORK_INSPECTION", _workNavigator is null ? "NOT_IMPLEMENTED" : "LOCATOR_UNVERIFIED", now, Target);
            return;
        }

        Target = candidate.Window;
        using var resumeVisit = _reader is IConversationIdentityProvider activeIdentity && !activeIdentity.IsConversationStillActive(Target.Handle, candidate.Identity)
            && checkedWorks.FirstOrDefault(work => work.ConversationIdentityHash == JsonWorkSelectionStore.BuildHash(candidate.Identity)) is { } candidateWork
            ? _workNavigator?.TryVisit(Target, candidateWork, _operationCancellation) : null;
        if (resumeVisit is not null && (!resumeVisit.IsCurrent || !resumeVisit.SelectionIdentityVerified))
        {
            State = AppState.NeedsAttention;
            ResumeStatusText = "Selected Work navigation was interrupted.";
            return;
        }
        if (_reader is IConversationIdentityProvider recheck && !recheck.IsConversationStillActive(Target.Handle, candidate.Identity))
        {
            State = AppState.NeedsAttention;
            ResumeStatusText = "Selected Work changed before resume verification.";
            return;
        }
        var eventId = $"incomplete-v1-{JsonWorkSelectionStore.BuildHash(candidate.Identity)}-{candidate.Evidence.AssistantReplyIdentity}";
        if (_currentEvent?.EventId != eventId)
            _currentEvent = new LimitEvent(eventId, Target.ProcessId, Target.Handle, Target.Title, now, now,
                "Twice-confirmed incomplete assistant reply", 100)
            { ConversationTarget = candidate.Identity, AssistantReplyIdentity = candidate.Evidence.AssistantReplyIdentity };
        _completionValidatedAt = OperationNow;
        _completionValidationReason = candidate.Evidence.Reason;
        if (_currentEvent.ResumeAttempted || _eventStore.HasResumeAttempt(eventId))
        {
            State = AppState.Error;
            ResumeStatusText = "Resume already attempted for this reply. Will not repeat.";
            return;
        }
        State = AppState.ReadyToResume;
        ResumeStatusText = "Selected Work is stopped and incomplete; confirmed twice.";
        if (_config.ResumePolicy == ResumePolicy.AutomaticForegroundResume
            && now >= _currentEvent.RetryAt.AddSeconds(_config.ResumeDelaySeconds))
            AttemptResume(now, completionAlreadyValidatedThisTick: true);
    }

    private AssistantCompletionEvidence ReadCompletion(TargetWindow window, ConversationTargetIdentity identity)
    {
        try
        {
            var evidence = _workCompletionProvider?.Read(window, identity, _operationCancellation)
                ?? AssistantCompletionEvidence.Unknown("NO_COMPLETION_PROVIDER");
            return PromoteTrustedLimitSurface(window, evidence);
        }
        catch { return AssistantCompletionEvidence.Unknown("COMPLETION_PROVIDER_FAILED"); }
    }

    private AssistantCompletionEvidence PromoteTrustedLimitSurface(TargetWindow window, AssistantCompletionEvidence evidence)
    {
        if (evidence.IsIncomplete || evidence.Footer == FooterPresence.Present
            || string.IsNullOrWhiteSpace(evidence.AssistantReplyIdentity))
        {
            return evidence;
        }

        if (evidence.Footer != FooterPresence.Unknown || !AvailableQuota(OperationNow))
        {
            return evidence;
        }

        foreach (var candidate in _limitSurfaceProvider.FindLimitSurfaces(window))
        {
            _operationCancellation.ThrowIfCancellationRequested();
            Trace("COMPLETION_LIMIT_SURFACE", $"{candidate.Kind};Eligible={candidate.IsEligibleForAutomation};Confidence={candidate.Confidence};Depth={candidate.Depth}", OperationNow, window);
            if (!candidate.IsEligibleForAutomation)
            {
                continue;
            }

            var result = _detector.AnalyzeForAutomation(candidate.Text, OperationNow, candidate.HasWarningRole, candidate.HasWarningIcon);
            if (result.Kind != DetectionKind.LimitDetected)
            {
                Trace("COMPLETION_LIMIT_SURFACE", $"DETECTOR_REJECTED:{result.Kind};Confidence={result.Confidence}", OperationNow, window, result.Confidence);
                continue;
            }

            Trace("COMPLETION_LIMIT_SURFACE", $"TRUSTED_LIMIT_INTERRUPTION;Confidence={result.Confidence}", OperationNow, window, result.Confidence);
            return evidence with
            {
                Stopped = true,
                Footer = FooterPresence.Absent,
                Reason = evidence.Stopped == false
                    ? "TRUSTED_LIMIT_SURFACE_OVERRIDES_STALE_RUNNING_CONTROL"
                    : "TRUSTED_LIMIT_SURFACE_WITHOUT_COMPLETED_FOOTER"
            };
        }

        return evidence;
    }

    private bool VerifyIncompleteWork(DateTimeOffset now)
    {
        now = OperationNow;
        if (_operationCancellation.IsCancellationRequested || _pauseRequested || _completionResetRequested) return false;
        if (Target is null || _currentEvent?.ConversationTarget is not { } identity
            || _currentEvent.AssistantReplyIdentity is not { } reply
            || _completionValidatedAt is not { } validated || now - validated > TimeSpan.FromSeconds(60)
            || !_config.AutoResume || !_workSelectionStore.IsAutoResumeEnabled(identity) || !AvailableQuota(now)
            || !_scanner.IsStillValid(Target) || _reader is not IConversationIdentityProvider identities
            || !identities.IsConversationStillActive(Target.Handle, identity)) return false;
        var evidence = ReadCompletion(Target, identity);
        return !_operationCancellation.IsCancellationRequested && !_pauseRequested && !_completionResetRequested && AvailableQuota(OperationNow)
            && OperationNow - validated <= TimeSpan.FromSeconds(60)
            && _workSelectionStore.IsAutoResumeEnabled(identity)
            && evidence.IsIncomplete && evidence.AssistantReplyIdentity == reply;
    }

    private void ScanForLimit(DateTimeOffset now)
    {
        // Legacy injected detector/test path. Production account monitoring returns earlier in Tick.
        var windows = _scanner.FindTargets();
        if (windows.Count == 0)
        {
            Target = null;
            Trace("TARGET_FOUND", "FAIL", now);
            UsageLimitText = "Not detected";
            ResumeStatusText = "Waiting for ChatGPT/Codex.";
            State = AppState.WaitingForTarget;
            return;
        }

        var firstTarget = windows[0];
        foreach (var window in windows)
        {
            Trace("TARGET_FOUND", "PASS", now, window);
            var text = _reader.ReadVisibleText(window.Handle, out var hasWarningRole);
            Trace("LIMIT_READER", $"Characters={text.Length};WarningRole={hasWarningRole}", now, window);
            var quotaSurfaceText = _reader is IAccountQuotaReader quotaReader
                ? quotaReader.ReadAccountQuotaSurfaceText(window.Handle)
                : "";
            var conversationTarget = _reader is IConversationIdentityProvider identityProvider
                ? identityProvider.CaptureConversationIdentity(window.Handle)
                : null;
            Trace("WORK_IDENTITY", conversationTarget is null ? "FAIL" : "CAPTURED_NOT_YET_REVALIDATED", now, window);
            var quotaSnapshot = QuotaSnapshotParser.Parse(quotaSurfaceText, now, _timeParser);
            if (HasAccountQuotaPanelContext(quotaSurfaceText) && TryGetBlockingQuotaRetryAt(quotaSnapshot, out var quotaRetryAt))
            {
                Target = window;
                UsageLimitText = "Account quota exhausted";
                _currentEvent = new LimitEvent(
                    EventId: BuildEventId(window, conversationTarget, quotaRetryAt),
                    ProcessId: window.ProcessId,
                    WindowHandle: window.Handle,
                    WindowTitle: window.Title,
                    DetectedAt: now,
                    RetryAt: quotaRetryAt,
                    SourceText: "Account quota exhausted",
                    Confidence: 90)
                {
                    ConversationTarget = conversationTarget
                };

                if (_config.RequireWorkSelection && !_workSelectionStore.IsAutoResumeEnabled(conversationTarget))
                {
                    ResumeStatusText = "Account quota is exhausted, but this Work is not selected for auto resume.";
                    State = AppState.LimitDetected;
                    return;
                }

                State = AppState.WaitingForRetry;
                Trace("WAITING_FOR_RETRY", "PASS", now);
                return;
            }

            var handledSurface = false;
            foreach (var candidate in _limitSurfaceProvider.FindLimitSurfaces(window))
            {
                Trace("LIMIT_SURFACE_CANDIDATE", $"{candidate.Kind};Eligible={candidate.IsEligibleForAutomation};Confidence={candidate.Confidence};Depth={candidate.Depth}", now, window);
                var candidateResult = _detector.AnalyzeForAutomation(candidate.Text, now, candidate.HasWarningRole, candidate.HasWarningIcon);
                if (!candidate.IsEligibleForAutomation)
                {
                    if (PossibleLimitHeuristics.Analyze(candidate.Text).IsInteresting)
                    {
                        _possibleLimitDiagnostics.Save(window, candidate.Text, candidateResult, candidate.HasWarningRole, $"limit_surface_rejected:{candidate.Kind}:{candidate.Confidence}", now);
                    }

                    continue;
                }

                Trace("LIMIT_DETECTED", $"Surface:{candidateResult.Kind}", now, window, candidateResult.Confidence);
                Trace("RETRY_PARSED", candidateResult.RetryAt is null ? "FAIL" : candidateResult.RetryAt.Value.ToString("O"), now, window);
                if (TryApplyLimitDetection(window, candidate.Text, candidateResult, conversationTarget, quotaSnapshot, candidate.HasWarningRole, now, "limit_surface"))
                {
                    handledSurface = true;
                    break;
                }

                if (PossibleLimitHeuristics.Analyze(candidate.Text).IsInteresting)
                {
                    _possibleLimitDiagnostics.Save(window, candidate.Text, candidateResult, candidate.HasWarningRole, $"limit_surface_detector_rejected:{candidate.Kind}:{candidateResult.Kind}:{candidateResult.Confidence}", now);
                }
            }

            if (handledSurface)
            {
                return;
            }

            var result = _detector.AnalyzeForAutomation(text, now, hasWarningRole, text.Contains('⚠') || text.Contains('!'));
            Trace("LIMIT_DETECTED", result.Kind.ToString(), now, window, result.Confidence);
            Trace("RETRY_PARSED", result.RetryAt is null ? "FAIL" : result.RetryAt.Value.ToString("O"), now, window);
            if (!TryApplyLimitDetection(window, text, result, conversationTarget, quotaSnapshot, hasWarningRole, now, "visible_text"))
            {
                if (PossibleLimitHeuristics.Analyze(text).IsInteresting)
                {
                    _possibleLimitDiagnostics.Save(window, text, result, hasWarningRole, BuildRejectionReason(result), now);
                }

                continue;
            }

            return;
        }

        Target = firstTarget;
        UsageLimitText = "Not detected";
        State = AppState.Monitoring;
    }

    private bool TryApplyLimitDetection(
        TargetWindow window,
        string sourceText,
        DetectionResult result,
        ConversationTargetIdentity? conversationTarget,
        QuotaSnapshot quotaSnapshot,
        bool hasWarningRole,
        DateTimeOffset now,
        string source)
    {
        if (result.Kind != DetectionKind.LimitDetected)
        {
            return false;
        }

        Target = window;
        if (result.RetryAt is null)
        {
            UsageLimitText = $"Detected without retry time, confidence {result.Confidence}";
            ResumeStatusText = "Usage limit detected. Waiting for a readable reset time.";
            var rejectionReason = source == "visible_text"
                ? "limit_detected_no_retry_datetime"
                : $"{source}:limit_detected_no_retry_datetime";
            _possibleLimitDiagnostics.Save(window, sourceText, result, hasWarningRole, rejectionReason, now);
            State = AppState.LimitDetected;
            return true;
        }

        UsageLimitText = $"Detected, confidence {result.Confidence}";
        var retryAt = PreferAccountQuotaResetTime(quotaSnapshot, result.RetryAt.Value);
        _currentEvent = new LimitEvent(
            EventId: BuildEventId(window, conversationTarget, retryAt),
            ProcessId: window.ProcessId,
            WindowHandle: window.Handle,
            WindowTitle: window.Title,
            DetectedAt: now,
            RetryAt: retryAt,
            SourceText: TrimSource(sourceText),
            Confidence: result.Confidence)
        {
            ConversationTarget = conversationTarget
        };
        if (_config.RequireWorkSelection && !_workSelectionStore.IsAutoResumeEnabled(conversationTarget))
        {
            ResumeStatusText = "Usage limit detected, but this Work is not selected for auto resume.";
            Trace("PERMISSION", "FAIL", now);
            State = AppState.LimitDetected;
            return true;
        }

        State = AppState.WaitingForRetry;
        Trace("WAITING_FOR_RETRY", "PASS", now);
        return true;
    }

    private static string BuildEventId(TargetWindow window, ConversationTargetIdentity? conversation, DateTimeOffset retryAt)
    {
        // Re-observing the same target/reset must not create a new send permission.
        var targetKey = $"{window.ProcessId}:{window.Handle}:{(conversation is null ? "" : JsonWorkSelectionStore.BuildHash(conversation))}";
        return $"v2-{ConversationTargetIdentity.Hash(targetKey)}-{retryAt.UtcTicks}";
    }

    private DateTimeOffset PreferAccountQuotaResetTime(QuotaSnapshot snapshot, DateTimeOffset detectedRetryAt)
    {
        if (snapshot.ShortWindowResetAt is not null)
        {
            return snapshot.ShortWindowResetAt.Value;
        }

        return detectedRetryAt;
    }

    private static bool TryGetBlockingQuotaRetryAt(QuotaSnapshot snapshot, out DateTimeOffset retryAt)
    {
        var blockers = new List<DateTimeOffset>();
        if (snapshot.ShortWindowRemainingPercent == 0 && snapshot.ShortWindowResetAt is not null)
        {
            blockers.Add(snapshot.ShortWindowResetAt.Value);
        }

        if (snapshot.WeeklyRemainingPercent == 0 && snapshot.WeeklyResetAt is not null)
        {
            blockers.Add(snapshot.WeeklyResetAt.Value);
        }

        if (blockers.Count == 0)
        {
            retryAt = default;
            return false;
        }

        retryAt = blockers.Max();
        return true;
    }

    private static bool HasAccountQuotaPanelContext(string text) =>
        text.Contains("剩餘用量", StringComparison.OrdinalIgnoreCase)
        || text.Contains("剩余用量", StringComparison.OrdinalIgnoreCase)
        || text.Contains("remaining usage", StringComparison.OrdinalIgnoreCase)
        || text.Contains("usage remaining", StringComparison.OrdinalIgnoreCase);

    private static string BuildRejectionReason(DetectionResult result)
    {
        if (result.RetryAt is null)
        {
            return "no_valid_retry_datetime";
        }

        if (result.Kind != DetectionKind.LimitDetected)
        {
            return $"confidence_or_context_not_high_enough:{result.Kind}:{result.Confidence}";
        }

        return "not_rejected";
    }

    private void AttemptResume(DateTimeOffset now, bool completionAlreadyValidatedThisTick = false)
    {
        if (_currentEvent is null || Target is null)
        {
            State = AppState.Error;
            return;
        }

        State = AppState.PreResumeVerify;
        Trace("RESUME_PATH", "REACHED", now);
        if (!_config.AutoResume || _currentEvent.ResumeAttempted || _eventStore.HasResumeAttempt(_currentEvent.EventId)
            || (_config.RequireWorkSelection && !_workSelectionStore.IsAutoResumeEnabled(_currentEvent.ConversationTarget)))
        {
            ResumeStatusText = "Resume aborted: permission revoked or event already attempted.";
            Trace("PERMISSION_OR_DUPLICATE", "FAIL", now);
            State = AppState.Error;
            return;
        }

        if (!_scanner.IsStillValid(Target))
        {
            Trace("WORK_WINDOW_REDISCOVERY", "FAIL", now);
            State = AppState.Error;
            return;
        }

        Trace("WORK_WINDOW_REDISCOVERY", "PASS", now);

        if (AccountQuotaStillBlocksResume(now))
        {
            return;
        }

        if (_accountQuotaProvider is not null
            && !(completionAlreadyValidatedThisTick && HasCurrentTickCompletionValidation())
            && !VerifyIncompleteWork(now))
        {
            _incompleteWorkGate.Reset();
            _completionValidatedAt = null;
            _completionValidationReason = "";
            ResumeStatusText = "Resume aborted: last reply completion state is not safely confirmed.";
            Trace("PRE_INPUT_COMPLETION", "BLOCKED", now);
            State = AppState.Error;
            return;
        }

        if (_currentEvent.ConversationTarget is not null
            && _reader is IConversationIdentityProvider identityProvider
            && !identityProvider.IsConversationStillActive(Target.Handle, _currentEvent.ConversationTarget))
        {
            ResumeStatusText = "Resume aborted: target conversation changed.";
            Trace("CONVERSATION_IDENTITY", "FAIL", now);
            State = AppState.Error;
            return;
        }

        try
        {
            // Persist the attempt before any input; an uncertain result must never retry automatically.
            _eventStore.MarkResumeAttempted(_currentEvent.EventId, now, sent: false);
            Trace("PRE_INPUT_CLAIM", "PASS", now);
        }
        catch
        {
            ResumeStatusText = "Resume aborted: could not persist attempt.";
            Trace("PRE_INPUT_CLAIM", "FAIL", now);
            State = AppState.Error;
            return;
        }

        _currentEvent = _currentEvent with { ResumeAttempted = true };
        State = AppState.ResumeSending;
        bool sent;
        try
        {
            Trace("RESUME_SENDER", "REACHED", now);
            var originalIdentity = _currentEvent.ConversationTarget;
            sent = _sender.TrySend(Target, _config.ResumeText, _config.DryRun, _config.SendEnter && _config.AllowRealSubmit,
                () => !_operationCancellation.IsCancellationRequested
                    && VerifyResumeTargetLightweight(originalIdentity)
                    && (originalIdentity is null || _reader is IConversationIdentityProvider currentIdentity
                        && currentIdentity.IsConversationStillActive(Target.Handle, originalIdentity)));
            Trace("INPUT", _config.DryRun ? "SAFETY BLOCKED" : sent ? "PROVIDER_REPORTED_INSERTED" : "FAIL_OR_UNCONFIRMED", now);
            Trace("ENTER", _config.DryRun || !_config.SendEnter || !_config.AllowRealSubmit ? "SAFETY BLOCKED" : sent ? "PROVIDER_REPORTED_SUBMITTED_NOT_VERIFIED" : "FAIL_OR_UNCONFIRMED", now);
        }
        catch
        {
            ResumeStatusText = "Resume unconfirmed: input provider failed. Attempt will not be repeated.";
            State = AppState.Error;
            return;
        }
        var actuallySubmitted = sent && !_config.DryRun && _config.SendEnter && _config.AllowRealSubmit;
        _currentEvent = _currentEvent with
        {
            ResumeAttempted = true,
            ResumeSentAt = actuallySubmitted ? now : null
        };
        if (actuallySubmitted)
        {
            try
            {
                _eventStore.MarkResumeAttempted(_currentEvent.EventId, now, sent: true);
            }
            catch
            {
                ResumeStatusText = "Resume unconfirmed: result could not be persisted. Attempt will not be repeated.";
                State = AppState.Error;
                return;
            }
        }

        _verifyUntil = sent ? now.AddSeconds(20) : null;
        ResumeStatusText = sent && _config.DryRun
            ? $"DRY RUN - Resume target verified. Would send: {_config.ResumeText}"
            : sent ? "Resume text inserted." : "Resume aborted.";
        State = sent ? AppState.Verifying : AppState.Error;
    }

    private bool HasCurrentTickCompletionValidation()
    {
        var now = OperationNow;
        return Target is not null
            && _currentEvent?.ConversationTarget is not null
            && !string.IsNullOrWhiteSpace(_currentEvent.AssistantReplyIdentity)
            && _completionValidatedAt is { } validated
            && _completionValidationReason == "TRUSTED_LIMIT_SURFACE_WITHOUT_COMPLETED_FOOTER"
            && now - validated <= TimeSpan.FromSeconds(10)
            && _scanner.IsStillValid(Target)
            && _workSelectionStore.IsAutoResumeEnabled(_currentEvent.ConversationTarget)
            && AvailableQuota(now)
            && (_reader is not IConversationIdentityProvider identities
                || identities.IsConversationStillActive(Target.Handle, _currentEvent.ConversationTarget));
    }

    private bool VerifyResumeTargetLightweight(ConversationTargetIdentity? identity)
    {
        if (_operationCancellation.IsCancellationRequested || _pauseRequested || _completionResetRequested || Target is null)
        {
            return false;
        }

        if (!_config.AutoResume || !_scanner.IsStillValid(Target))
        {
            return false;
        }

        if (identity is not null && !_workSelectionStore.IsAutoResumeEnabled(identity))
        {
            return false;
        }

        if (_accountQuotaProvider is not null && !AvailableQuota(OperationNow))
        {
            return false;
        }

        return true;
    }

    private bool AccountQuotaStillBlocksResume(DateTimeOffset now)
    {
        if (_accountQuotaProvider is not null)
        {
            AccountQuota = _accountQuotaProvider.Read(forceRefresh: true, cancellationToken: _operationCancellation);
            _operationCancellation.ThrowIfCancellationRequested();
            if (AvailableQuota(OperationNow))
                return false;
            if (AccountQuota is { } quota && TryGetBlockingQuotaRetryAt(quota, out var resetAt)
                && resetAt > now && _currentEvent is not null)
            {
                _currentEvent = _currentEvent with { RetryAt = resetAt };
                State = AppState.WaitingForRetry;
            }
            else State = AppState.LimitDetected;
            ResumeStatusText = "Resume blocked: account quota has not been verified available.";
            Trace("ACCOUNT_QUOTA_RECHECK", "BLOCKED", now);
            return true;
        }
        if (Target is null || _currentEvent is null || _reader is not IAccountQuotaReader quotaReader)
        {
            return false;
        }

        var quotaSurfaceText = quotaReader.ReadAccountQuotaSurfaceText(Target.Handle);
        if (!HasAccountQuotaPanelContext(quotaSurfaceText))
        {
            return false;
        }

        var quotaSnapshot = QuotaSnapshotParser.Parse(quotaSurfaceText, now, _timeParser);
        if (!TryGetBlockingQuotaRetryAt(quotaSnapshot, out var quotaRetryAt))
        {
            return false;
        }

        UsageLimitText = "Account quota exhausted";
        if (quotaRetryAt > now)
        {
            var updatedEvent = _currentEvent with
            {
                EventId = BuildEventId(Target, _currentEvent.ConversationTarget, quotaRetryAt),
                RetryAt = quotaRetryAt,
                SourceText = "Account quota exhausted"
            };
            _currentEvent = updatedEvent;
            ResumeStatusText = "Account quota is still exhausted. Waiting for the latest reset time.";
            State = AppState.WaitingForRetry;
            return true;
        }

        ResumeStatusText = "Account quota is still exhausted, but the reset time is not usable yet.";
        State = AppState.LimitDetected;
        return true;
    }

    public void ConfirmResume(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        _operationStartedAt = now;
        _operationClock.Restart();
        _operationCancellation = cancellationToken;
        if (cancellationToken.IsCancellationRequested || _pauseRequested || _completionResetRequested) return;
        if (State == AppState.ReadyToResume && _currentEvent is not null && Target is not null)
        {
            if (_accountQuotaProvider is not null && (_config.ResumePolicy == ResumePolicy.NotifyOnly
                || now < _currentEvent.RetryAt.AddSeconds(_config.ResumeDelaySeconds))) return;
            AttemptResume(now);
        }
    }

    public void SetPreviewState(AppState state, DateTimeOffset? retryAt = null)
    {
        State = state;
        LastCheck = DateTimeOffset.Now;
        Target ??= new TargetWindow(0, 0, "ChatGPT / Codex", "ChatGPT");
        UsageLimitText = state == AppState.WaitingForRetry ? "Preview usage limit" : "Not detected";
        _currentEvent = retryAt is null
            ? null
            : new LimitEvent(
                EventId: "ui-preview",
                ProcessId: 0,
                WindowHandle: 0,
                WindowTitle: "ChatGPT / Codex",
                DetectedAt: LastCheck,
                RetryAt: retryAt.Value,
                SourceText: "UI preview only",
                Confidence: 100);
    }

    private static string TrimSource(string text)
    {
        var compact = string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
        return compact.Length > 500 ? compact[..500] : compact;
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GPTAutoResume.Core;
using GPTAutoResume.Automation;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace GPTAutoResume;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ConfigStore _configStore = new();
    private readonly MonitorService _monitor;
    private readonly JsonWorkSelectionStore _workSelectionStore = new();
    private readonly BannerService _bannerService = new();
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _targetPresenceTimer = new();
    private readonly BackgroundOperationGate _monitorTickGate = new();
    private readonly BackgroundOperationGate _workRefreshGate = new();
    private readonly BackgroundOperationGate _quotaProbeGate = new();
    private readonly BackgroundOperationGate _targetPresenceGate = new();
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Window _owner;
    private AppState _lastState;
    private readonly string? _previewRemaining;
    private readonly string? _previewRetryAt;
    private bool _showAllRecentWork;
    private bool _isRefreshingWork;
    private bool? _lastTargetPresent;
    private QuotaProbeDisplayState _quotaProbe = new(QuotaProbeStatus.NotRead);
    private CancellationTokenSource? _targetQuotaRefreshSource;
    private bool _disposed;

    public AppConfig Config { get; }
    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand MinimizeCommand { get; }
    public ICommand DiscoveryCommand { get; }
    public ICommand TestModeCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand ConfirmResumeCommand { get; }
    public ICommand DiagnosticsCommand { get; }
    public ICommand CaptureLimitCommand { get; }
    public ICommand WrongWindowProbeCommand { get; }
    public ICommand RefreshBannerCommand { get; }
    public ICommand OpenBannerLinkCommand { get; }
    public ICommand OpenHomepageCommand { get; }
    public ICommand OpenPossibleLimitDiagnosticsCommand { get; }
    public ICommand ToggleRecentWorkCommand { get; }
    public ICommand RefreshWorkCommand { get; }
    public ICommand RefreshQuotaCommand { get; }
    public ICommand ResetResumeMessageCommand { get; }
    public ObservableCollection<WorkSelectionViewModel> RecentWorkSelections { get; } = [];
    public IReadOnlyList<ResumePolicyOption> ResumePolicies => Localization.GetResumePolicies(Config.Language);

    public IReadOnlyList<LanguageOption> Languages => Localization.Languages;
    public string SelectedLanguage
    {
        get => Config.Language;
        set
        {
            if (Config.Language == value)
            {
                return;
            }

            ResumeMessageLocalizer.ApplyLanguage(Config, value);
            _configStore.Save(Config);
            RaiseAll();
        }
    }

    public string ResumeText
    {
        get => Config.ResumeText;
        set
        {
            if (Config.ResumeText == value)
            {
                return;
            }

            ResumeMessageLocalizer.SetCustom(Config, value);
            _configStore.Save(Config);
            RaiseAll();
        }
    }

    public string BannerImage { get; private set; } = "pack://application:,,,/Assets/fallback-cat-banner.png";
    public string HeaderTitle => "GPT AUTO RESUME";
    public string MoodText => _monitor.State == AppState.WaitingForRetry ? T("TakeBreak") : T("WaitingActivity");
    public string StatusLine => $"● {FormatState(_monitor.State)}";
    public string MainStatus => FormatState(_monitor.State);
    public string MainMessage => _monitor.State switch
    {
        AppState.WaitingForTarget => T("WaitingForTargetMessage"),
        AppState.Monitoring => T("WaitingForLimit"),
        AppState.WaitingForRetry => T("WaitingForQuota"),
        AppState.ReadyToResume => T("ReadyToResumeMessage"),
        AppState.ResumeSending => T("ResumingMessage"),
        AppState.Verifying => T("VerifyingMessage"),
        AppState.ResumeSuccess => T("ResumeSuccess"),
        AppState.Paused => T("PausedMessage"),
        AppState.NeedsAttention or AppState.Error => T("NeedsAttentionMessage"),
        _ => T("WaitingForLimit")
    };
    public string SubMessage => _monitor.State switch
    {
        AppState.WaitingForTarget => T("OpenChatGptToStart"),
        AppState.Monitoring => T("AllNormalRest"),
        AppState.WaitingForRetry => T("WillResumeOriginalWork"),
        AppState.ReadyToResume => T("PressContinueWhenReady"),
        AppState.ResumeSending => T("SwitchingToChatGpt"),
        AppState.Verifying => T("CheckingResumeResult"),
        AppState.ResumeSuccess => T("BackToMonitoringSoon"),
        AppState.Paused => T("MonitoringPaused"),
        AppState.NeedsAttention or AppState.Error => T("CannotVerifyOriginalWork"),
        _ => ""
    };
    public string PrimaryStatus => _monitor.State == AppState.WaitingForRetry ? T("LimitDetected") : T("NoUsageLimit");
    public string SecondaryStatus => _monitor.State == AppState.WaitingForRetry ? T("AutoResumeReady") : Target;
    public string ModeBadge => Config.AllowRealSubmit ? T("ExperimentalAutomation") : T("AlphaSafe");
    public string AutoResumeState => Config.AutoResume ? "ON" : "OFF";
    public bool AutoResumeEnabled
    {
        get => Config.AutoResume;
        set
        {
            if (Config.AutoResume == value)
            {
                return;
            }

            Config.AutoResume = value;
            if (value)
            {
                _monitor.Start();
                ResumeStatus = "Monitoring started.";
            }
            else
            {
                _monitor.Pause();
                ResumeStatus = "Monitoring paused.";
            }

            _configStore.Save(Config);
            RaiseAll();
        }
    }
    public string DryRunState => Config.DryRun ? "ON" : "OFF";
    public string SafetyDelay => $"{Config.ResumeDelaySeconds} s";
    public string Target => _monitor.Target is null ? "ChatGPT / Codex" : $"{_monitor.Target.ProcessName}: {_monitor.Target.Title}";
    public string LastCheck => _monitor.LastCheck == default ? "-" : _monitor.LastCheck.ToString("HH:mm:ss");
    public string QuotaProbeText => QuotaProbeDisplay.FormatCurrent(_quotaProbe, Config.Language, DateTimeOffset.Now,
        _quotaProbe.Snapshot is { } currentQuota
        && DateTimeOffset.Now - currentQuota.CapturedAt < TimeSpan.FromMinutes(2)
        && (currentQuota.ShortWindowRemainingPercent == 0 || currentQuota.WeeklyRemainingPercent == 0));
    public string QuotaProbeTooltip => QuotaProbeDisplay.FormatTooltip(_quotaProbe, Config.Language);
    public string UsageLimit => _monitor.UsageLimitText;
    public string RetryAt => _previewRetryAt ?? _monitor.RetryAt?.ToString("HH:mm") ?? "";
    public string RetryAtInline => string.IsNullOrWhiteSpace(RetryAt) ? "" : string.Format(T("EstimatedResumeAt"), RetryAt);
    public string Version => "v0.1.1-alpha";
    public string Tagline => T("Tagline");
    public string StudioPresents => T("StudioPresents");
    public string HeroTitle => T("HeroTitle");
    public string ProductSubtitle => T("ProductSubtitle");
    public string ByBrand => T("ByBrand");
    public string HeroNote => T("HeroNote");
    public string HeroDescription => T("HeroDescription");
    public string ResumeAtLabel => T("ResumeAt");
    public string RemainingLabel => T("Remaining");
    public string AllGoodLabel => T("AllGood");
    public string AutoResumeLabel => T("AutoResume");
    public string AutoResumeDescription => T("AutoResumeDescription");
    public string ResumeMessageLabel => T("ResumeMessage");
    public string ResetResumeMessageLabel => T("ResetResumeMessage");
    public string SafetyDelayLabel => T("SafetyDelay");
    public string LastCheckLabel => T("LastCheck");
    public string ModeLabel => T("Mode");
    public string TargetAppLabel => T("TargetApp");
    public string VersionLabel => T("Version");
    public string SettingsLabel => T("Settings");
    public string DiagnosticsLabel => T("Diagnostics");
    public string CaptureLimitLabel => T("CaptureLimit");
    public string MoreLabel => T("More");
    public string AboutLabel => T("About");
    public string PossibleLimitDiagnosticsLabel => T("PossibleLimitDiagnostics");
    public string ConfirmResumeLabel => T("ConfirmResume");
    public string ResumePolicyLabel => T("ResumePolicy");
    public string ResumePolicyState => Config.ResumePolicy switch
    {
        ResumePolicy.NotifyOnly => T("PolicyNotifyOnly"),
        ResumePolicy.ConfirmBeforeResume => T("PolicyConfirmBeforeResume"),
        ResumePolicy.AutomaticForegroundResume => T("PolicyAutomaticForegroundResume"),
        _ => T("PolicyConfirmBeforeResume")
    };
    public string PauseLabel => T("Pause");
    public string StartLabel => T("Start");
    public string StartButtonLabel => IsRunning ? T("Running") : T("Start");
    public string StartButtonIcon => IsRunning ? "\uE930" : "\uE768";
    public string StartButtonBackground => IsRunning ? "#DCFCE7" : "#FFFFFF";
    public string StartButtonBorder => IsRunning ? "#86EFAC" : "#D4E0EF";
    public string StartButtonForeground => IsRunning ? "#047857" : "#0F172A";
    public string StatusAccent => _monitor.State switch
    {
        AppState.WaitingForRetry => "#B7791F",
        AppState.ReadyToResume => "#2563EB",
        AppState.ResumeSending or AppState.Verifying => "#2563EB",
        AppState.Error or AppState.NeedsAttention => "#DC2626",
        AppState.WaitingForTarget => "#64748B",
        AppState.Paused => "#64748B",
        _ => "#16866A"
    };
    public string StatusDotBackground => StatusAccent;
    public string MinimizeLabel => T("Minimize");
    public string TipTitle => T("TipTitle");
    public string TipBody => T("TipBody");
    public string PrivacyLabel => T("Privacy");
    public string CatDisabledLabel => T("CatDisabled");
    public string SettingsTitle => T("SettingsTitle");
    public string GeneralLabel => T("General");
    public string AppearanceLabel => T("Appearance");
    public string AdvancedLabel => T("Advanced");
    public string StartWithWindowsLabel => T("StartWithWindows");
    public string CatBannerLabel => T("CatBanner");
    public string RefreshBannerLabel => T("RefreshBanner");
    public string RefreshBannerNowLabel => T("RefreshBannerNow");
    public string AlphaSafetyLabel => T("AlphaSafety");
    public string ExperimentalAutomationLabel => T("ExperimentalAutomation");
    public string DryRunLabel => T("DryRun");
    public string SendEnterLabel => T("SendEnter");
    public string AllowRealSubmitLabel => T("AllowRealSubmit");
    public string UiaDiscoveryLabel => T("UiaDiscovery");
    public string DeveloperTestLabel => T("DeveloperTest");
    public string WrongWindowProbeLabel => T("WrongWindowProbe");
    public string WaitingActivityLabel => T("WaitingActivity");
    public string TakeBreakLabel => T("TakeBreak");
    public string RecentWorkLabel => T("RecentWork");
    public string WorkSelectionScopeNotice => T("WorkSelectionScopeNotice");
    public string NoRecentWorkLabel => T("NoRecentWork");
    public string ShowAllLabel => _showAllRecentWork ? T("ShowLess") : T("ShowAll");
    public string RefreshWorkLabel => _isRefreshingWork ? T("RefreshingWork") : T("RefreshWork");
    public Visibility RecentWorkEmptyVisibility => RecentWorkSelections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowAllRecentWorkVisibility =>
        (_workSelectionStore.Load().Count(record => !record.IsStale) > 3
            || Environment.GetEnvironmentVariable("GPT_AUTO_RESUME_UI_PREVIEW_WORKS") == "1")
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility BannerVisibility => Config.CatBannerEnabled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BannerOffVisibility => Config.CatBannerEnabled ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CountdownVisibility => _monitor.State == AppState.WaitingForRetry ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NonCountdownVisibility => _monitor.State == AppState.WaitingForRetry ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ReadyButtonVisibility => _monitor.State == AppState.ReadyToResume ? Visibility.Visible : Visibility.Collapsed;
    public bool IsRunning => Config.AutoResume && _monitor.State != AppState.Paused;
    public string Remaining
    {
        get
        {
            if (_monitor.RetryAt is null)
            {
                return _previewRemaining ?? "";
            }

            if (!string.IsNullOrWhiteSpace(_previewRemaining))
            {
                return _previewRemaining;
            }

            var remain = _monitor.RetryAt.Value.AddSeconds(Config.ResumeDelaySeconds) - DateTimeOffset.Now;
            return remain <= TimeSpan.Zero ? T("ReadyShort") : remain.ToString(@"hh\:mm\:ss");
        }
    }
    public string ResumeStatus { get; private set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(Window owner)
    {
        _owner = owner;
        Config = _configStore.Load();
        _monitor = new MonitorService(Config);
        _lastState = _monitor.State;
        StartCommand = new RelayCommand(() => _ = StartMonitoringAsync());
        ConfirmResumeCommand = new RelayCommand(() => _ = TickMonitorAsync(confirmResume: true));
        PauseCommand = new RelayCommand(PauseMonitoring);
        MinimizeCommand = new RelayCommand(owner.Hide);
        DiscoveryCommand = new RelayCommand(RunDiscovery);
        TestModeCommand = new RelayCommand(RunDeveloperTestMode);
        SettingsCommand = new RelayCommand(ShowSettings);
        DiagnosticsCommand = new RelayCommand(ShowDiagnostics);
        CaptureLimitCommand = new RelayCommand(CaptureLimitMessage);
        WrongWindowProbeCommand = new RelayCommand(RunWrongWindowProbe);
        RefreshBannerCommand = new RelayCommand(() => _ = RefreshBannerNowAsync());
        OpenBannerLinkCommand = new RelayCommand(OpenBannerLink);
        OpenHomepageCommand = new RelayCommand(OpenHomepage);
        OpenPossibleLimitDiagnosticsCommand = new RelayCommand(OpenPossibleLimitDiagnostics);
        ToggleRecentWorkCommand = new RelayCommand(() =>
        {
            _showAllRecentWork = !_showAllRecentWork;
            RefreshRecentWorkSelections();
            RaiseAll();
        });
        RefreshWorkCommand = new RelayCommand(() => _ = RefreshWorkAsync(autoEnable: false));
        RefreshQuotaCommand = new RelayCommand(() => _ = RefreshQuotaAsync(allowUiInteraction: true, allowControlledForeground: true));
        ResetResumeMessageCommand = new RelayCommand(() =>
        {
            ResumeMessageLocalizer.ResetToDefault(Config);
            _configStore.Save(Config);
            RaiseAll();
        });

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "GPT Auto Resume",
            Icon = LoadTrayIcon(),
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(owner)
        };
        _trayIcon.DoubleClick += (_, _) => Show(owner);

        var preview = Environment.GetEnvironmentVariable("GPT_AUTO_RESUME_UI_PREVIEW");
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GPT_AUTO_RESUME_UI_LANG")))
        {
            ResumeMessageLocalizer.ApplyLanguage(Config, Environment.GetEnvironmentVariable("GPT_AUTO_RESUME_UI_LANG")!);
        }

        _previewRemaining = Environment.GetEnvironmentVariable("GPT_AUTO_RESUME_PREVIEW_REMAINING");
        _previewRetryAt = Environment.GetEnvironmentVariable("GPT_AUTO_RESUME_PREVIEW_RETRY");
        var isPreview = !string.IsNullOrWhiteSpace(preview);
        if (isPreview)
        {
            var retry = DateTimeOffset.Now.AddHours(3).AddMinutes(39).AddSeconds(4);
            _monitor.SetPreviewState(preview!.Equals("waiting", StringComparison.OrdinalIgnoreCase)
                ? AppState.WaitingForRetry
                : AppState.Monitoring, retry);
        }

        _timer.Interval = TimeSpan.FromSeconds(Math.Max(5, Config.ScanIntervalSeconds));
        _timer.Tick += async (_, _) => await TickMonitorAsync();
        _targetPresenceTimer.Interval = TimeSpan.FromSeconds(10);
        _targetPresenceTimer.Tick += async (_, _) => await CheckTargetPresenceAsync();
        if (!isPreview)
        {
            _timer.Start();
            _targetPresenceTimer.Start();
        }

        _ = RefreshBannerAsync(force: true);
        RefreshRecentWorkSelections();
        if (!isPreview)
        {
            _ = TickMonitorAsync();
            _ = RefreshWorkAsync(autoEnable: false);
            StartTargetQuotaRefreshSequence();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _configStore.Save(Config);
        _timer.Stop();
        _targetPresenceTimer.Stop();
        _targetQuotaRefreshSource?.Cancel();
        _targetQuotaRefreshSource?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Icon = null;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
    }

    private Forms.ContextMenuStrip BuildTrayMenu(Window owner)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Show(owner));
        menu.Items.Add("Pause Monitoring", null, (_, _) => _monitor.Pause());
        menu.Items.Add("Resume Monitoring", null, (_, _) => _monitor.Start());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            Dispose();
            WpfApplication.Current.Shutdown();
        });
        return menu;
    }

    private static void Show(Window owner)
    {
        owner.Show();
        owner.WindowState = WindowState.Normal;
        owner.Activate();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(MoodText));
        OnPropertyChanged(nameof(MainStatus));
        OnPropertyChanged(nameof(MainMessage));
        OnPropertyChanged(nameof(SubMessage));
        OnPropertyChanged(nameof(PrimaryStatus));
        OnPropertyChanged(nameof(SecondaryStatus));
        OnPropertyChanged(nameof(ModeBadge));
        OnPropertyChanged(nameof(BannerImage));
        OnPropertyChanged(nameof(BannerVisibility));
        OnPropertyChanged(nameof(BannerOffVisibility));
        OnPropertyChanged(nameof(AutoResumeState));
        OnPropertyChanged(nameof(AutoResumeEnabled));
        OnPropertyChanged(nameof(ResumeText));
        OnPropertyChanged(nameof(DryRunState));
        OnPropertyChanged(nameof(SafetyDelay));
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(LastCheck));
        OnPropertyChanged(nameof(QuotaProbeText));
        OnPropertyChanged(nameof(QuotaProbeTooltip));
        OnPropertyChanged(nameof(UsageLimit));
        OnPropertyChanged(nameof(RetryAt));
        OnPropertyChanged(nameof(RetryAtInline));
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(ResumeStatus));
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(Tagline));
        OnPropertyChanged(nameof(StudioPresents));
        OnPropertyChanged(nameof(HeroTitle));
        OnPropertyChanged(nameof(ProductSubtitle));
        OnPropertyChanged(nameof(ByBrand));
        OnPropertyChanged(nameof(HeroNote));
        OnPropertyChanged(nameof(HeroDescription));
        OnPropertyChanged(nameof(ResumeAtLabel));
        OnPropertyChanged(nameof(RemainingLabel));
        OnPropertyChanged(nameof(AllGoodLabel));
        OnPropertyChanged(nameof(AutoResumeLabel));
        OnPropertyChanged(nameof(AutoResumeDescription));
        OnPropertyChanged(nameof(ResumeMessageLabel));
        OnPropertyChanged(nameof(ResetResumeMessageLabel));
        OnPropertyChanged(nameof(SafetyDelayLabel));
        OnPropertyChanged(nameof(LastCheckLabel));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(TargetAppLabel));
        OnPropertyChanged(nameof(VersionLabel));
        OnPropertyChanged(nameof(SettingsLabel));
        OnPropertyChanged(nameof(DiagnosticsLabel));
        OnPropertyChanged(nameof(CaptureLimitLabel));
        OnPropertyChanged(nameof(MoreLabel));
        OnPropertyChanged(nameof(AboutLabel));
        OnPropertyChanged(nameof(PossibleLimitDiagnosticsLabel));
        OnPropertyChanged(nameof(ConfirmResumeLabel));
        OnPropertyChanged(nameof(ResumePolicyLabel));
        OnPropertyChanged(nameof(ResumePolicyState));
        OnPropertyChanged(nameof(ResumePolicies));
        OnPropertyChanged(nameof(PauseLabel));
        OnPropertyChanged(nameof(StartLabel));
        OnPropertyChanged(nameof(StartButtonLabel));
        OnPropertyChanged(nameof(StartButtonIcon));
        OnPropertyChanged(nameof(StartButtonBackground));
        OnPropertyChanged(nameof(StartButtonBorder));
        OnPropertyChanged(nameof(StartButtonForeground));
        OnPropertyChanged(nameof(StatusAccent));
        OnPropertyChanged(nameof(StatusDotBackground));
        OnPropertyChanged(nameof(CountdownVisibility));
        OnPropertyChanged(nameof(NonCountdownVisibility));
        OnPropertyChanged(nameof(ReadyButtonVisibility));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(MinimizeLabel));
        OnPropertyChanged(nameof(TipTitle));
        OnPropertyChanged(nameof(TipBody));
        OnPropertyChanged(nameof(PrivacyLabel));
        OnPropertyChanged(nameof(CatDisabledLabel));
        OnPropertyChanged(nameof(SettingsTitle));
        OnPropertyChanged(nameof(GeneralLabel));
        OnPropertyChanged(nameof(AppearanceLabel));
        OnPropertyChanged(nameof(AdvancedLabel));
        OnPropertyChanged(nameof(StartWithWindowsLabel));
        OnPropertyChanged(nameof(CatBannerLabel));
        OnPropertyChanged(nameof(RefreshBannerLabel));
        OnPropertyChanged(nameof(RefreshBannerNowLabel));
        OnPropertyChanged(nameof(AlphaSafetyLabel));
        OnPropertyChanged(nameof(ExperimentalAutomationLabel));
        OnPropertyChanged(nameof(DryRunLabel));
        OnPropertyChanged(nameof(SendEnterLabel));
        OnPropertyChanged(nameof(AllowRealSubmitLabel));
        OnPropertyChanged(nameof(UiaDiscoveryLabel));
        OnPropertyChanged(nameof(DeveloperTestLabel));
        OnPropertyChanged(nameof(WrongWindowProbeLabel));
        OnPropertyChanged(nameof(WaitingActivityLabel));
        OnPropertyChanged(nameof(TakeBreakLabel));
        OnPropertyChanged(nameof(RecentWorkLabel));
        OnPropertyChanged(nameof(WorkSelectionScopeNotice));
        OnPropertyChanged(nameof(NoRecentWorkLabel));
        OnPropertyChanged(nameof(ShowAllLabel));
        OnPropertyChanged(nameof(RefreshWorkLabel));
        OnPropertyChanged(nameof(RecentWorkEmptyVisibility));
        OnPropertyChanged(nameof(ShowAllRecentWorkVisibility));
    }

    private async Task RefreshWorkAsync(bool autoEnable)
    {
        if (_isRefreshingWork)
        {
            return;
        }

        _isRefreshingWork = true;
        ResumeStatus = T("RefreshingWork");
        RaiseAll();

        try
        {
            var result = await _workRefreshGate.RunAsync(
                token => Task.Run(() => DiscoverVisibleWork(token), token),
                TimeSpan.FromSeconds(12));
            if (result.Status == BackgroundOperationStatus.AlreadyRunning)
            {
                ResumeStatus = T("RefreshingWork");
                return;
            }

            if (result.Status == BackgroundOperationStatus.TimedOut)
            {
                ResumeStatus = T("WorkRefreshTimedOut");
                return;
            }

            if (result.Status != BackgroundOperationStatus.Completed || result.Value is null)
            {
                ResumeStatus = T("CurrentWorkNotFound");
                return;
            }

            var records = result.Value;
            foreach (var record in records)
            {
                _workSelectionStore.UpsertRecent(record.DisplayTitle, record.Identity, record.SeenAt);
                if (autoEnable)
                {
                    _workSelectionStore.SetEnabled(JsonWorkSelectionStore.BuildHash(record.Identity), true);
                }
            }

            ResumeStatus = records.Count > 0
                ? string.Format(T("WorkRefreshComplete"), records.Count)
                : T("CurrentWorkNotFound");
        }
        catch
        {
            ResumeStatus = T("CurrentWorkNotFound");
        }
        finally
        {
            _isRefreshingWork = false;
            RefreshRecentWorkSelections();
            RaiseAll();
        }
    }

    private void RefreshRecentWorkSelections()
    {
        if (Environment.GetEnvironmentVariable("GPT_AUTO_RESUME_UI_PREVIEW_WORKS") == "1")
        {
            RecentWorkSelections.Clear();
            var now = DateTimeOffset.Now;
            var previewRecords = new[]
            {
                new WorkSelectionRecord("preview-work-1", "Example Developer API", true, now),
                new WorkSelectionRecord("preview-work-2", "GPT Auto Resume", true, now.AddSeconds(-1)),
                new WorkSelectionRecord("preview-work-3", "Article Factory", false, now.AddSeconds(-2)),
                new WorkSelectionRecord("preview-work-4", "Example Media Generator", false, now.AddSeconds(-3))
            }.Take(_showAllRecentWork ? 5 : 3);
            foreach (var record in previewRecords)
            {
                RecentWorkSelections.Add(new WorkSelectionViewModel(record, new PreviewWorkSelectionStore()));
            }

            OnPropertyChanged(nameof(RecentWorkEmptyVisibility));
            return;
        }

        var records = _workSelectionStore.Load()
            .Where(record => !record.IsStale)
            .Where(record => !IsGenericTitle(record.DisplayTitle))
            .OrderByDescending(record => record.LastSeenAt)
            .Take(5)
            .Take(_showAllRecentWork ? 5 : 3)
            .ToList();

        RecentWorkSelections.Clear();
        foreach (var record in records)
        {
            RecentWorkSelections.Add(new WorkSelectionViewModel(record with
            {
                DisplayTitle = DisplayWorkTitle(record.DisplayTitle)
            }, _workSelectionStore));
        }

        OnPropertyChanged(nameof(RecentWorkEmptyVisibility));
        OnPropertyChanged(nameof(ShowAllRecentWorkVisibility));
    }

    private static IReadOnlyList<DiscoveredWork> DiscoverVisibleWork(CancellationToken cancellationToken)
    {
        try
        {
            return RecentWorkDiscovery.Discover(new WindowScanner(), new UiAutomationReader(), cancellationToken: cancellationToken).Works;
        }
        catch
        {
            return [];
        }
    }

    private async Task RefreshQuotaAsync(
        bool allowUiInteraction,
        bool allowControlledForeground,
        CancellationToken cancellationToken = default)
    {
        _quotaProbe = new QuotaProbeDisplayState(QuotaProbeStatus.Reading, LastReadAt: DateTimeOffset.Now);
        RaiseQuotaProbe();

        var result = await _quotaProbeGate.RunAsync(
            token => Task.Run(() => QuotaProbe.Run(allowUiInteraction, allowControlledForeground, token), token),
            TimeSpan.FromSeconds(12),
            cancellationToken);

        if (result.Status == BackgroundOperationStatus.AlreadyRunning)
        {
            return;
        }

        if (result.Status != BackgroundOperationStatus.Completed || result.Value is null)
        {
            _quotaProbe = new QuotaProbeDisplayState(QuotaProbeStatus.NotRead, LastReadAt: DateTimeOffset.Now, Source: "Codex signed-in account");
            RaiseQuotaProbe();
            return;
        }

        var probe = result.Value;
        _quotaProbe = new QuotaProbeDisplayState(probe.Status, probe.Snapshot, probe.CapturedAt, "Codex signed-in account");
        RaiseQuotaProbe();
    }

    private void StartTargetQuotaRefreshSequence()
    {
        _targetQuotaRefreshSource?.Cancel();
        _targetQuotaRefreshSource?.Dispose();
        _targetQuotaRefreshSource = new CancellationTokenSource();
        _ = RefreshQuotaAfterTargetAppearsAsync(_targetQuotaRefreshSource.Token);
    }

    private async Task RefreshQuotaAfterTargetAppearsAsync(CancellationToken cancellationToken)
    {
        var delays = new[]
        {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(12),
            TimeSpan.FromSeconds(25)
        };

        foreach (var delay in delays)
        {
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                await RefreshQuotaAsync(
                    allowUiInteraction: true,
                    allowControlledForeground: true,
                    cancellationToken);
                if (_quotaProbe.Status is QuotaProbeStatus.Complete or QuotaProbeStatus.Partial)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void RaiseQuotaProbe()
    {
        OnPropertyChanged(nameof(QuotaProbeText));
        OnPropertyChanged(nameof(QuotaProbeTooltip));
    }

    private async Task TickMonitorAsync(bool confirmResume = false)
    {
        var result = await _monitorTickGate.RunAsync(
            token => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (confirmResume) _monitor.ConfirmResume(DateTimeOffset.Now, token);
                else _monitor.Tick(DateTimeOffset.Now, token);
                return _monitor.State;
            }, token),
            TimeSpan.FromSeconds(12));
        if (result.Status != BackgroundOperationStatus.Completed)
        {
            new ProductionEventLog().Write(new ProductionTrace(DateTimeOffset.Now, "MONITOR_PROVIDER", result.Status.ToString()));
            ResumeStatus = result.Status == BackgroundOperationStatus.TimedOut
                ? "Monitor scan timed out."
                : ResumeStatus;
            RaiseAll();
            return;
        }

        if (_lastState != _monitor.State)
        {
            _lastState = _monitor.State;
            _ = RefreshBannerAsync(force: true);
        }

        var account = _monitor.AccountQuota;
        _quotaProbe = new QuotaProbeDisplayState(account is null ? QuotaProbeStatus.NotRead
            : account.ShortWindowResetAt is not null && account.WeeklyResetAt is not null
                && account.ShortWindowRemainingPercent is not null && account.WeeklyRemainingPercent is not null
                ? QuotaProbeStatus.Complete : QuotaProbeStatus.Partial,
            account, account?.CapturedAt, "Codex signed-in account");
        RaiseQuotaProbe();
        RaiseAll();
        _configStore.Save(Config);
        ResumeStatus = _monitor.ResumeStatusText;
        OnPropertyChanged(nameof(ResumeStatus));
        RefreshRecentWorkSelections();
    }

    private async Task CheckTargetPresenceAsync()
    {
        var result = await _targetPresenceGate.RunAsync(
            token => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return new WindowScanner().FindTargets().Count > 0;
            }, token),
            TimeSpan.FromSeconds(5));

        if (result.Status != BackgroundOperationStatus.Completed)
        {
            return;
        }

        var hasTarget = result.Value;
        if (_lastTargetPresent is not null && _lastTargetPresent == hasTarget)
        {
            return;
        }

        _lastTargetPresent = hasTarget;
        await TickMonitorAsync();
        if (hasTarget)
        {
            _ = RefreshWorkAsync(autoEnable: false);
            StartTargetQuotaRefreshSequence();
        }
        else
        {
            _targetQuotaRefreshSource?.Cancel();
        }
    }

    private static bool IsGenericTitle(string title) =>
        string.Equals(title.Trim(), "ChatGPT", StringComparison.OrdinalIgnoreCase)
        || string.Equals(title.Trim(), "Codex", StringComparison.OrdinalIgnoreCase)
        || string.Equals(title.Trim(), "ChatGPT / Codex", StringComparison.OrdinalIgnoreCase);

    private string DisplayWorkTitle(string title) =>
        title is "__unnamed_work__" or "目前 ChatGPT/Codex 工作"
            ? T("UnnamedWork")
            : title;

    private void RunDiscovery()
    {
        var root = FindProjectRoot();
        var report = DiscoveryTool.CaptureToFile(root);
        ResumeStatus = report.WindowFound
            ? $"Discovery: window found. Process: {report.ProcessName}, HWND: {report.WindowHandle}, Editable: {(report.EditableControlFound ? "FOUND" : "NOT FOUND")}, Message text: {report.MessageTextAccessStatus}"
            : "Discovery: no ChatGPT/Codex window found.";
        RaiseAll();
    }

    private async Task StartMonitoringAsync()
    {
        Config.AutoResume = true;
        _monitor.Start();
        _configStore.Save(Config);
        ResumeStatus = "Monitoring started.";
        _ = RefreshBannerAsync(force: true);
        RaiseAll();
        await TickMonitorAsync();
    }

    private void PauseMonitoring()
    {
        _monitor.Pause();
        _configStore.Save(Config);
        ResumeStatus = "Monitoring paused.";
        RaiseAll();
    }

    private void CaptureLimitMessage()
    {
        var root = FindProjectRoot();
        var path = LimitMessageCapture.Capture(root);
        ResumeStatus = File.Exists(path)
            ? File.ReadAllText(path)
            : "Capture did not produce a diagnostics report.";
        RaiseAll();
    }

    private void RunDeveloperTestMode()
    {
        var detector = new UsageLimitDetector(PatternCatalog.LoadDefault(), new RetryTimeParser());
        var samples = new[]
        {
            "你已達使用上限，請於晚上 8:35 再試一次",
            "You've reached your usage limit. Try again after Aug 31, 2026 at 8:35 PM.",
            "使用上限に到達しました。20:35 に再試行してください。"
        };
        var now = DateTimeOffset.Now;
        var lines = samples.Select(sample =>
        {
            var result = detector.Analyze(sample, now, hasWarningRole: true);
            return $"{result.DetectedLanguage ?? "unknown"} {result.Kind} confidence={result.Confidence} retry={result.RetryAt:yyyy/MM/dd HH:mm}";
        });
        ResumeStatus = "Developer Test Mode: " + string.Join(" | ", lines);
        RaiseAll();
    }

    private void ShowSettings()
    {
        var window = new SettingsWindow { Owner = _owner, DataContext = this };
        window.ShowDialog();
        _configStore.Save(Config);
        _ = RefreshBannerAsync(force: true);
        RaiseAll();
    }

    private void ShowDiagnostics()
    {
        var window = new DiagnosticsWindow { Owner = _owner, DataContext = this };
        window.ShowDialog();
    }

    private void RunWrongWindowProbe()
    {
        var root = FindProjectRoot();
        WrongWindowProbe.Run(root);
        var reportPath = Path.Combine(root, "diagnostics", "wrong-window-probe.txt");
        ResumeStatus = File.Exists(reportPath) ? File.ReadAllText(reportPath) : "Wrong-window probe did not produce a diagnostics report.";
        RaiseAll();
    }

    private void OpenHomepage()
    {
        Process.Start(new ProcessStartInfo(AppUrls.EasyLifeHubHome) { UseShellExecute = true });
    }

    private void OpenBannerLink()
    {
        Process.Start(new ProcessStartInfo(AppUrls.BannerArtworkLink) { UseShellExecute = true });
    }

    private static void OpenPossibleLimitDiagnostics()
    {
        var directory = LocalAppPaths.PossibleLimitDiagnosticsDirectory;
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe")
        {
            Arguments = $"\"{directory}\"",
            UseShellExecute = true
        });
    }

    private async Task RefreshBannerAsync(bool force)
    {
        if (!force && !Config.CatBannerEnabled)
        {
            return;
        }

        const string fallback = "pack://application:,,,/Assets/fallback-cat-banner.png";
        var image = await _bannerService.GetBannerAsync(Config.CatBannerEnabled, Config.BannerRefreshMinutes, fallback, force);
        if (!image.StartsWith("pack://", StringComparison.OrdinalIgnoreCase) && !File.Exists(image))
        {
            return;
        }

        BannerImage = image;
        OnPropertyChanged(nameof(BannerImage));
    }

    private async Task RefreshBannerNowAsync()
    {
        await RefreshBannerAsync(force: true);
        ResumeStatus = "Banner refreshed.";
        RaiseAll();
    }

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, "GPTAutoResume.sln")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        return Environment.CurrentDirectory;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private string T(string key) => Localization.Get(Config.Language, key);

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app-icon.ico"));
        return resource is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(resource.Stream);
    }

    private string FormatState(AppState state) => state switch
    {
        AppState.Monitoring => T("Monitoring"),
        AppState.WaitingForTarget => T("WaitingForTargetState"),
        AppState.LimitDetected => T("LimitDetected"),
        AppState.WaitingForRetry => T("Waiting"),
        AppState.ReadyToResume => T("ReadyToResume"),
        AppState.PreResumeVerify => T("CheckingWindow"),
        AppState.ResumeSending => T("Sending"),
        AppState.Verifying => T("Verifying"),
        AppState.ResumeSuccess => T("ResumeSuccess"),
        AppState.NeedsAttention => T("NeedsAttention"),
        AppState.Paused => T("Paused"),
        _ => T("Error")
    };

    public void SetUiPreviewState(AppState state, DateTimeOffset? retryAt = null)
    {
        _monitor.SetPreviewState(state, retryAt);
        RaiseAll();
    }
}

public sealed class PreviewWorkSelectionStore : IWorkSelectionStore
{
    public IReadOnlyList<WorkSelectionRecord> Load() => [];
    public void Save(IReadOnlyList<WorkSelectionRecord> records) { }
    public bool IsAutoResumeEnabled(ConversationTargetIdentity? identity) => false;
    public void UpsertRecent(string displayTitle, ConversationTargetIdentity identity, DateTimeOffset seenAt) { }
    public void RenameDisplayName(string conversationIdentityHash, string displayTitle) { }
    public void SetEnabled(string conversationIdentityHash, bool enabled) { }
}

public sealed record ResumePolicyOption(ResumePolicy Value, string DisplayName);

public sealed class WorkSelectionViewModel : INotifyPropertyChanged
{
    private readonly IWorkSelectionStore _store;
    private bool _isSelected;
    private string _displayTitle;

    public string ConversationIdentityHash { get; }
    public string DisplayTitle
    {
        get => _displayTitle;
        set
        {
            var trimmed = value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || string.Equals(_displayTitle, trimmed, StringComparison.Ordinal))
            {
                return;
            }

            _displayTitle = trimmed;
            _store.RenameDisplayName(ConversationIdentityHash, trimmed);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            _store.SetEnabled(ConversationIdentityHash, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkSelectionViewModel(WorkSelectionRecord record, IWorkSelectionStore store)
    {
        _store = store;
        ConversationIdentityHash = record.ConversationIdentityHash;
        _displayTitle = record.DisplayTitle;
        _isSelected = record.AutoResumeEnabled;
    }
}

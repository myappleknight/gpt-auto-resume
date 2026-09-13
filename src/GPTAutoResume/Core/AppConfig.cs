namespace GPTAutoResume.Core;

public sealed class AppConfig
{
    public bool AutoResume { get; set; } = true;
    public ResumePolicy ResumePolicy { get; set; } = ResumePolicy.AutomaticForegroundResume;
    public string Language { get; set; } = "zh-TW";
    public string ResumeText { get; set; } = "請繼續";
    public ResumeMessageMode ResumeMessageMode { get; set; } = ResumeMessageMode.Default;
    public int ResumeDelaySeconds { get; set; } = 30;
    public int ScanIntervalSeconds { get; set; } = 25;
    public bool StartWithWindows { get; set; }
    public bool CatBannerEnabled { get; set; } = true;
    public int BannerRefreshMinutes { get; set; } = 45;
    public bool DryRun { get; set; }
    public bool SendEnter { get; set; } = true;
    public bool AllowRealSubmit { get; set; } = true;
    public bool RequireWorkSelection { get; set; } = true;
}

public enum ResumePolicy
{
    NotifyOnly,
    ConfirmBeforeResume,
    AutomaticForegroundResume
}

public enum ResumeMessageMode
{
    Default,
    Custom
}

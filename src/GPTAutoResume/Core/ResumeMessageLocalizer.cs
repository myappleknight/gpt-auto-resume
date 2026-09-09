namespace GPTAutoResume.Core;

public static class ResumeMessageLocalizer
{
    public static void ApplyLanguage(AppConfig config, string language)
    {
        config.Language = language;
        if (config.ResumeMessageMode == ResumeMessageMode.Default)
        {
            config.ResumeText = Localization.DefaultResumeText(language);
        }
    }

    public static void SetCustom(AppConfig config, string value)
    {
        config.ResumeText = value;
        config.ResumeMessageMode = ResumeMessageMode.Custom;
    }

    public static void ResetToDefault(AppConfig config)
    {
        config.ResumeMessageMode = ResumeMessageMode.Default;
        config.ResumeText = Localization.DefaultResumeText(config.Language);
    }
}

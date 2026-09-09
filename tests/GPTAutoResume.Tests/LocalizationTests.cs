using GPTAutoResume;
using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void DefaultResumeTextFollowsLanguage()
    {
        Assert.Equal("請繼續", Localization.DefaultResumeText("zh-TW"));
        Assert.Equal("Please continue", Localization.DefaultResumeText("en"));
        Assert.Equal("続けてください", Localization.DefaultResumeText("ja"));
    }

    [Theory]
    [InlineData("請繼續", true)]
    [InlineData("Please continue", true)]
    [InlineData("続けてください", true)]
    [InlineData("幫我繼續這個任務", false)]
    public void DetectsBuiltInResumeText(string value, bool expected)
    {
        Assert.Equal(expected, Localization.IsDefaultResumeText(value));
    }

    [Theory]
    [InlineData("zh-TW", "請繼續")]
    [InlineData("en", "Please continue")]
    [InlineData("ja", "続けてください")]
    public void DefaultModeSwitchesResumeTextWithLanguage(string language, string expected)
    {
        var config = new AppConfig { ResumeMessageMode = ResumeMessageMode.Default };

        ResumeMessageLocalizer.ApplyLanguage(config, language);

        Assert.Equal(language, config.Language);
        Assert.Equal(expected, config.ResumeText);
        Assert.Equal(ResumeMessageMode.Default, config.ResumeMessageMode);
    }

    [Fact]
    public void CustomModeDoesNotOverwriteResumeTextWhenLanguageChanges()
    {
        var config = new AppConfig
        {
            ResumeMessageMode = ResumeMessageMode.Custom,
            ResumeText = "請繼續完成剩下工作並自行測試"
        };

        ResumeMessageLocalizer.ApplyLanguage(config, "en");

        Assert.Equal("en", config.Language);
        Assert.Equal("請繼續完成剩下工作並自行測試", config.ResumeText);
        Assert.Equal(ResumeMessageMode.Custom, config.ResumeMessageMode);
    }

    [Fact]
    public void ResetToDefaultUsesCurrentLanguage()
    {
        var config = new AppConfig
        {
            Language = "ja",
            ResumeMessageMode = ResumeMessageMode.Custom,
            ResumeText = "custom"
        };

        ResumeMessageLocalizer.ResetToDefault(config);

        Assert.Equal("続けてください", config.ResumeText);
        Assert.Equal(ResumeMessageMode.Default, config.ResumeMessageMode);
    }

    [Theory]
    [InlineData("zh-TW", "目前開啟")]
    [InlineData("en", "currently open")]
    [InlineData("ja", "現在開いて")]
    public void WorkSelectionScopeStatesActiveWorkLimit(string language, string expectedPhrase)
    {
        Assert.Contains(expectedPhrase, Localization.Get(language, "WorkSelectionScopeNotice"), StringComparison.OrdinalIgnoreCase);
    }
}

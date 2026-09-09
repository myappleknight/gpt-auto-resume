using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class UsageLimitDetectorTests
{
    private readonly DateTimeOffset _now = new(2026, 8, 31, 18, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void DetectsHighConfidenceTraditionalChineseUsageLimit()
    {
        var detector = NewDetector();
        var result = detector.Analyze("你已達使用上限。請升級方案，或於晚上 7:54 再試一次。", _now, hasWarningRole: true);

        Assert.Equal(DetectionKind.LimitDetected, result.Kind);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 19, 54, 0, TimeSpan.FromHours(8)), result.RetryAt);
        Assert.True(result.Confidence >= 75);
    }

    [Fact]
    public void IgnoresNetworkErrorEvenWithWarningIcon()
    {
        var detector = NewDetector();
        var result = detector.Analyze("⚠ Network error. Please check your connection.", _now, hasWarningRole: true, hasWarningIcon: true);

        Assert.Equal(DetectionKind.Ignore, result.Kind);
        Assert.Null(result.RetryAt);
    }

    [Fact]
    public void TreatsTimeAndRetryWithoutLimitAsPossibleOnly()
    {
        var detector = NewDetector();
        var result = detector.Analyze("Try again after Sep 1, 2026 at 2:10 AM.", _now);

        Assert.NotEqual(DetectionKind.LimitDetected, result.Kind);
    }

    [Fact]
    public void DetectsEnglishCodexRelativeUsageLimit()
    {
        var detector = NewDetector();
        var result = detector.Analyze("You've hit your usage limit. Upgrade to Pro or try again in 2 days 22 hours 51 minutes.", _now, hasWarningIcon: true);

        Assert.Equal(DetectionKind.LimitDetected, result.Kind);
        Assert.Equal(_now.AddDays(2).AddHours(22).AddMinutes(51), result.RetryAt);
    }

    [Fact]
    public void DetectsProductionTraditionalChineseCreditsWording()
    {
        var detector = NewDetector();
        var result = detector.AnalyzeForAutomation("你已達使用上限。請升級方案或加值點數以繼續，或於 清晨6:14 再試一次。", _now);

        Assert.Equal(DetectionKind.LimitDetected, result.Kind);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 6, 14, 0, TimeSpan.FromHours(8)), result.RetryAt);
    }

    [Fact]
    public void DetectsProductionCodexWorkUsageWithChineseTime()
    {
        var detector = NewDetector();
        var result = detector.AnalyzeForAutomation("You're out of Codex and Work usage. Add credits or upgrade your plan — or wait for usage to reset on 清晨6:14", _now);

        Assert.Equal(DetectionKind.LimitDetected, result.Kind);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 6, 14, 0, TimeSpan.FromHours(8)), result.RetryAt);
        Assert.Contains("en.out_of_usage_regex", result.MatchedPatternIds);
    }

    [Fact]
    public void DetectsScreenshotDerivedCodexWorkUsageExhaustedWording()
    {
        var detector = NewDetector();
        var result = detector.AnalyzeForAutomation("你的 Codex 和工作使用量已用完。新增點數或升級方案，或等到 清晨7:28 用量重置", _now);

        Assert.Equal(DetectionKind.LimitDetected, result.Kind);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 7, 28, 0, TimeSpan.FromHours(8)), result.RetryAt);
        Assert.Contains("zh_tw.usage_exhausted_regex", result.MatchedPatternIds);
    }

    [Fact]
    public void DetectsScreenshotDerivedCodexWorkUsageWhenUiaSplitsNodes()
    {
        var detector = NewDetector();
        var result = detector.AnalyzeForAutomation(
            """
            你的 Codex
            和工作使用量
            已用完
            新增點數或升級方案，或等到 清晨7:28 用量重置
            """,
            _now);

        Assert.Equal(DetectionKind.LimitDetected, result.Kind);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 7, 28, 0, TimeSpan.FromHours(8)), result.RetryAt);
    }

    [Fact]
    public void DetectsLimitWithoutRetryTimeAsKnownLimitNotMonitoring()
    {
        var detector = NewDetector();
        var result = detector.AnalyzeForAutomation("你的 Codex 和工作使用量已用完。新增點數或升級方案。", _now);

        Assert.Equal(DetectionKind.LimitDetected, result.Kind);
        Assert.Null(result.RetryAt);
    }

    [Fact]
    public void ConversationDiscussingUsageLimitIsPossibleOnlyWithoutUiContext()
    {
        var detector = NewDetector();
        var text = string.Join("\n", Enumerable.Repeat("normal conversation text", 38))
            + "\nUser: Can you explain the phrase \"You've reached your usage limit. Try again after Sep 1, 2026 at 2:10 AM.\"?";

        var result = detector.AnalyzeForAutomation(text, _now);

        Assert.Equal(DetectionKind.PossibleLimit, result.Kind);
    }

    [Fact]
    public void LongVisiblePageWithoutWarningContextIsPossibleOnly()
    {
        var detector = NewDetector();
        var longPage = string.Join("\n", Enumerable.Repeat("normal conversation text", 40))
            + "\nYou've reached your usage limit. Try again after Sep 1, 2026 at 2:10 AM.";

        var result = detector.AnalyzeForAutomation(longPage, _now);

        Assert.Equal(DetectionKind.PossibleLimit, result.Kind);
    }

    [Fact]
    public void JapaneseSpecificSignalsWinOverSharedChineseCharacters()
    {
        var detector = new UsageLimitDetector(PatternCatalog.LoadDefault(), new RetryTimeParser());

        var result = detector.Analyze("使用上限に到達しました。20:35 に再試行してください。", _now, hasWarningRole: true);

        Assert.Equal(DetectionKind.LimitDetected, result.Kind);
        Assert.Equal("ja", result.DetectedLanguage);
    }

    private static UsageLimitDetector NewDetector()
    {
        var catalog = new PatternCatalog
        {
            Languages = new List<LanguagePattern>
            {
                new()
                {
                    Language = "en",
                    UsageLimit =
                    [
                        new PatternEntry { Id = "en.usage_limit", Text = "usage limit" },
                        new PatternEntry { Id = "en.out_of_usage", Text = "out of Codex and Work usage" },
                        new PatternEntry { Id = "en.add_credits", Text = "add credits" },
                        new PatternEntry { Id = "en.upgrade", Text = "upgrade" }
                    ],
                    Retry =
                    [
                        new PatternEntry { Id = "en.try_again", Text = "try again" },
                        new PatternEntry { Id = "en.usage_to_reset", Text = "usage to reset" },
                        new PatternEntry { Id = "en.wait_for_usage_reset", Text = "wait for usage to reset" }
                    ]
                },
                new()
                {
                    Language = "zh-TW",
                    UsageLimit =
                    [
                        new PatternEntry { Id = "zh_tw.usage_limit", Text = "使用上限" },
                        new PatternEntry { Id = "zh_tw.usage_exhausted", Text = "使用量已用完" },
                        new PatternEntry { Id = "zh_tw.codex_work_usage_exhausted", Text = "Codex 和工作使用量已用完" },
                        new PatternEntry { Id = "zh_tw.upgrade_plan", Text = "升級方案" },
                        new PatternEntry { Id = "zh_tw.add_credits", Text = "加值點數" }
                    ],
                    Retry =
                    [
                        new PatternEntry { Id = "zh_tw.try_again", Text = "再試一次" },
                        new PatternEntry { Id = "zh_tw.reset", Text = "重置" }
                    ]
                }
            }
        };
        return new UsageLimitDetector(catalog, new RetryTimeParser());
    }
}

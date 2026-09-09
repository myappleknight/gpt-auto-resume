using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class QuotaProbeDisplayTests
{
    [Fact]
    public void OldQuotaIsNotPresentedAsCurrent()
    {
        var captured = DateTimeOffset.Parse("2026-09-08T07:30:00+08:00");
        var snapshot = new QuotaSnapshot(96, captured.AddHours(1), 61, captured.AddDays(5), captured);
        var state = new QuotaProbeDisplayState(QuotaProbeStatus.Complete, snapshot, captured);
        Assert.Equal("額度：待更新", QuotaProbeDisplay.FormatCurrent(state, "zh-TW", captured.AddMinutes(6), false));
        Assert.Equal("額度：已達上限", QuotaProbeDisplay.FormatCurrent(state, "zh-TW", captured.AddMinutes(6), true));
        Assert.Contains("07:30", QuotaProbeDisplay.FormatCurrent(state, "zh-TW", captured.AddSeconds(30), false));
    }

    private readonly DateTimeOffset _now = new(2026, 9, 7, 22, 41, 0, TimeSpan.FromHours(8));

    [Theory]
    [InlineData("zh-TW", "額度：未讀取")]
    [InlineData("en", "Quota: Not read")]
    [InlineData("ja", "使用量：未取得")]
    public void FormatsNotReadState(string language, string expected)
    {
        var state = new QuotaProbeDisplayState(QuotaProbeStatus.NotRead);

        Assert.Equal(expected, QuotaProbeDisplay.FormatInline(state, language));
    }

    [Theory]
    [InlineData("zh-TW", "額度：0% · 07:28 重置")]
    [InlineData("en", "Quota: 0% · resets 07:28")]
    [InlineData("ja", "使用量：0% · 07:28 リセット")]
    public void FormatsShortWindowResetWhenReadable(string language, string expected)
    {
        var snapshot = new QuotaSnapshot(
            ShortWindowRemainingPercent: 0,
            ShortWindowResetAt: new DateTimeOffset(2026, 9, 8, 7, 28, 0, TimeSpan.FromHours(8)),
            WeeklyRemainingPercent: 6,
            WeeklyResetAt: new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.FromHours(8)),
            CapturedAt: _now);
        var state = new QuotaProbeDisplayState(QuotaProbeStatus.Complete, snapshot, _now);

        Assert.Equal(expected, QuotaProbeDisplay.FormatInline(state, language));
    }

    [Fact]
    public void PartialShortWindowPercentDoesNotPretendResetIsKnown()
    {
        var snapshot = new QuotaSnapshot(
            ShortWindowRemainingPercent: 43,
            ShortWindowResetAt: null,
            WeeklyRemainingPercent: null,
            WeeklyResetAt: null,
            CapturedAt: _now);
        var state = new QuotaProbeDisplayState(QuotaProbeStatus.Partial, snapshot, _now);

        Assert.Equal("額度：43% · 重置時間未知", QuotaProbeDisplay.FormatInline(state, "zh-TW"));
    }

    [Fact]
    public void TooltipOnlyShowsFieldsThatWereActuallyRead()
    {
        var snapshot = new QuotaSnapshot(
            ShortWindowRemainingPercent: 43,
            ShortWindowResetAt: null,
            WeeklyRemainingPercent: null,
            WeeklyResetAt: null,
            CapturedAt: _now);
        var state = new QuotaProbeDisplayState(QuotaProbeStatus.Partial, snapshot, _now);

        var tooltip = QuotaProbeDisplay.FormatTooltip(state, "zh-TW");

        Assert.Contains("5 小時: 43%", tooltip);
        Assert.DoesNotContain("1 週:", tooltip);
    }
}

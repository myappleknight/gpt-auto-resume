using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class RetryTimeParserTests
{
    private readonly RetryTimeParser _parser = new();
    private readonly DateTimeOffset _now = new(2026, 8, 31, 18, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void ParsesTraditionalChineseEveningTimeToday()
    {
        var ok = _parser.TryParse("或於晚上 7:54 再試一次。", _now, out var retryAt);

        Assert.True(ok);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 19, 54, 0, TimeSpan.FromHours(8)), retryAt);
    }

    [Theory]
    [InlineData("清晨6:14", 9, 1, 6)]
    [InlineData("凌晨6:14", 9, 1, 6)]
    [InlineData("早上6:14", 9, 1, 6)]
    [InlineData("上午6:14", 9, 1, 6)]
    [InlineData("中午12:14", 9, 1, 12)]
    [InlineData("下午6:14", 8, 31, 18)]
    [InlineData("晚上6:14", 8, 31, 18)]
    public void ParsesTraditionalChineseLocalizedTimePrefixes(string text, int expectedMonth, int expectedDay, int expectedHour)
    {
        var ok = _parser.TryParse($"或於 {text} 再試一次。", _now, out var retryAt);

        Assert.True(ok);
        Assert.Equal(new DateTimeOffset(2026, expectedMonth, expectedDay, expectedHour, 14, 0, TimeSpan.FromHours(8)), retryAt);
    }

    [Fact]
    public void ParsesEnglishSentenceWithChineseLocalizedTime()
    {
        var ok = _parser.TryParse("wait for usage to reset on 清晨6:14", _now, out var retryAt);

        Assert.True(ok);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 6, 14, 0, TimeSpan.FromHours(8)), retryAt);
    }

    [Fact]
    public void ParsesTimeOnlyAsTomorrowWhenAlreadyPassed()
    {
        var ok = _parser.TryParse("Retry at 7:54 PM", new DateTimeOffset(2026, 8, 31, 20, 0, 0, TimeSpan.FromHours(8)), out var retryAt);

        Assert.True(ok);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 19, 54, 0, TimeSpan.FromHours(8)), retryAt);
    }

    [Fact]
    public void ParsesEnglishMonthDate()
    {
        var ok = _parser.TryParse("Try again after Sep 1, 2026 at 2:10 AM.", _now, out var retryAt);

        Assert.True(ok);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 2, 10, 0, TimeSpan.FromHours(8)), retryAt);
    }

    [Fact]
    public void ParsesRelativeDuration()
    {
        var ok = _parser.TryParse("try again in 1 day 20 hours 45 minutes", _now, out var retryAt);

        Assert.True(ok);
        Assert.Equal(_now.AddDays(1).AddHours(20).AddMinutes(45), retryAt);
    }
}

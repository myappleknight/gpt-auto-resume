using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class QuotaSnapshotParserTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 1, 1, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void MissingShortWindowFieldsDoNotBorrowWeeklyValues()
    {
        var snapshot = QuotaSnapshotParser.Parse(
            "剩餘用量\n5 小時\n1 週\n0%\n9月7日 上午8:00", _now, new RetryTimeParser());

        Assert.Null(snapshot.ShortWindowRemainingPercent);
        Assert.Null(snapshot.ShortWindowResetAt);
        Assert.Equal(0, snapshot.WeeklyRemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.FromHours(8)), snapshot.WeeklyResetAt);
    }

    [Fact]
    public void ParsesAccountQuotaRows()
    {
        var snapshot = QuotaSnapshotParser.Parse(
            """
            剩餘用量
            5 小時
            0%
            清晨6:14
            1 週
            53%
            9月7日 上午8:00
            """,
            _now,
            new RetryTimeParser());

        Assert.Equal(0, snapshot.ShortWindowRemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 6, 14, 0, TimeSpan.FromHours(8)), snapshot.ShortWindowResetAt);
        Assert.Equal(53, snapshot.WeeklyRemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.FromHours(8)), snapshot.WeeklyResetAt);
    }

    [Fact]
    public void ParsesCompactAccountQuotaRowsFromProfilePanel()
    {
        var snapshot = QuotaSnapshotParser.Parse(
            """
            剩餘用量
            5 小時 0% 清晨7:28
            1 週 6% 9月7日
            """,
            _now,
            new RetryTimeParser());

        Assert.Equal(0, snapshot.ShortWindowRemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 7, 28, 0, TimeSpan.FromHours(8)), snapshot.ShortWindowResetAt);
        Assert.Equal(6, snapshot.WeeklyRemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.FromHours(8)), snapshot.WeeklyResetAt);
    }

    [Fact]
    public void ParsesNestedProfileQuotaPanelRowsWithLocalizedResetTime()
    {
        var now = new DateTimeOffset(2026, 9, 7, 22, 41, 0, TimeSpan.FromHours(8));

        var snapshot = QuotaSnapshotParser.Parse(
            """
            剩餘用量
            5 小時
            40%
            凌晨2:54
            1 週
            75%
            9月14日
            """,
            now,
            new RetryTimeParser());

        Assert.Equal(40, snapshot.ShortWindowRemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 9, 8, 2, 54, 0, TimeSpan.FromHours(8)), snapshot.ShortWindowResetAt);
        Assert.Equal(75, snapshot.WeeklyRemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.FromHours(8)), snapshot.WeeklyResetAt);
    }
}

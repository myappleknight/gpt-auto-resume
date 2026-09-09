using System.Text.RegularExpressions;

namespace GPTAutoResume.Core;

public sealed record QuotaSnapshot(
    int? ShortWindowRemainingPercent,
    DateTimeOffset? ShortWindowResetAt,
    int? WeeklyRemainingPercent,
    DateTimeOffset? WeeklyResetAt,
    DateTimeOffset CapturedAt);

public static partial class QuotaSnapshotParser
{
    public static QuotaSnapshot Parse(string text, DateTimeOffset capturedAt, RetryTimeParser retryTimeParser)
    {
        int? shortPercent = null;
        int? weeklyPercent = null;
        DateTimeOffset? shortResetAt = null;
        DateTimeOffset? weeklyResetAt = null;

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        for (var i = 0; i < lines.Length; i++)
        {
            if (IsShortWindowLabel(lines[i]))
            {
                shortPercent ??= FindPercent(lines, i);
                shortResetAt ??= FindResetAt(lines, i, capturedAt, retryTimeParser);
            }

            if (IsWeeklyLabel(lines[i]))
            {
                weeklyPercent ??= FindPercent(lines, i);
                weeklyResetAt ??= FindResetAt(lines, i, capturedAt, retryTimeParser);
            }
        }

        return new QuotaSnapshot(shortPercent, shortResetAt, weeklyPercent, weeklyResetAt, capturedAt);
    }

    private static int? FindPercent(IReadOnlyList<string> lines, int index)
    {
        foreach (var line in Nearby(lines, index))
        {
            var match = PercentRegex().Match(line);
            if (match.Success && int.TryParse(match.Groups["value"].Value, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static DateTimeOffset? FindResetAt(IReadOnlyList<string> lines, int index, DateTimeOffset capturedAt, RetryTimeParser retryTimeParser)
    {
        foreach (var line in Nearby(lines, index))
        {
            if (retryTimeParser.TryParse(line, capturedAt, out var retryAt))
            {
                return retryAt;
            }
        }

        return null;
    }

    private static IEnumerable<string> Nearby(IReadOnlyList<string> lines, int index)
    {
        for (var i = index; i <= Math.Min(lines.Count - 1, index + 3); i++)
        {
            if (i > index && (IsShortWindowLabel(lines[i]) || IsWeeklyLabel(lines[i])))
            {
                yield break;
            }

            yield return lines[i];
        }
    }

    private static bool IsShortWindowLabel(string line) =>
        line.Contains("小時", StringComparison.OrdinalIgnoreCase)
        || line.Contains("hour", StringComparison.OrdinalIgnoreCase);

    private static bool IsWeeklyLabel(string line) =>
        line.Contains('週') || line.Contains('周') || line.Contains("week", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?<value>\d{1,3})\s*%")]
    private static partial Regex PercentRegex();
}

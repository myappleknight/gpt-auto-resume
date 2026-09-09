using System.Globalization;
using System.Text.RegularExpressions;

namespace GPTAutoResume.Core;

public sealed partial class RetryTimeParser
{
    private static readonly Dictionary<string, int> EnglishMonths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"] = 1, ["january"] = 1, ["feb"] = 2, ["february"] = 2, ["mar"] = 3, ["march"] = 3,
        ["apr"] = 4, ["april"] = 4, ["may"] = 5, ["jun"] = 6, ["june"] = 6, ["jul"] = 7, ["july"] = 7,
        ["aug"] = 8, ["august"] = 8, ["sep"] = 9, ["sept"] = 9, ["september"] = 9, ["oct"] = 10,
        ["october"] = 10, ["nov"] = 11, ["november"] = 11, ["dec"] = 12, ["december"] = 12
    };

    private static readonly Dictionary<string, int> FrenchMonths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["janvier"] = 1, ["février"] = 2, ["fevrier"] = 2, ["mars"] = 3, ["avril"] = 4,
        ["mai"] = 5, ["juin"] = 6, ["juillet"] = 7, ["août"] = 8, ["aout"] = 8,
        ["septembre"] = 9, ["octobre"] = 10, ["novembre"] = 11, ["décembre"] = 12, ["decembre"] = 12
    };

    private static readonly Dictionary<string, int> GermanMonths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["januar"] = 1, ["februar"] = 2, ["märz"] = 3, ["maerz"] = 3, ["april"] = 4,
        ["mai"] = 5, ["juni"] = 6, ["juli"] = 7, ["august"] = 8, ["september"] = 9,
        ["oktober"] = 10, ["november"] = 11, ["dezember"] = 12
    };

    public bool TryParse(string text, DateTimeOffset now, out DateTimeOffset retryAt)
    {
        var normalized = Normalize(text);
        return TryParseIsoLike(normalized, now, out retryAt)
            || TryParseChineseDateTime(normalized, now, out retryAt)
            || TryParseChineseDateOnly(normalized, now, out retryAt)
            || TryParseEnglishMonthDate(normalized, now, out retryAt)
            || TryParseEuropeanMonthDate(normalized, now, out retryAt)
            || TryParseNumericDate(normalized, now, out retryAt)
            || TryParseRelativeDuration(normalized, now, out retryAt)
            || TryParseTimeOnly(normalized, now, out retryAt);
    }

    private static bool TryParseIsoLike(string text, DateTimeOffset now, out DateTimeOffset result)
    {
        foreach (Match match in IsoLikeRegex().Matches(text))
        {
            if (DateTimeOffset.TryParse(match.Value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed)
                && IsPlausibleFuture(parsed, now))
            {
                result = parsed;
                return true;
            }
        }

        result = default;
        return false;
    }

    private static bool TryParseChineseDateTime(string text, DateTimeOffset now, out DateTimeOffset result)
    {
        var match = ChineseDateTimeRegex().Match(text);
        if (!match.Success)
        {
            result = default;
            return false;
        }

        var hasDate = match.Groups["month"].Success && match.Groups["day"].Success;
        var hasChineseMeridiem = match.Groups["meridiem"].Success;
        if (!hasDate && !hasChineseMeridiem)
        {
            result = default;
            return false;
        }

        var year = match.Groups["year"].Success ? int.Parse(match.Groups["year"].Value) : now.Year;
        var month = hasDate ? int.Parse(match.Groups["month"].Value) : now.Month;
        var day = hasDate ? int.Parse(match.Groups["day"].Value) : now.Day;
        var hour = int.Parse(match.Groups["hour"].Value);
        var minute = int.Parse(match.Groups["minute"].Value);
        hour = ApplyMeridiem(hour, match.Groups["meridiem"].Value);
        return hasDate
            ? BuildFuture(year, month, day, hour, minute, now, explicitYear: match.Groups["year"].Success, out result)
            : BuildTimeOnly(hour, minute, now, out result);
    }

    private static bool TryParseChineseDateOnly(string text, DateTimeOffset now, out DateTimeOffset result)
    {
        var match = ChineseDateOnlyRegex().Match(text);
        if (!match.Success)
        {
            result = default;
            return false;
        }

        var year = match.Groups["year"].Success ? int.Parse(match.Groups["year"].Value) : now.Year;
        var month = int.Parse(match.Groups["month"].Value);
        var day = int.Parse(match.Groups["day"].Value);
        return BuildFuture(year, month, day, 0, 0, now, explicitYear: match.Groups["year"].Success, out result);
    }

    private static bool TryParseEnglishMonthDate(string text, DateTimeOffset now, out DateTimeOffset result)
    {
        var match = EnglishMonthDateRegex().Match(text);
        if (!match.Success || !EnglishMonths.TryGetValue(match.Groups["month"].Value, out var month))
        {
            result = default;
            return false;
        }

        var explicitYear = match.Groups["year"].Success;
        var year = explicitYear ? int.Parse(match.Groups["year"].Value) : now.Year;
        var day = int.Parse(match.Groups["day"].Value);
        var hour = int.Parse(match.Groups["hour"].Value);
        var minute = int.Parse(match.Groups["minute"].Value);
        hour = ApplyMeridiem(hour, match.Groups["ampm"].Value);
        return BuildFuture(year, month, day, hour, minute, now, explicitYear, out result);
    }

    private static bool TryParseEuropeanMonthDate(string text, DateTimeOffset now, out DateTimeOffset result)
    {
        var match = EuropeanMonthDateRegex().Match(text);
        if (!match.Success)
        {
            result = default;
            return false;
        }

        var monthName = match.Groups["month"].Value;
        if (!FrenchMonths.TryGetValue(monthName, out var month) && !GermanMonths.TryGetValue(monthName, out month))
        {
            result = default;
            return false;
        }

        var explicitYear = match.Groups["year"].Success;
        var year = explicitYear ? int.Parse(match.Groups["year"].Value) : now.Year;
        var day = int.Parse(match.Groups["day"].Value);
        var hour = int.Parse(match.Groups["hour"].Value);
        var minute = int.Parse(match.Groups["minute"].Value);
        return BuildFuture(year, month, day, hour, minute, now, explicitYear, out result);
    }

    private static bool TryParseNumericDate(string text, DateTimeOffset now, out DateTimeOffset result)
    {
        var match = NumericDateRegex().Match(text);
        if (!match.Success)
        {
            result = default;
            return false;
        }

        var first = int.Parse(match.Groups["first"].Value);
        var second = int.Parse(match.Groups["second"].Value);
        var explicitYear = match.Groups["year"].Success;
        var year = explicitYear ? int.Parse(match.Groups["year"].Value) : now.Year;
        var hour = int.Parse(match.Groups["hour"].Value);
        var minute = int.Parse(match.Groups["minute"].Value);
        var month = first > 12 ? second : first;
        var day = first > 12 ? first : second;
        return BuildFuture(year, month, day, hour, minute, now, explicitYear, out result);
    }

    private static bool TryParseRelativeDuration(string text, DateTimeOffset now, out DateTimeOffset result)
    {
        var match = RelativeDurationRegex().Match(text);
        if (!match.Success)
        {
            result = default;
            return false;
        }

        var days = match.Groups["days"].Success ? int.Parse(match.Groups["days"].Value) : 0;
        var hours = match.Groups["hours"].Success ? int.Parse(match.Groups["hours"].Value) : 0;
        var minutes = match.Groups["minutes"].Success ? int.Parse(match.Groups["minutes"].Value) : 0;
        result = now.AddDays(days).AddHours(hours).AddMinutes(minutes);
        return result > now;
    }

    private static bool TryParseTimeOnly(string text, DateTimeOffset now, out DateTimeOffset result)
    {
        var match = TimeOnlyRegex().Match(text);
        if (!match.Success)
        {
            result = default;
            return false;
        }

        var hour = int.Parse(match.Groups["hour"].Value);
        var minute = int.Parse(match.Groups["minute"].Value);
        var meridiem = match.Groups["prefix"].Success ? match.Groups["prefix"].Value : match.Groups["suffix"].Value;
        hour = ApplyMeridiem(hour, meridiem);
        return BuildTimeOnly(hour, minute, now, out result);
    }

    private static bool BuildTimeOnly(int hour, int minute, DateTimeOffset now, out DateTimeOffset result)
    {
        result = new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, now.Offset);
        if (result <= now)
        {
            result = result.AddDays(1);
        }

        return IsPlausibleFuture(result, now);
    }

    private static bool BuildFuture(int year, int month, int day, int hour, int minute, DateTimeOffset now, bool explicitYear, out DateTimeOffset result)
    {
        try
        {
            result = new DateTimeOffset(year, month, day, hour, minute, 0, now.Offset);
            if (result <= now && !explicitYear)
            {
                result = result.AddYears(1);
            }

            return IsPlausibleFuture(result, now);
        }
        catch
        {
            result = default;
            return false;
        }
    }

    private static int ApplyMeridiem(int hour, string marker)
    {
        marker = marker.Trim().ToLowerInvariant();
        if ((marker is "pm" or "p.m." or "下午" or "晚上") && hour < 12)
        {
            return hour + 12;
        }

        if (marker is "中午" && hour is > 0 and < 11)
        {
            return hour + 12;
        }

        if ((marker is "am" or "a.m." or "上午" or "清晨" or "凌晨" or "早上") && hour == 12)
        {
            return 0;
        }

        return hour;
    }

    private static bool IsPlausibleFuture(DateTimeOffset value, DateTimeOffset now) =>
        value > now && value <= now.AddDays(14);

    private static string Normalize(string text) =>
        text.Replace('\u3000', ' ').Replace("，", ",").Replace("：", ":");

    [GeneratedRegex(@"\b\d{4}[-/]\d{1,2}[-/]\d{1,2}\s+\d{1,2}:\d{2}\b")]
    private static partial Regex IsoLikeRegex();

    [GeneratedRegex(@"(?:(?<year>\d{4})年)?(?:(?<month>\d{1,2})月(?<day>\d{1,2})日)?\s*(?<meridiem>清晨|凌晨|早上|上午|中午|下午|晚上)?\s*(?<hour>\d{1,2})[:點点](?<minute>\d{2})")]
    private static partial Regex ChineseDateTimeRegex();

    [GeneratedRegex(@"(?:(?<year>\d{4})年)?(?<month>\d{1,2})月(?<day>\d{1,2})日")]
    private static partial Regex ChineseDateOnlyRegex();

    [GeneratedRegex(@"(?<month>jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:t|tember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\.?\s+(?<day>\d{1,2})(?:,\s*(?<year>\d{4}))?(?:\s+at)?\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*(?<ampm>am|pm|a\.m\.|p\.m\.)?", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishMonthDateRegex();

    [GeneratedRegex(@"(?<day>\d{1,2})\.?\s+(?<month>janvier|février|fevrier|mars|avril|mai|juin|juillet|août|aout|septembre|octobre|novembre|décembre|decembre|januar|februar|märz|maerz|april|juni|juli|august|september|oktober|dezember)(?:\s+(?<year>\d{4}))?(?:\s+(?:à|um|at))?\s+(?<hour>\d{1,2}):(?<minute>\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex EuropeanMonthDateRegex();

    [GeneratedRegex(@"\b(?<first>\d{1,2})[./-](?<second>\d{1,2})(?:[./-](?<year>\d{4}))?\s+(?<hour>\d{1,2}):(?<minute>\d{2})\b")]
    private static partial Regex NumericDateRegex();

    [GeneratedRegex(@"(?=\d+\s+(?:days?|hours?|minutes?))(?:(?<days>\d+)\s+days?\s*)?(?:(?<hours>\d+)\s+hours?\s*)?(?:(?<minutes>\d+)\s+minutes?)?", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeDurationRegex();

    [GeneratedRegex(@"(?<prefix>清晨|凌晨|早上|上午|中午|下午|晚上|am|pm|a\.m\.|p\.m\.)?\s*(?<hour>\d{1,2}):(?<minute>\d{2})\s*(?<suffix>am|pm|a\.m\.|p\.m\.)?", RegexOptions.IgnoreCase)]
    private static partial Regex TimeOnlyRegex();
}

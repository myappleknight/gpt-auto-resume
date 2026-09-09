using System.Globalization;
using System.Text.RegularExpressions;

namespace GPTAutoResume.Core;

public sealed partial class UsageLimitDetector(PatternCatalog catalog, RetryTimeParser timeParser)
{
    private const int WholePageSafeLength = 600;

    public DetectionResult Analyze(string text, DateTimeOffset now, bool hasWarningRole = false, bool hasWarningIcon = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DetectionResult.Ignore();
        }

        var normalized = text.ToLower(CultureInfo.CurrentCulture);
        var compact = WhitespaceRegex().Replace(normalized, "");
        var score = 0;
        var matched = new List<string>();
        var languageScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hasUsageSignal = false;
        var hasRetrySignal = false;
        var hasRetryTime = timeParser.TryParse(text, now, out var retryAt);

        if (hasRetryTime)
        {
            score += 40;
        }

        foreach (var lang in catalog.Languages)
        {
            var usageHit = MatchAny(normalized, lang.UsageLimit, matched);
            var retryHit = MatchAny(normalized, lang.Retry, matched);
            if (usageHit)
            {
                score += 30;
                AddLanguageScore(lang.Language, 30);
                hasUsageSignal = true;
            }

            if (retryHit)
            {
                score += 15;
                AddLanguageScore(lang.Language, 15);
                hasRetrySignal = true;
            }
        }

        if (OutOfUsageRegex().IsMatch(normalized))
        {
            score += 30;
            AddLanguageScore("en", 30);
            hasUsageSignal = true;
            matched.Add("en.out_of_usage_regex");
        }

        if (ChineseCodexWorkUsageExhaustedRegex().IsMatch(compact)
            || ChineseUsageExhaustedRegex().IsMatch(compact))
        {
            score += 35;
            AddLanguageScore("zh-TW", 35);
            hasUsageSignal = true;
            matched.Add("zh_tw.usage_exhausted_regex");
        }

        var hasWarningContext = hasWarningRole || hasWarningIcon || normalized.Contains('!') || normalized.Contains('⚠');
        if (hasWarningRole)
        {
            score += 10;
            matched.Add("ui.warning_role");
        }

        if (hasWarningIcon || normalized.Contains('!') || normalized.Contains('⚠'))
        {
            score += 5;
            matched.Add("ui.warning_icon");
        }

        var strongSignal = hasUsageSignal || (hasRetrySignal && hasWarningContext);
        if (!hasRetryTime)
        {
            var language = BestLanguage();
            var noTimeKind = score >= 35 && (hasUsageSignal || (hasRetrySignal && hasWarningContext))
                ? DetectionKind.LimitDetected
                : score >= 20 ? DetectionKind.PossibleLimit : DetectionKind.Ignore;
            return new DetectionResult(noTimeKind, score, null, language, matched.Distinct().ToArray());
        }

        var kind = score >= 75 && strongSignal
            ? DetectionKind.LimitDetected
            : score >= 50 ? DetectionKind.PossibleLimit : DetectionKind.Ignore;

        return new DetectionResult(kind, score, retryAt, BestLanguage(), matched.Distinct().ToArray());

        void AddLanguageScore(string detectedLanguage, int points)
        {
            languageScores[detectedLanguage] = languageScores.GetValueOrDefault(detectedLanguage) + points;
        }

        string? BestLanguage() =>
            languageScores.Count == 0
                ? null
                : languageScores.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).First().Key;
    }

    public DetectionResult AnalyzeForAutomation(string visibleText, DateTimeOffset now, bool hasWarningRole = false, bool hasWarningIcon = false)
    {
        var result = Analyze(visibleText, now, hasWarningRole, hasWarningIcon);
        if (result.Kind != DetectionKind.LimitDetected)
        {
            return result;
        }

        var hasWarningContext = hasWarningRole || hasWarningIcon || visibleText.Contains('⚠') || visibleText.Contains('!');
        if (hasWarningContext || visibleText.Length <= WholePageSafeLength)
        {
            return result;
        }

        var signal = PossibleLimitHeuristics.Analyze(visibleText);
        if (!string.IsNullOrWhiteSpace(signal.TextFragment)
            && signal.TextFragment.Length < visibleText.Length
            && HasProductionBannerSignal(signal.TextFragment))
        {
            var fragmentResult = Analyze(signal.TextFragment, now, hasWarningRole, hasWarningIcon);
            if (fragmentResult.Kind == DetectionKind.LimitDetected)
            {
                return fragmentResult;
            }
        }

        return result with { Kind = DetectionKind.PossibleLimit };
    }


    private static bool MatchAny(string normalized, IEnumerable<PatternEntry> entries, List<string> matched)
    {
        var hit = false;
        foreach (var entry in entries)
        {
            if (!normalized.Contains(entry.Text.ToLower(CultureInfo.CurrentCulture)))
            {
                continue;
            }

            matched.Add(entry.Id);
            hit = true;
        }

        return hit;
    }

    [GeneratedRegex(@"\bout of\b.{0,80}\busage\b", RegexOptions.IgnoreCase)]
    private static partial Regex OutOfUsageRegex();

    private static bool HasProductionBannerSignal(string text) =>
        text.Contains("Codex and Work usage", StringComparison.OrdinalIgnoreCase)
        || text.Contains("add credits", StringComparison.OrdinalIgnoreCase)
        || text.Contains("upgrade", StringComparison.OrdinalIgnoreCase)
        || text.Contains("升級方案", StringComparison.OrdinalIgnoreCase)
        || text.Contains("加值點數", StringComparison.OrdinalIgnoreCase)
        || text.Contains("已用完", StringComparison.OrdinalIgnoreCase)
        || text.Contains("用量重置", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"你的?codex和工作使用量已用完", RegexOptions.IgnoreCase)]
    private static partial Regex ChineseCodexWorkUsageExhaustedRegex();

    [GeneratedRegex(@"使用量已用完", RegexOptions.IgnoreCase)]
    private static partial Regex ChineseUsageExhaustedRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

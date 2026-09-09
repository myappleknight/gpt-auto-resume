using System.Globalization;
using System.Text.RegularExpressions;

namespace GPTAutoResume.Core;

public static partial class PossibleLimitHeuristics
{
    private static readonly string[] Terms =
    [
        "usage",
        "limit",
        "out of usage",
        "reset",
        "retry",
        "try again",
        "credits",
        "upgrade",
        "剩餘用量",
        "使用上限",
        "再試一次",
        "再试一次",
        "加值點數",
        "升級方案",
        "你的 Codex",
        "工作使用量",
        "使用量已用完",
        "用量已用完",
        "已用完"
    ];

    public static PossibleLimitSignal Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new PossibleLimitSignal(false, "", "");
        }

        var normalized = text.ToLower(CultureInfo.CurrentCulture);
        var matchedTerms = Terms
            .Where(term => normalized.Contains(term.ToLower(CultureInfo.CurrentCulture)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var datetimeCandidate = DateTimeCandidateRegex().Match(text).Value;
        var isInteresting = matchedTerms.Length > 0 || !string.IsNullOrWhiteSpace(datetimeCandidate);
        return new PossibleLimitSignal(
            isInteresting,
            BuildFragment(text, matchedTerms),
            datetimeCandidate);
    }

    private static string BuildFragment(string text, IReadOnlyCollection<string> matchedTerms)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => matchedTerms.Any(term => line.Contains(term, StringComparison.OrdinalIgnoreCase))
                || DateTimeCandidateRegex().IsMatch(line))
            .Take(8)
            .ToArray();
        var fragment = string.Join(Environment.NewLine, lines);
        return fragment.Length <= 1_000 ? fragment : fragment[..1_000];
    }

    [GeneratedRegex(@"(?:清晨|凌晨|早上|上午|中午|下午|晚上)?\s*\d{1,2}[:點点]\d{2}|(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s+\d{1,2}", RegexOptions.IgnoreCase)]
    private static partial Regex DateTimeCandidateRegex();
}

public sealed record PossibleLimitSignal(bool IsInteresting, string TextFragment, string DateTimeCandidate);

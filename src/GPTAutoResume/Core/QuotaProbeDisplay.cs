namespace GPTAutoResume.Core;

public enum QuotaProbeStatus
{
    NotRead,
    Reading,
    Complete,
    Partial,
    Unsupported
}

public sealed record QuotaProbeDisplayState(
    QuotaProbeStatus Status,
    QuotaSnapshot? Snapshot = null,
    DateTimeOffset? LastReadAt = null,
    string Source = "UI Automation");

public static class QuotaProbeDisplay
{
    public static string FormatCurrent(QuotaProbeDisplayState state, string language, DateTimeOffset now, bool limitDetected)
    {
        if (limitDetected)
            return $"{Label(language, "Quota")}{Label(language, "Separator")}{Label(language, "LimitReached")}";
        if (state.Status is QuotaProbeStatus.Complete or QuotaProbeStatus.Partial)
        {
            var readAt = state.LastReadAt ?? state.Snapshot?.CapturedAt;
            if (readAt is null || now - readAt > TimeSpan.FromMinutes(5))
                return $"{Label(language, "Quota")}{Label(language, "Separator")}{Label(language, "Stale")}";
            return $"{FormatInline(state, language)} · {readAt:HH:mm} {Label(language, "ReadAt")}";
        }
        return FormatInline(state, language);
    }

    public static string FormatInline(QuotaProbeDisplayState state, string language)
    {
        var quotaLabel = Label(language, "Quota");
        var separator = Label(language, "Separator");
        return state.Status switch
        {
            QuotaProbeStatus.NotRead => $"{quotaLabel}{separator}{Label(language, "NotRead")}",
            QuotaProbeStatus.Reading => $"{quotaLabel}{separator}{Label(language, "Reading")}",
            QuotaProbeStatus.Unsupported => $"{quotaLabel}{separator}{Label(language, "Unsupported")}",
            QuotaProbeStatus.Complete or QuotaProbeStatus.Partial => FormatSnapshotInline(quotaLabel, separator, state.Snapshot, language),
            _ => $"{quotaLabel}: {Label(language, "NotRead")}"
        };
    }

    public static string FormatTooltip(QuotaProbeDisplayState state, string language)
    {
        var lines = new List<string> { Label(language, "TooltipTitle") };
        if (state.Snapshot is { } snapshot)
        {
            if (snapshot.ShortWindowRemainingPercent is not null)
            {
                lines.Add($"{Label(language, "ShortWindow")}: {snapshot.ShortWindowRemainingPercent}%");
            }

            if (snapshot.ShortWindowResetAt is not null)
            {
                lines.Add($"{Label(language, "Reset")}: {FormatReset(snapshot.ShortWindowResetAt.Value)}");
            }

            if (snapshot.WeeklyRemainingPercent is not null)
            {
                lines.Add($"{Label(language, "Weekly")}: {snapshot.WeeklyRemainingPercent}%");
            }

            if (snapshot.WeeklyResetAt is not null)
            {
                lines.Add($"{Label(language, "Reset")}: {snapshot.WeeklyResetAt.Value:MM/dd HH:mm}");
            }
        }

        lines.Add($"Source: {state.Source}");
        if (state.LastReadAt is not null)
        {
            lines.Add($"{Label(language, "LastRead")}: {state.LastReadAt.Value:HH:mm:ss}");
        }

        if (state.Status is QuotaProbeStatus.Unsupported)
        {
            lines.Add(Label(language, "UnsupportedLong"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatSnapshotInline(string quotaLabel, string separator, QuotaSnapshot? snapshot, string language)
    {
        if (snapshot is null)
        {
            return $"{quotaLabel}{separator}{Label(language, "Partial")}";
        }

        if (snapshot.ShortWindowRemainingPercent is null)
        {
            return $"{quotaLabel}{separator}{Label(language, "Partial")}";
        }

        if (snapshot.ShortWindowResetAt is null)
        {
            return $"{quotaLabel}{separator}{snapshot.ShortWindowRemainingPercent}% · {Label(language, "ResetUnknown")}";
        }

        return string.Format(Label(language, "PercentReset"), quotaLabel, separator, snapshot.ShortWindowRemainingPercent, FormatReset(snapshot.ShortWindowResetAt.Value));
    }

    private static string FormatReset(DateTimeOffset resetAt) => resetAt.ToString("HH:mm");

    private static string Label(string language, string key)
    {
        var zh = language.Equals("zh-TW", StringComparison.OrdinalIgnoreCase);
        var ja = language.Equals("ja", StringComparison.OrdinalIgnoreCase);
        return key switch
        {
            "Quota" => ja ? "使用量" : zh ? "額度" : "Quota",
            "Stale" => ja ? "更新待ち" : zh ? "待更新" : "Update needed",
            "LimitReached" => ja ? "上限に達しました" : zh ? "已達上限" : "Limit reached",
            "ReadAt" => ja ? "取得" : zh ? "讀取" : "read",
            "Separator" => zh || ja ? "：" : ": ",
            "NotRead" => ja ? "未取得" : zh ? "未讀取" : "Not read",
            "Reading" => ja ? "取得中..." : zh ? "讀取中..." : "Reading...",
            "Unsupported" => ja ? "現在未対応" : zh ? "目前不支援" : "Unsupported",
            "Partial" => ja ? "一部取得" : zh ? "部分可讀" : "Partial",
            "ResetUnknown" => ja ? "リセット時刻不明" : zh ? "重置時間未知" : "reset unknown",
            "PercentReset" => ja ? "{0}{1}{2}% · {3} リセット" : zh ? "{0}{1}{2}% · {3} 重置" : "{0}{1}{2}% · resets {3}",
            "TooltipTitle" => ja ? "使用量プローブ" : zh ? "額度探針" : "Quota probe",
            "ShortWindow" => ja ? "5 時間" : zh ? "5 小時" : "5h",
            "Weekly" => ja ? "1 週" : zh ? "1 週" : "1 week",
            "Reset" => ja ? "リセット" : zh ? "重置" : "Reset",
            "LastRead" => ja ? "前回取得" : zh ? "上次讀取" : "Last read",
            "UnsupportedLong" => ja ? "現在の ChatGPT UIA では完全な使用量行を取得できません。" : zh ? "目前 ChatGPT UIA 未提供完整可信額度列。" : "Current ChatGPT UIA does not expose complete trusted quota rows.",
            _ => key
        };
    }
}

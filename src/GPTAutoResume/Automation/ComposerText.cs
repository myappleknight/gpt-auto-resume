namespace GPTAutoResume.Automation;

public static class ComposerText
{
    public static string StableClassName(string className) => string.Join(" ",
        className.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token != "ProseMirror-focused"));

    public static string? Normalize(string? text, string name, bool singleEmptyPlaceholder) =>
        Normalize(text, name, singleEmptyPlaceholder, "");

    public static string? Normalize(string? text, string name, bool singleEmptyPlaceholder, string className)
    {
        if (text is null) return null;
        var textTrimmed = text.Trim();
        var nameTrimmed = name.Trim();
        if (textTrimmed == nameTrimmed
            && (singleEmptyPlaceholder || StableClassName(className).Contains("ProseMirror", StringComparison.Ordinal)))
        {
            return "";
        }

        return text;
    }
}

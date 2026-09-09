namespace GPTAutoResume.Automation;

public static class ComposerText
{
    public static string StableClassName(string className) => string.Join(" ",
        className.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token != "ProseMirror-focused"));
    public static string? Normalize(string? text, string name, bool singleEmptyPlaceholder) =>
        singleEmptyPlaceholder && text?.Trim() == name.Trim() ? "" : text;
}

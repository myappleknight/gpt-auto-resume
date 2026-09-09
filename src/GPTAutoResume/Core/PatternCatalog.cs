using System.Text.Json;
using System.IO;

namespace GPTAutoResume.Core;

public sealed class PatternCatalog
{
    public required List<LanguagePattern> Languages { get; init; }

    public static PatternCatalog LoadDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "usage_limit_patterns.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "usage_limit_patterns.json");
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PatternCatalog>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Pattern catalog could not be loaded.");
    }
}

public sealed class LanguagePattern
{
    public required string Language { get; init; }
    public required List<PatternEntry> UsageLimit { get; init; }
    public required List<PatternEntry> Retry { get; init; }
}

public sealed class PatternEntry
{
    public required string Id { get; init; }
    public required string Text { get; init; }
}

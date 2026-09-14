using System.Text.Json;
using System.IO;
using System.Reflection;

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

        var json = File.Exists(path) ? File.ReadAllText(path) : ReadEmbeddedDefault();
        return JsonSerializer.Deserialize<PatternCatalog>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Pattern catalog could not be loaded.");
    }

    private static string ReadEmbeddedDefault()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("usage_limit_patterns.json", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new FileNotFoundException("usage_limit_patterns.json could not be found next to the app or as an embedded resource.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException("usage_limit_patterns.json embedded resource could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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

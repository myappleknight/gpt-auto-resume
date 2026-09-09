using System.Text.Json;
using System.IO;

namespace GPTAutoResume.Core;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public ConfigStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GPTAutoResume");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "config.json");
    }

    public AppConfig Load()
    {
        if (!File.Exists(_path))
        {
            return new AppConfig();
        }

        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(config, JsonOptions));
    }
}

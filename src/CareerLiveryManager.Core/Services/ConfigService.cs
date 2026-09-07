using System.Text.Json;
using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

public sealed class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CareerLiveryManager");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new AppConfig();
        }

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>
    /// True if the given folder contains at least one subfolder with a readable manifest.json.
    /// </summary>
    public bool IsValidOfficialPath(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        return Directory.EnumerateDirectories(path)
            .Any(dir => File.Exists(Path.Combine(dir, "manifest.json")));
    }

    public bool CommunityPathExists(string path) => Directory.Exists(path);

    public void EnsureCommunityPathExists(string path) => Directory.CreateDirectory(path);
}

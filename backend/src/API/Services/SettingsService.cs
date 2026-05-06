using System.Text.Json;

namespace API.Services;

public sealed class SettingsService
{
    public string? LastRootPath { get; set; }
    public string LastLocale { get; set; } = "english";
    public bool PlaceholderResolverEnabled { get; set; } = true;
    public string ThemeVariant { get; set; } = "Dark";
    public bool AutoExtractEnabled { get; set; } = true;
    public bool ExtractPng { get; set; } = true;
    public bool ExtractGlb { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoUpdateEnabled { get; set; } = false;
    public bool VerboseLogging { get; set; } = false;

    private static string GetSettingsFilePath()
    {
        var dir = AppContext.BaseDirectory;
        return Path.Combine(dir, "settings.json");
    }

    public void Load()
    {
        try
        {
            var path = GetSettingsFilePath();
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<SettingsService>(json);
            if (dto is null) return;

            LastRootPath = dto.LastRootPath;
            LastLocale = dto.LastLocale ?? "english";
            PlaceholderResolverEnabled = dto.PlaceholderResolverEnabled;
            ThemeVariant = dto.ThemeVariant ?? "Dark";
            AutoExtractEnabled = dto.AutoExtractEnabled;
            ExtractPng = dto.ExtractPng;
            ExtractGlb = dto.ExtractGlb;
            MinimizeToTray = dto.MinimizeToTray;
            AutoUpdateEnabled = dto.AutoUpdateEnabled;
            VerboseLogging = dto.VerboseLogging;
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            var path = GetSettingsFilePath();
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }
}

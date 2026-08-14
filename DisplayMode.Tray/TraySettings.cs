using System.Text.Json;

namespace DisplayMode.Tray;

internal sealed class TraySettings
{
    public string DisplayModePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "DisplayMode.exe");
    public string ConfigPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "profiles.json");
    public List<HotKeyBinding> HotKeys { get; set; } =
    [
        new() { Profile = "1", Shortcut = "Alt+Shift+1" },
        new() { Profile = "2", Shortcut = "Alt+Shift+2" },
        new() { Profile = "3", Shortcut = "Alt+Shift+3" }
    ];

    internal static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DisplayMode", "tray-settings.json");

    internal static TraySettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new TraySettings();
            return JsonSerializer.Deserialize<TraySettings>(File.ReadAllText(SettingsPath), JsonOptions()) ?? new TraySettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load tray settings. Defaults will be used.\n\n{ex.Message}",
                "DisplayMode", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return new TraySettings();
        }
    }

    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}

internal sealed class HotKeyBinding
{
    public string Profile { get; set; } = string.Empty;
    public string Shortcut { get; set; } = string.Empty;
}

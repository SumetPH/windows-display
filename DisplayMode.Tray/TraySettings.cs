using System.Text.Json;

namespace DisplayMode.Tray;

internal sealed class TraySettings
{
    public string DisplayModePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "DisplayMode.exe");
    public string ConfigPath { get; set; } = DefaultConfigPath;
    // Names follow the profile order in the bundled profiles.example.json.
    public List<HotKeyBinding> HotKeys { get; set; } =
    [
        new() { Profile = "Extend", Shortcut = "Alt+1" },
        new() { Profile = "TV", Shortcut = "Alt+2" },
        new() { Profile = "Main", Shortcut = "Alt+3" }
    ];
    public List<string> ReapplyOnDisplayChange { get; set; } = [];
    public string? DefaultProfile { get; set; }
    private static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayMode");
    internal static string SettingsPath => Path.Combine(DataDirectory, "tray-settings.json");
    // Must match ConfigurationLoader.DefaultPath in the CLI.
    internal static string DefaultConfigPath => Path.Combine(DataDirectory, "profiles.json");
    private static string LegacyConfigPath => Path.Combine(AppContext.BaseDirectory, "profiles.json");

    internal static TraySettings Load()
    {
        try
        {
            var settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<TraySettings>(File.ReadAllText(SettingsPath), JsonOptions()) ?? new TraySettings()
                : new TraySettings();
            settings.MigrateConfigPath();
            return settings;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load tray settings. Defaults will be used.\n\n{ex.Message}",
                "DisplayMode", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return new TraySettings();
        }
    }

    // Older versions kept profiles.json in the program folder, where a publish could
    // overwrite or remove it. Move such a setting to the per-user default location.
    private void MigrateConfigPath()
    {
        var usesLegacy = string.Equals(Path.GetFullPath(ConfigPath), LegacyConfigPath,
            StringComparison.OrdinalIgnoreCase);
        if (usesLegacy)
            ConfigPath = DefaultConfigPath;
        if (!string.Equals(Path.GetFullPath(ConfigPath), DefaultConfigPath, StringComparison.OrdinalIgnoreCase))
            return;

        if (!File.Exists(DefaultConfigPath))
        {
            var source = new[] { LegacyConfigPath, Path.Combine(AppContext.BaseDirectory, "profiles.example.json") }
                .FirstOrDefault(File.Exists);
            if (source is not null)
            {
                Directory.CreateDirectory(DataDirectory);
                File.Copy(source, DefaultConfigPath);
            }
        }
        if (usesLegacy)
            Save();
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

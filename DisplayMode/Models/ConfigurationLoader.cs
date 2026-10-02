using System.Text.Json;
using DisplayMode.Displays;
using DisplayMode.Native;

namespace DisplayMode.Models;

internal static class ConfigurationLoader
{
    // The live config sits outside the program folder so that publishing a new build,
    // which rewrites that folder, can never replace or remove the user's profiles.
    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayMode", "profiles.json");

    /// <summary>
    /// Creates the default config on first run from a profiles.json left next to the
    /// executable by older versions, or from the bundled example.
    /// </summary>
    internal static void EnsureDefaultExists()
    {
        if (File.Exists(DefaultPath))
            return;
        var source = new[] { "profiles.json", "profiles.example.json" }
            .Select(name => Path.Combine(AppContext.BaseDirectory, name))
            .FirstOrDefault(File.Exists);
        if (source is null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath)!);
        File.Copy(source, DefaultPath);
        Console.WriteLine($"Created {DefaultPath} from {source}.");
    }

    internal static DisplayModeConfiguration Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Configuration file was not found: {fullPath}", fullPath);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var document = JsonSerializer.Deserialize<ConfigurationDocument>(File.ReadAllText(fullPath), options)
                       ?? throw new InvalidDataException("Configuration is empty.");
        if (document.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported schemaVersion {document.SchemaVersion}; expected 1.");
        if (document.Monitors.Count == 0 || document.Profiles.Count == 0)
            throw new InvalidDataException("At least one monitor and one profile must be defined.");

        var monitors = new Dictionary<string, MonitorDefinition>(StringComparer.OrdinalIgnoreCase);
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, value) in document.Monitors)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(value.Match))
                throw new InvalidDataException("Monitor aliases and match strings cannot be empty.");
            if (!matches.Add(value.Match.Trim()))
                throw new InvalidDataException($"Monitor match '{value.Match}' is used more than once.");
            var monitorAlias = alias.Trim();
            if (value.Width == 0 || value.Height == 0 || value.RefreshHz <= 0 || !double.IsFinite(value.RefreshHz))
                throw new InvalidDataException($"Monitor '{monitorAlias}' has an invalid size or refresh rate.");
            monitors.Add(monitorAlias, new MonitorDefinition(monitorAlias, value.Match.Trim(), value.Width, value.Height,
                value.RefreshHz, ParseRotation(value.Rotation, monitorAlias, monitorAlias), value.X, value.Y));
        }

        var profiles = new Dictionary<string, DisplayProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in document.Profiles)
        {
            if (string.IsNullOrWhiteSpace(name) || value.Monitors.Count == 0)
                throw new InvalidDataException("Profile names cannot be empty and every profile needs an active monitor.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<MonitorProfile>();
            foreach (var item in value.Monitors)
            {
                if (!monitors.TryGetValue(item.Monitor, out var monitor))
                    throw new InvalidDataException($"Profile '{name}' references unknown monitor alias '{item.Monitor}'.");
                if (!seen.Add(monitor.Alias))
                    throw new InvalidDataException($"Profile '{name}' contains monitor '{monitor.Alias}' more than once.");
                if (!string.IsNullOrWhiteSpace(item.Wallpaper) && !string.IsNullOrWhiteSpace(item.WallpaperFolder))
                    throw new InvalidDataException(
                        $"Profile '{name}', monitor '{monitor.Alias}' cannot use both wallpaper and wallpaperFolder.");
                var wallpaperPath = ResolveWallpaperPath(item.Wallpaper, fullPath, name, monitor.Alias);
                var wallpaperFolder = ResolveWallpaperFolder(item.WallpaperFolder, fullPath, name, monitor.Alias);
                var slideshowInterval = item.SlideshowInterval ?? 300;
                if (wallpaperFolder is not null && slideshowInterval < 60)
                    throw new InvalidDataException(
                        $"Profile '{name}', monitor '{monitor.Alias}' slideshowInterval must be at least 60 seconds.");
                entries.Add(new MonitorProfile(monitor.Alias, monitor.Match, monitor.Width, monitor.Height, monitor.RefreshHz,
                    monitor.Rotation, monitor.X, monitor.Y, item.Primary, wallpaperPath,
                    wallpaperFolder, slideshowInterval, ParseSlideshowOrder(item.SlideshowOrder, name, monitor.Alias)));
            }
            ValidateLayout(name, entries);
            profiles.Add(name.Trim(), new DisplayProfile(name.Trim(), entries));
        }
        return new DisplayModeConfiguration(fullPath, monitors, profiles, document.AllowUnmanagedTargets);
    }

    private static DisplayConfigRotation ParseRotation(string value, string profile, string monitor) => value.Trim().ToLowerInvariant() switch
    {
        "landscape" or "identity" or "0" => DisplayConfigRotation.Identity,
        "portrait" or "rotate90" or "90" => DisplayConfigRotation.Rotate90,
        "landscape-inverted" or "rotate180" or "180" => DisplayConfigRotation.Rotate180,
        "portrait-inverted" or "rotate270" or "270" => DisplayConfigRotation.Rotate270,
        _ => throw new InvalidDataException($"Profile '{profile}', monitor '{monitor}' has unknown rotation '{value}'.")
    };

    private static string? ResolveWallpaperPath(string? value, string configurationPath, string profile, string monitor)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
            var directory = Path.GetDirectoryName(configurationPath)
                            ?? throw new InvalidDataException("The configuration directory could not be determined.");
            var fullPath = Path.GetFullPath(expanded, directory);
            if (!File.Exists(fullPath))
                throw new InvalidDataException(
                    $"Profile '{profile}', monitor '{monitor}' wallpaper was not found: {fullPath}");
            return fullPath;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDataException(
                $"Profile '{profile}', monitor '{monitor}' has an invalid wallpaper path '{value}'.", ex);
        }
    }

    private static string? ResolveWallpaperFolder(string? value, string configurationPath, string profile, string monitor)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
            var directory = Path.GetDirectoryName(configurationPath)
                            ?? throw new InvalidDataException("The configuration directory could not be determined.");
            var fullPath = Path.GetFullPath(expanded, directory);
            if (!Directory.Exists(fullPath))
                throw new InvalidDataException(
                    $"Profile '{profile}', monitor '{monitor}' wallpaper folder was not found: {fullPath}");
            if (!DesktopWallpaperManager.HasSupportedImages(fullPath))
                throw new InvalidDataException(
                    $"Profile '{profile}', monitor '{monitor}' wallpaper folder contains no supported images: {fullPath}");
            return fullPath;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"Profile '{profile}', monitor '{monitor}' has an invalid wallpaperFolder path '{value}'.", ex);
        }
    }

    private static SlideshowOrder ParseSlideshowOrder(string? value, string profile, string monitor) =>
        (value ?? "random").Trim().ToLowerInvariant() switch
        {
            "random" => SlideshowOrder.Random,
            "sequential" => SlideshowOrder.Sequential,
            _ => throw new InvalidDataException(
                $"Profile '{profile}', monitor '{monitor}' has unknown slideshowOrder '{value}'.")
        };

    private static void ValidateLayout(string name, IReadOnlyList<MonitorProfile> entries)
    {
        var primaries = entries.Count(e => e.Primary);
        if (primaries != 1)
            throw new InvalidDataException($"Profile '{name}' must contain exactly one primary monitor; found {primaries}.");
        var primary = entries.Single(e => e.Primary);
        if (primary.X != 0 || primary.Y != 0)
            throw new InvalidDataException($"Profile '{name}' primary monitor must be positioned at (0,0).");

        for (var i = 0; i < entries.Count; i++)
        for (var j = i + 1; j < entries.Count; j++)
            if (Overlaps(entries[i], entries[j]))
                throw new InvalidDataException($"Profile '{name}' monitors '{entries[i].Alias}' and '{entries[j].Alias}' overlap.");

        if (entries.Count <= 1) return;
        var reached = new HashSet<int> { 0 };
        bool changed;
        do
        {
            changed = false;
            for (var i = 0; i < entries.Count; i++)
            for (var j = 0; j < entries.Count; j++)
                if (reached.Contains(i) && !reached.Contains(j) && Touches(entries[i], entries[j]))
                    changed |= reached.Add(j);
        } while (changed);
        if (reached.Count != entries.Count)
            throw new InvalidDataException($"Profile '{name}' contains a gap; every monitor rectangle must touch the layout.");
    }

    private static bool Overlaps(MonitorProfile a, MonitorProfile b) =>
        a.X < (long)b.X + b.Width && (long)a.X + a.Width > b.X && a.Y < (long)b.Y + b.Height && (long)a.Y + a.Height > b.Y;

    private static bool Touches(MonitorProfile a, MonitorProfile b)
    {
        var horizontal = ((long)a.X + a.Width == b.X || (long)b.X + b.Width == a.X) &&
                         Math.Max(a.Y, b.Y) < Math.Min((long)a.Y + a.Height, (long)b.Y + b.Height);
        var vertical = ((long)a.Y + a.Height == b.Y || (long)b.Y + b.Height == a.Y) &&
                       Math.Max(a.X, b.X) < Math.Min((long)a.X + a.Width, (long)b.X + b.Width);
        return horizontal || vertical;
    }

    private sealed class ConfigurationDocument
    {
        public int SchemaVersion { get; set; }
        public bool AllowUnmanagedTargets { get; set; }
        public Dictionary<string, MonitorDocument> Monitors { get; set; } = new();
        public Dictionary<string, ProfileDocument> Profiles { get; set; } = new();
    }
    private sealed class MonitorDocument
    {
        public string Match { get; set; } = string.Empty;
        public uint Width { get; set; }
        public uint Height { get; set; }
        public double RefreshHz { get; set; }
        public string Rotation { get; set; } = "landscape";
        public int X { get; set; }
        public int Y { get; set; }
    }
    private sealed class ProfileDocument { public List<ProfileMonitorDocument> Monitors { get; set; } = []; }
    private sealed class ProfileMonitorDocument
    {
        public string Monitor { get; set; } = string.Empty;
        public bool Primary { get; set; }
        public string? Wallpaper { get; set; }
        public string? WallpaperFolder { get; set; }
        public int? SlideshowInterval { get; set; }
        public string? SlideshowOrder { get; set; }
    }
}

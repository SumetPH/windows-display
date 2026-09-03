using DisplayMode.Native;

namespace DisplayMode.Models;

internal sealed record MonitorDefinition(string Alias, string Match);

internal sealed record MonitorProfile(
    string Alias,
    string HardwareId,
    uint Width,
    uint Height,
    double RefreshHz,
    DisplayConfigRotation Rotation,
    int X,
    int Y,
    bool Primary,
    string? WallpaperPath,
    string? WallpaperFolder,
    int SlideshowIntervalSeconds,
    SlideshowOrder SlideshowOrder);

internal enum SlideshowOrder
{
    Random,
    Sequential
}

internal sealed record DisplayProfile(string Name, IReadOnlyList<MonitorProfile> ActiveMonitors);

internal sealed record DisplayModeConfiguration(
    string Path,
    IReadOnlyDictionary<string, MonitorDefinition> Monitors,
    IReadOnlyDictionary<string, DisplayProfile> Profiles,
    bool AllowUnmanagedTargets);

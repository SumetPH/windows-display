using DisplayMode.Native;

namespace DisplayMode.Models;

internal sealed record MonitorProfile(
    string HardwareId,
    uint Width,
    uint Height,
    uint RefreshHz,
    DisplayConfigRotation Rotation,
    int X,
    int Y,
    bool Primary);

internal sealed record DisplayProfile(string Name, IReadOnlyList<MonitorProfile> ActiveMonitors)
{
    internal static readonly IReadOnlyDictionary<string, DisplayProfile> Profiles =
        new Dictionary<string, DisplayProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["3"] = new("3", [
                new("DELA274", 3840, 2160, 120, DisplayConfigRotation.Identity, 0, 0, true)
            ]),
            ["13"] = new("13", [
                new("DELA274", 3840, 2160, 120, DisplayConfigRotation.Identity, 0, 0, true),
                new("GSM5BD2", 1440, 2560, 120, DisplayConfigRotation.Rotate90, -1440, 0, false)
            ]),
            ["2"] = new("2", [
                new("HEC002F", 3840, 2160, 60, DisplayConfigRotation.Identity, 0, 0, true)
            ]),
            // Calibration topology: use the monitor's real preferred ~144 Hz timing so that
            // GSM5BD2 can be activated safely before the user selects 120 Hz and captures it.
            ["prepare-13"] = new("prepare-13", [
                new("DELA274", 3840, 2160, 120, DisplayConfigRotation.Identity, 0, 0, true),
                new("GSM5BD2", 1440, 2560, 144, DisplayConfigRotation.Rotate90, -1440, 0, false)
            ])
        };

    internal static readonly string[] KnownHardwareIds = ["DELA274", "HEC002F", "GSM5BD2"];
}

using DisplayMode.Native;

namespace DisplayMode.Displays;

internal sealed record DisplaySnapshot(IReadOnlyList<Models.MonitorInfo> Monitors);

internal sealed record ProposedTopology(
    DisplayConfigPathInfo[] Paths,
    DisplayConfigModeInfo[] Modes,
    IReadOnlyList<Models.MonitorProfile> Profiles,
    IReadOnlyDictionary<string, Models.MonitorInfo> Monitors,
    IReadOnlyDictionary<string, DisplayConfigRational> RefreshRates,
    IReadOnlySet<string> ReusedTargetTimings);

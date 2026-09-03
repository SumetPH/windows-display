using DisplayMode.Models;
using DisplayMode.Native;

namespace DisplayMode.Displays;

internal sealed class DisplayTopologyManager(bool verbose, DisplayModeConfiguration configuration)
{
    private DisplaySnapshot? lastSnapshot;
    private readonly TargetTimingStore timingStore = CreateTimingStore(verbose);

    private static TargetTimingStore CreateTimingStore(bool verbose)
    {
        var store = new TargetTimingStore(verbose);
        store.Load();
        return store;
    }

    internal DisplaySnapshot Discover(bool announce = true)
    {
        if (announce)
            Console.WriteLine("Discovering display paths...");
        var snapshot = DisplayDiscovery.QueryAllPaths(verbose, configuration.Monitors.Values.Select(m => m.Match));
        lastSnapshot = snapshot;
        if (announce)
            Console.WriteLine();
        return snapshot;
    }

    internal bool TryResolveKnownMonitors(DisplayProfile profile, DisplaySnapshot snapshot,
        out Dictionary<string, MonitorInfo> result, bool reportErrors = true)
    {
        result = new(StringComparer.OrdinalIgnoreCase);
        var safe = true;

        // A supplied CCD topology contains only the paths that should remain active, so a
        // configured monitor that is not part of this profile does not need to be present.
        // Requiring every configured monitor here prevented otherwise valid profiles from
        // being applied whenever an unused monitor was powered off or disconnected.
        foreach (var requested in profile.ActiveMonitors)
        {
            var id = requested.HardwareId;
            var rawMatches = snapshot.Monitors.Where(m =>
                m.DevicePath.Contains(id, StringComparison.OrdinalIgnoreCase) ||
                m.FriendlyName.Contains(id, StringComparison.OrdinalIgnoreCase)).ToList();

            if (rawMatches.Count != 1)
            {
                if (reportErrors)
                    Console.Error.WriteLine(rawMatches.Count == 0
                        ? $"ERROR: Required monitor {id} was not found."
                        : $"ERROR: Monitor identifier {id} matched {rawMatches.Count} physical targets; refusing to guess.");
                safe = false;
            }
            else
            {
                result[id] = rawMatches[0];
            }
        }

        if (!configuration.AllowUnmanagedTargets)
        {
            var unmanaged = snapshot.Monitors.Where(m => m.HardwareId is null).ToList();
            if (unmanaged.Count > 0)
            {
                if (reportErrors)
                    Console.Error.WriteLine($"ERROR: Found {unmanaged.Count} connected monitor target(s) not declared in profiles.json; refusing an exclusive topology change.");
                safe = false;
            }
        }

        if (!safe && reportErrors)
        {
            Console.Error.WriteLine("No display changes were made. Detected targets:");
            PrintMonitors(snapshot, activeOnly: false);
        }

        return safe;
    }

    internal static bool RequiredTargetsAvailable(DisplayProfile profile,
        IReadOnlyDictionary<string, MonitorInfo> monitors) =>
        profile.ActiveMonitors.All(requested =>
            monitors.TryGetValue(requested.HardwareId, out var monitor) &&
            monitor.CandidatePaths.Any(path => path.TargetInfo.TargetAvailable));

    internal static bool IsProfileActive(DisplayProfile profile, DisplaySnapshot snapshot) =>
        profile.ActiveMonitors.All(requested => snapshot.Monitors.Any(monitor =>
            monitor.Active && requested.HardwareId.Equals(monitor.HardwareId, StringComparison.OrdinalIgnoreCase)));

    internal ProposedTopology Build(DisplayProfile profile, DisplaySnapshot snapshot, Dictionary<string, MonitorInfo> monitors)
    {
        var selectedPaths = new List<DisplayConfigPathInfo>();
        var selectedModes = new List<DisplayConfigModeInfo>();
        var usedSources = new HashSet<(Luid Adapter, uint Source)>();
        var rates = new Dictionary<string, DisplayConfigRational>(StringComparer.OrdinalIgnoreCase);
        var reusedTimings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requested in profile.ActiveMonitors.OrderByDescending(p => p.Primary))
        {
            var monitor = monitors[requested.HardwareId];
            if (!monitor.CandidatePaths.Any(p => p.TargetInfo.TargetAvailable))
                throw new InvalidOperationException($"Monitor {requested.HardwareId} was identified but its CCD target is not currently available.");
            var path = SelectPath(monitor, usedSources);
            if (!path.HasValue)
                throw new InvalidOperationException($"No distinct source path is available for {requested.HardwareId}.");

            var configuredPath = path.Value;
            if (verbose)
            {
                Console.WriteLine($"Raw candidate for {requested.HardwareId}: adapter={configuredPath.TargetInfo.AdapterId}, " +
                    $"source={configuredPath.SourceInfo.Id}, target={configuredPath.TargetInfo.Id}, " +
                    $"sourceMode={configuredPath.SourceInfo.ModeInfoIdx}, targetMode={configuredPath.TargetInfo.ModeInfoIdx}, " +
                    $"available={configuredPath.TargetInfo.TargetAvailable}, output={configuredPath.TargetInfo.OutputTechnology}, " +
                    $"rotation={configuredPath.TargetInfo.Rotation}, scaling={configuredPath.TargetInfo.Scaling}, " +
                    $"refresh={configuredPath.TargetInfo.RefreshRate}");
                if (monitor.PreferredTargetMode is { } preferredMode)
                    Console.WriteLine($"Preferred target mode for {requested.HardwareId}: {monitor.PreferredWidth}x{monitor.PreferredHeight}, " +
                        $"signal={preferredMode.TargetVideoSignalInfo.ActiveSize.Cx}x{preferredMode.TargetVideoSignalInfo.ActiveSize.Cy}, " +
                        $"refresh={preferredMode.TargetVideoSignalInfo.VSyncFreq}");
            }
            usedSources.Add((configuredPath.SourceInfo.AdapterId, configuredPath.SourceInfo.Id));

            var sourceMode = new DisplayConfigSourceMode
            {
                // CCD source dimensions are expressed before target rotation. A 2560x1440
                // source rotated 90 degrees therefore occupies 1440x2560 on the desktop.
                Width = requested.Rotation is DisplayConfigRotation.Rotate90 or DisplayConfigRotation.Rotate270
                    ? requested.Height : requested.Width,
                Height = requested.Rotation is DisplayConfigRotation.Rotate90 or DisplayConfigRotation.Rotate270
                    ? requested.Width : requested.Height,
                PixelFormat = monitor.ActiveSourceMode?.PixelFormat ?? DisplayConfigPixelFormat.PixelFormat32Bpp,
                Position = new PointL { X = requested.X, Y = requested.Y }
            };

            configuredPath.SourceInfo.ModeInfoIdx = (uint)selectedModes.Count;
            selectedModes.Add(new DisplayConfigModeInfo
            {
                InfoType = DisplayConfigModeInfoType.Source,
                Id = configuredPath.SourceInfo.Id,
                AdapterId = configuredPath.SourceInfo.AdapterId,
                ModeInfo = new DisplayConfigModeInfoUnion { SourceMode = sourceMode }
            });

            var timing = FindExactObservedTiming(monitor, requested);
            var rate = FindObservedRefresh(monitor, requested.RefreshHz)
                       ?? timing?.TargetVideoSignalInfo.VSyncFreq
                       ?? RationalFromHertz(requested.RefreshHz);

            if (timing.HasValue)
            {
                configuredPath.TargetInfo.ModeInfoIdx = (uint)selectedModes.Count;
                selectedModes.Add(new DisplayConfigModeInfo
                {
                    InfoType = DisplayConfigModeInfoType.Target,
                    Id = configuredPath.TargetInfo.Id,
                    AdapterId = configuredPath.TargetInfo.AdapterId,
                    ModeInfo = new DisplayConfigModeInfoUnion { TargetMode = timing.Value }
                });
                reusedTimings.Add(requested.HardwareId);
            }
            else
            {
                configuredPath.TargetInfo.ModeInfoIdx = DisplayConfigConstants.ModeIndexInvalid;
            }

            configuredPath.TargetInfo.OutputTechnology = monitor.OutputTechnology;
            configuredPath.TargetInfo.Rotation = requested.Rotation;
            configuredPath.TargetInfo.Scaling = DisplayConfigScaling.Preferred;
            configuredPath.TargetInfo.RefreshRate = rate;
            configuredPath.TargetInfo.ScanLineOrdering = timing?.TargetVideoSignalInfo.ScanLineOrdering
                ?? DisplayConfigScanLineOrdering.Unspecified;
            configuredPath.Flags = DisplayConfigConstants.PathActive;
            selectedPaths.Add(configuredPath);
            rates[requested.HardwareId] = rate;
        }

        return new ProposedTopology(selectedPaths.ToArray(), selectedModes.ToArray(), profile.ActiveMonitors,
            monitors, rates, reusedTimings);
    }

    internal int CaptureTimings(DisplaySnapshot snapshot)
    {
        var captured = timingStore.Capture(snapshot);
        Console.WriteLine(captured > 0
            ? $"Captured {captured} active native target timing(s) to {timingStore.CachePath}."
            : captured == 0
                ? "No new timings captured. A known monitor must be active at its requested resolution and refresh rate."
                : "Native timing was found, but the timing cache could not be saved.");
        return captured > 0 ? 0 : 1;
    }

    internal void CacheObservedTimings(DisplaySnapshot snapshot) => timingStore.Capture(snapshot);

    internal int ValidateAndApply(string modeName, ProposedTopology proposed, bool dryRun, bool validateOnly)
    {
        PrintProposal(modeName, proposed);
        if (dryRun)
        {
            Console.WriteLine("DRY RUN: SetDisplayConfig was not called.");
            return 0;
        }

        Console.WriteLine("Validating configuration...");
        var validateFlags = SetDisplayConfigFlags.Validate | SetDisplayConfigFlags.UseSuppliedDisplayConfig;
        var result = DisplayConfigNative.SetDisplayConfig((uint)proposed.Paths.Length, proposed.Paths,
            (uint)proposed.Modes.Length, ModesOrNull(proposed.Modes), validateFlags);
        if (result == DisplayConfigConstants.ErrorSuccess)
        {
            Console.WriteLine("Validation succeeded.");
            if (validateOnly)
            {
                Console.WriteLine("VALIDATE ONLY: configuration was not applied.");
                return 0;
            }

            return ApplyFinal(modeName, proposed);
        }

        if (proposed.ReusedTargetTimings.Count == proposed.Profiles.Count)
            return ReportSetError("Validation", result, modeName);

        Console.Error.WriteLine($"Validation failed for mode {modeName}: {DisplayDiscovery.ErrorText(result)}");
        var missing = proposed.Profiles
            .Where(p => !proposed.ReusedTargetTimings.Contains(p.HardwareId))
            .Select(p => p.HardwareId)
            .ToArray();
        Console.Error.WriteLine($"No complete native timing is available for: {string.Join(", ", missing)}.");
        Console.Error.WriteLine("Set the monitor(s) to the requested refresh rate once in Windows, then run 'DisplayMode.exe capture --verbose'.");
        Console.Error.WriteLine("The current display topology was not changed.");
        return 1;
    }

    private int ApplyFinal(string modeName, ProposedTopology topology)
    {
        Console.WriteLine("Applying configuration...");
        var flags = SetDisplayConfigFlags.Apply | SetDisplayConfigFlags.UseSuppliedDisplayConfig |
                    SetDisplayConfigFlags.SaveToDatabase;
        var result = DisplayConfigNative.SetDisplayConfig((uint)topology.Paths.Length, topology.Paths,
            (uint)topology.Modes.Length, ModesOrNull(topology.Modes), flags);
        if (result != DisplayConfigConstants.ErrorSuccess)
            return ReportSetError("Apply", result, modeName);

        Console.WriteLine("Success.");
        return DesktopWallpaperManager.Apply(topology.Profiles, topology.Monitors) ? 0 : 1;
    }

    internal void PrintMonitors(DisplaySnapshot snapshot, bool activeOnly)
    {
        var monitors = activeOnly ? snapshot.Monitors.Where(m => m.Active) : snapshot.Monitors;
        Console.WriteLine(activeOnly ? "Current active topology:" : "Detected monitors:");
        Console.WriteLine();

        foreach (var monitor in monitors.OrderBy(m => m.HardwareId ?? m.FriendlyName))
        {
            var label = monitor.HardwareId ?? (string.IsNullOrWhiteSpace(monitor.FriendlyName) ? "Unknown" : monitor.FriendlyName);
            Console.WriteLine(label);
            Console.WriteLine($"  Device Path: {monitor.DevicePath}");
            Console.WriteLine($"  Friendly Name: {monitor.FriendlyName}");
            Console.WriteLine($"  Adapter LUID: {monitor.Key.AdapterId}");
            Console.WriteLine($"  Target ID: {monitor.Key.TargetId}");
            Console.WriteLine($"  Source ID: {(monitor.ActivePath.HasValue ? monitor.ActivePath.Value.SourceInfo.Id.ToString() : "inactive")}");
            Console.WriteLine($"  Active: {monitor.Active.ToString().ToLowerInvariant()}");
            if (monitor.ActivePath is { } path)
            {
                var source = monitor.ActiveSourceMode;
                var dimensions = source.HasValue ? EffectiveDimensions(source.Value, path.TargetInfo.Rotation) : null;
                Console.WriteLine($"  Resolution: {(dimensions.HasValue ? $"{dimensions.Value.Width}x{dimensions.Value.Height}" : "unknown")}");
                Console.WriteLine($"  Refresh: {EffectiveRefresh(path, monitor.ActiveTargetMode)}");
                Console.WriteLine($"  Position: {(source.HasValue ? $"{source.Value.Position.X},{source.Value.Position.Y}" : "unknown")}");
                Console.WriteLine($"  Rotation: {RotationText(path.TargetInfo.Rotation)}");
            }
            Console.WriteLine();
        }

        if (!monitors.Any())
            Console.WriteLine("  (none)");
    }

    internal static void PrintInteropSizes()
    {
        Console.WriteLine("CCD interop structure sizes:");
        Console.WriteLine($"  DISPLAYCONFIG_PATH_SOURCE_INFO: {System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfigPathSourceInfo>()}");
        Console.WriteLine($"  DISPLAYCONFIG_PATH_TARGET_INFO: {System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfigPathTargetInfo>()}");
        Console.WriteLine($"  DISPLAYCONFIG_PATH_INFO: {System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfigPathInfo>()}");
        Console.WriteLine($"  DISPLAYCONFIG_MODE_INFO: {System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfigModeInfo>()}");
        Console.WriteLine($"  DISPLAYCONFIG_TARGET_DEVICE_NAME: {System.Runtime.InteropServices.Marshal.SizeOf<DisplayConfigTargetDeviceName>()}");
        Console.WriteLine();
    }

    internal static void ValidateInteropLayout()
    {
        var expected = new (Type Type, int Size)[]
        {
            (typeof(Luid), 8),
            (typeof(DisplayConfigVideoSignalInfo), 48),
            (typeof(DisplayConfigPathSourceInfo), 20),
            (typeof(DisplayConfigPathTargetInfo), 48),
            (typeof(DisplayConfigPathInfo), 72),
            (typeof(DisplayConfigModeInfo), 64),
            (typeof(DisplayConfigTargetDeviceName), 420),
            (typeof(DisplayConfigTargetPreferredMode), 80)
        };

        foreach (var item in expected)
        {
            var actual = System.Runtime.InteropServices.Marshal.SizeOf(item.Type);
            if (actual != item.Size)
                throw new PlatformNotSupportedException(
                    $"Unsafe CCD interop layout: {item.Type.Name} is {actual} bytes; expected {item.Size} on Windows x64.");
        }
    }

    private static DisplayConfigPathInfo? SelectPath(MonitorInfo monitor, HashSet<(Luid Adapter, uint Source)> used)
    {
        var candidates = monitor.CandidatePaths
            .Where(p => p.TargetInfo.TargetAvailable)
            .OrderByDescending(p => (p.Flags & DisplayConfigConstants.PathActive) != 0)
            .ThenByDescending(p => p.TargetInfo.TargetAvailable);
        foreach (var path in candidates)
            if (!used.Contains((path.SourceInfo.AdapterId, path.SourceInfo.Id)))
                return path;
        return null;
    }

    private DisplayConfigTargetMode? FindExactObservedTiming(MonitorInfo monitor, MonitorProfile profile)
    {
        foreach (var timing in new[] { monitor.ActiveTargetMode, monitor.PreferredTargetMode })
        {
            if (!timing.HasValue)
                continue;
            var signal = timing.Value.TargetVideoSignalInfo;
            var dimensionsMatch = (signal.ActiveSize.Cx == profile.Width && signal.ActiveSize.Cy == profile.Height) ||
                                  (profile.Rotation is DisplayConfigRotation.Rotate90 or DisplayConfigRotation.Rotate270 &&
                                   signal.ActiveSize.Cx == profile.Height && signal.ActiveSize.Cy == profile.Width);
            if (dimensionsMatch && Near(signal.VSyncFreq.Hertz, profile.RefreshHz))
                return timing.Value;
        }
        return timingStore.Find(profile);
    }

    private static DisplayConfigRational? FindObservedRefresh(MonitorInfo monitor, double desiredHz)
    {
        if (monitor.ActivePath is { } path && Near(path.TargetInfo.RefreshRate.Hertz, desiredHz))
            return path.TargetInfo.RefreshRate;
        if (monitor.ActiveTargetMode is { } target && Near(target.TargetVideoSignalInfo.VSyncFreq.Hertz, desiredHz))
            return target.TargetVideoSignalInfo.VSyncFreq;
        return null;
    }

    private static bool Near(double actual, double desired) => actual > 0 && Math.Abs(actual - desired) <= 0.5;

    private static DisplayConfigRational RationalFromHertz(double hertz)
    {
        const uint denominator = 1000;
        var numerator = checked((uint)Math.Round(hertz * denominator));
        var divisor = GreatestCommonDivisor(numerator, denominator);
        return new DisplayConfigRational { Numerator = numerator / divisor, Denominator = denominator / divisor };
    }

    private static uint GreatestCommonDivisor(uint a, uint b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }

    private static DisplayConfigModeInfo[]? ModesOrNull(DisplayConfigModeInfo[] modes) =>
        modes.Length == 0 ? null : modes;

    private static string EffectiveRefresh(DisplayConfigPathInfo path, DisplayConfigTargetMode? target)
    {
        var rate = target?.TargetVideoSignalInfo.VSyncFreq ?? path.TargetInfo.RefreshRate;
        return rate.ToString();
    }

    private static (uint Width, uint Height)? EffectiveDimensions(DisplayConfigSourceMode source, DisplayConfigRotation rotation) =>
        rotation is DisplayConfigRotation.Rotate90 or DisplayConfigRotation.Rotate270
            ? (source.Height, source.Width)
            : (source.Width, source.Height);

    private void PrintProposal(string modeName, ProposedTopology proposed)
    {
        Console.WriteLine($"Applying mode {modeName}:");
        Console.WriteLine();
        Console.WriteLine("ACTIVE:");
        foreach (var p in proposed.Profiles)
        {
            var orientation = RotationText(p.Rotation);
            var primary = p.Primary ? " PRIMARY" : string.Empty;
            var timing = proposed.ReusedTargetTimings.Contains(p.HardwareId) ? ", complete native timing supplied" : ", target timing selected by CCD";
            Console.WriteLine($"  {p.Alias} ({p.HardwareId}) {p.Width}x{p.Height} @{proposed.RefreshRates[p.HardwareId]} {orientation} ({p.X},{p.Y}){primary}{timing}");
            if (p.WallpaperPath is not null)
                Console.WriteLine($"    Wallpaper: {p.WallpaperPath}");
            else if (p.WallpaperFolder is not null)
                Console.WriteLine($"    Wallpaper slideshow: {p.WallpaperFolder} every {p.SlideshowIntervalSeconds}s ({p.SlideshowOrder.ToString().ToLowerInvariant()})");
        }
        Console.WriteLine();
        Console.WriteLine("INACTIVE:");
        foreach (var definition in configuration.Monitors.Values.Where(m =>
                     !proposed.Profiles.Any(p => p.HardwareId.Equals(m.Match, StringComparison.OrdinalIgnoreCase))))
            Console.WriteLine($"  {definition.Alias} ({definition.Match})");
        Console.WriteLine();

        if (verbose)
        {
            foreach (var path in proposed.Paths)
                Console.WriteLine($"  Path: adapter={path.TargetInfo.AdapterId}, source={path.SourceInfo.Id}, target={path.TargetInfo.Id}, sourceMode={path.SourceInfo.ModeInfoIdx}, targetMode={path.TargetInfo.ModeInfoIdx}");
            Console.WriteLine();
        }
    }

    private int ReportSetError(string operation, int result, string modeName)
    {
        Console.Error.WriteLine($"{operation} failed for mode {modeName}: {DisplayDiscovery.ErrorText(result)}");
        Console.Error.WriteLine(operation == "Validation"
            ? "The current display topology was not changed."
            : "Windows rejected the apply operation after successful validation; query 'status' to inspect the current topology.");
        if (lastSnapshot is not null)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Active paths before the request:");
            PrintMonitors(lastSnapshot, activeOnly: true);
        }
        return 1;
    }

    private static string RotationText(DisplayConfigRotation rotation) => rotation switch
    {
        DisplayConfigRotation.Identity => "landscape",
        DisplayConfigRotation.Rotate90 => "portrait",
        DisplayConfigRotation.Rotate180 => "landscape-inverted",
        DisplayConfigRotation.Rotate270 => "portrait-inverted",
        _ => $"unknown ({(uint)rotation})"
    };
}

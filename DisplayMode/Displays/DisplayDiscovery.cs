using System.ComponentModel;
using System.Runtime.InteropServices;
using DisplayMode.Models;
using DisplayMode.Native;

namespace DisplayMode.Displays;

internal static class DisplayDiscovery
{
    private const int MaxQueryAttempts = 8;

    internal static DisplaySnapshot QueryAllPaths(bool verbose, IEnumerable<string> monitorIdentifiers)
    {
        var (paths, modes) = Query(QueryDisplayConfigFlags.AllPaths);
        var grouped = paths.GroupBy(p => new TargetKey(p.TargetInfo.AdapterId, p.TargetInfo.Id));
        var monitors = new List<MonitorInfo>();

        foreach (var group in grouped)
        {
            var sample = group.First();
            var packet = new DisplayConfigTargetDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = DisplayConfigDeviceInfoType.GetTargetName,
                    Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                    AdapterId = group.Key.AdapterId,
                    Id = group.Key.TargetId
                },
                MonitorFriendlyDeviceName = string.Empty,
                MonitorDevicePath = string.Empty
            };

            var result = DisplayConfigNative.DisplayConfigGetDeviceInfo(ref packet);
            if (result != DisplayConfigConstants.ErrorSuccess)
            {
                if (verbose)
                    Console.WriteLine($"Skipping target {group.Key.AdapterId}/{group.Key.TargetId}: DisplayConfigGetDeviceInfo failed: {ErrorText(result)}");
                continue;
            }

            // Targets with no connected monitor normally have an empty path and are not useful here.
            if (string.IsNullOrWhiteSpace(packet.MonitorDevicePath) &&
                string.IsNullOrWhiteSpace(packet.MonitorFriendlyDeviceName))
                continue;

            var candidates = group.ToList();
            var activePath = candidates.FirstOrDefault(p => (p.Flags & DisplayConfigConstants.PathActive) != 0);
            var hasActive = candidates.Any(p => (p.Flags & DisplayConfigConstants.PathActive) != 0);
            var preferred = new DisplayConfigTargetPreferredMode
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = DisplayConfigDeviceInfoType.GetTargetPreferredMode,
                    Size = (uint)Marshal.SizeOf<DisplayConfigTargetPreferredMode>(),
                    AdapterId = group.Key.AdapterId,
                    Id = group.Key.TargetId
                }
            };
            var preferredResult = DisplayConfigNative.DisplayConfigGetPreferredMode(ref preferred);
            if (preferredResult != DisplayConfigConstants.ErrorSuccess && verbose)
                Console.WriteLine($"Preferred-mode query failed for target {group.Key.AdapterId}/{group.Key.TargetId}: {ErrorText(preferredResult)}");

            monitors.Add(new MonitorInfo
            {
                Key = group.Key,
                FriendlyName = packet.MonitorFriendlyDeviceName ?? string.Empty,
                DevicePath = packet.MonitorDevicePath ?? string.Empty,
                OutputTechnology = packet.OutputTechnology,
                CandidatePaths = candidates,
                ActivePath = hasActive ? activePath : null,
                ActiveSourceMode = hasActive ? SourceMode(activePath, modes) : null,
                ActiveTargetMode = hasActive ? TargetMode(activePath, modes) : null,
                PreferredTargetMode = preferredResult == DisplayConfigConstants.ErrorSuccess ? preferred.TargetMode : null,
                PreferredWidth = preferred.Width,
                PreferredHeight = preferred.Height
            });
        }

        MatchKnownHardware(monitors, monitorIdentifiers);
        return new DisplaySnapshot(paths, modes, monitors);
    }

    private static (DisplayConfigPathInfo[] Paths, DisplayConfigModeInfo[] Modes) Query(QueryDisplayConfigFlags flags)
    {
        for (var attempt = 1; attempt <= MaxQueryAttempts; attempt++)
        {
            var result = DisplayConfigNative.GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            if (result != DisplayConfigConstants.ErrorSuccess)
                throw new Win32Exception(result, $"GetDisplayConfigBufferSizes failed: {ErrorText(result)}");

            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            result = DisplayConfigNative.QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, 0);

            if (result == DisplayConfigConstants.ErrorInsufficientBuffer)
                continue;
            if (result != DisplayConfigConstants.ErrorSuccess)
                throw new Win32Exception(result, $"QueryDisplayConfig failed: {ErrorText(result)}");

            return (paths[..checked((int)pathCount)], modes[..checked((int)modeCount)]);
        }

        throw new Win32Exception(DisplayConfigConstants.ErrorInsufficientBuffer,
            $"Display topology kept changing during {MaxQueryAttempts} QueryDisplayConfig attempts.");
    }

    private static void MatchKnownHardware(List<MonitorInfo> monitors, IEnumerable<string> monitorIdentifiers)
    {
        foreach (var hardwareId in monitorIdentifiers)
        {
            var matches = monitors.Where(m =>
                m.DevicePath.Contains(hardwareId, StringComparison.OrdinalIgnoreCase) ||
                m.FriendlyName.Contains(hardwareId, StringComparison.OrdinalIgnoreCase)).ToList();

            // Assignment only happens for an unambiguous physical target. The manager reports errors.
            if (matches.Count == 1)
                matches[0].HardwareId = hardwareId;
        }
    }

    internal static DisplayConfigSourceMode? SourceMode(DisplayConfigPathInfo path, DisplayConfigModeInfo[] modes)
    {
        var index = path.SourceInfo.ModeInfoIdx;
        if (index == DisplayConfigConstants.ModeIndexInvalid || index >= modes.Length)
            return null;
        var mode = modes[index];
        return mode.InfoType == DisplayConfigModeInfoType.Source ? mode.ModeInfo.SourceMode : null;
    }

    internal static DisplayConfigTargetMode? TargetMode(DisplayConfigPathInfo path, DisplayConfigModeInfo[] modes)
    {
        var index = path.TargetInfo.ModeInfoIdx;
        if (index == DisplayConfigConstants.ModeIndexInvalid || index >= modes.Length)
            return null;
        var mode = modes[index];
        return mode.InfoType == DisplayConfigModeInfoType.Target ? mode.ModeInfo.TargetMode : null;
    }

    internal static string ErrorText(int code)
    {
        var message = new Win32Exception(code).Message;
        return $"{code} (0x{code:X8}, {message})";
    }
}

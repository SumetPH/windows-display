using System.Text.Json;
using DisplayMode.Models;
using DisplayMode.Native;

namespace DisplayMode.Displays;

internal sealed class TargetTimingStore(bool verbose)
{
    private readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DisplayMode", "target-timings.json");
    private readonly Dictionary<string, TargetTimingDto> timings = new(StringComparer.OrdinalIgnoreCase);

    internal void Load()
    {
        try
        {
            if (!File.Exists(path))
                return;
            foreach (var item in JsonSerializer.Deserialize<List<TargetTimingDto>>(File.ReadAllText(path)) ?? [])
                timings[item.HardwareId] = item;
            if (verbose)
                Console.WriteLine($"Loaded {timings.Count} cached native target timing(s) from {path}.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WARNING: Could not read target timing cache '{path}': {ex.Message}");
        }
    }

    internal DisplayConfigTargetMode? Find(MonitorProfile profile)
    {
        if (!timings.TryGetValue(profile.HardwareId, out var item) ||
            item.Width != profile.Width || item.Height != profile.Height ||
            Math.Abs(item.RefreshHz - profile.RefreshHz) > 0.5)
            return null;
        return item.ToNative();
    }

    internal int Capture(DisplaySnapshot snapshot)
    {
        var captured = 0;
        foreach (var monitor in snapshot.Monitors.Where(m => m.HardwareId is not null && m.ActiveTargetMode.HasValue))
        {
            var desired = DisplayProfile.Profiles.Values.SelectMany(p => p.ActiveMonitors)
                .FirstOrDefault(p => p.HardwareId.Equals(monitor.HardwareId, StringComparison.OrdinalIgnoreCase));
            if (desired is null)
                continue;

            var timing = monitor.ActiveTargetMode!.Value;
            var signal = timing.TargetVideoSignalInfo;
            var dimensionsMatch = (signal.ActiveSize.Cx == desired.Width && signal.ActiveSize.Cy == desired.Height) ||
                                  (desired.Rotation is DisplayConfigRotation.Rotate90 or DisplayConfigRotation.Rotate270 &&
                                   signal.ActiveSize.Cx == desired.Height && signal.ActiveSize.Cy == desired.Width);
            if (!dimensionsMatch || Math.Abs(signal.VSyncFreq.Hertz - desired.RefreshHz) > 0.5)
            {
                if (verbose)
                    Console.WriteLine($"Not caching {monitor.HardwareId}: active timing is {signal.ActiveSize.Cx}x{signal.ActiveSize.Cy} @ {signal.VSyncFreq}, requested {desired.RefreshHz} Hz.");
                continue;
            }

            timings[monitor.HardwareId!] = TargetTimingDto.FromNative(monitor.HardwareId!, desired, timing);
            captured++;
        }

        if (captured > 0)
        {
            try
            {
                Save();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"WARNING: Could not save target timing cache '{path}': {ex.Message}");
                return -1;
            }
        }
        return captured;
    }

    internal string CachePath => path;

    private void Save()
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(timings.Values.OrderBy(t => t.HardwareId),
            new JsonSerializerOptions { WriteIndented = true }));
    }

    internal sealed class TargetTimingDto
    {
        public string HardwareId { get; set; } = string.Empty;
        public uint Width { get; set; }
        public uint Height { get; set; }
        public double RefreshHz { get; set; }
        public ulong PixelRate { get; set; }
        public uint HNumerator { get; set; }
        public uint HDenominator { get; set; }
        public uint VNumerator { get; set; }
        public uint VDenominator { get; set; }
        public uint ActiveWidth { get; set; }
        public uint ActiveHeight { get; set; }
        public uint TotalWidth { get; set; }
        public uint TotalHeight { get; set; }
        public uint AdditionalSignalInfo { get; set; }
        public DisplayConfigScanLineOrdering ScanLineOrdering { get; set; }

        internal static TargetTimingDto FromNative(string id, MonitorProfile profile, DisplayConfigTargetMode mode)
        {
            var s = mode.TargetVideoSignalInfo;
            return new TargetTimingDto
            {
                HardwareId = id,
                Width = profile.Width,
                Height = profile.Height,
                RefreshHz = s.VSyncFreq.Hertz,
                PixelRate = s.PixelRate,
                HNumerator = s.HSyncFreq.Numerator,
                HDenominator = s.HSyncFreq.Denominator,
                VNumerator = s.VSyncFreq.Numerator,
                VDenominator = s.VSyncFreq.Denominator,
                ActiveWidth = s.ActiveSize.Cx,
                ActiveHeight = s.ActiveSize.Cy,
                TotalWidth = s.TotalSize.Cx,
                TotalHeight = s.TotalSize.Cy,
                AdditionalSignalInfo = s.AdditionalSignalInfo,
                ScanLineOrdering = s.ScanLineOrdering
            };
        }

        internal DisplayConfigTargetMode ToNative() => new()
        {
            TargetVideoSignalInfo = new DisplayConfigVideoSignalInfo
            {
                PixelRate = PixelRate,
                HSyncFreq = new DisplayConfigRational { Numerator = HNumerator, Denominator = HDenominator },
                VSyncFreq = new DisplayConfigRational { Numerator = VNumerator, Denominator = VDenominator },
                ActiveSize = new DisplayConfig2DRegion { Cx = ActiveWidth, Cy = ActiveHeight },
                TotalSize = new DisplayConfig2DRegion { Cx = TotalWidth, Cy = TotalHeight },
                AdditionalSignalInfo = AdditionalSignalInfo,
                ScanLineOrdering = ScanLineOrdering
            }
        };
    }
}

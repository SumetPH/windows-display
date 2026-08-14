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
                timings[Key(item.HardwareId, item.Width, item.Height, item.RefreshHz)] = item;
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
        return timings.Values.FirstOrDefault(item =>
            item.HardwareId.Equals(profile.HardwareId, StringComparison.OrdinalIgnoreCase) &&
            item.Width == profile.Width && item.Height == profile.Height &&
            Math.Abs(item.RefreshHz - profile.RefreshHz) <= 0.5)?.ToNative();
    }

    internal int Capture(DisplaySnapshot snapshot)
    {
        var captured = 0;
        foreach (var monitor in snapshot.Monitors.Where(m => m.HardwareId is not null &&
                     m.ActiveTargetMode.HasValue && m.ActiveSourceMode.HasValue && m.ActivePath.HasValue))
        {
            var timing = monitor.ActiveTargetMode!.Value;
            var signal = timing.TargetVideoSignalInfo;
            var source = monitor.ActiveSourceMode!.Value;
            var rotation = monitor.ActivePath!.Value.TargetInfo.Rotation;
            var width = rotation is DisplayConfigRotation.Rotate90 or DisplayConfigRotation.Rotate270 ? source.Height : source.Width;
            var height = rotation is DisplayConfigRotation.Rotate90 or DisplayConfigRotation.Rotate270 ? source.Width : source.Height;
            var item = TargetTimingDto.FromNative(monitor.HardwareId!, width, height, timing);
            timings[Key(item.HardwareId, item.Width, item.Height, item.RefreshHz)] = item;
            if (verbose)
                Console.WriteLine($"Caching {monitor.HardwareId}: {width}x{height} @ {signal.VSyncFreq}.");
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

    private static string Key(string id, uint width, uint height, double refresh) =>
        $"{id}|{width}x{height}|{refresh:0.000}";

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

        internal static TargetTimingDto FromNative(string id, uint width, uint height, DisplayConfigTargetMode mode)
        {
            var s = mode.TargetVideoSignalInfo;
            return new TargetTimingDto
            {
                HardwareId = id,
                Width = width,
                Height = height,
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

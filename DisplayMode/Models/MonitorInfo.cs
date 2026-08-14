using DisplayMode.Native;

namespace DisplayMode.Models;

internal readonly record struct TargetKey(Luid AdapterId, uint TargetId);

internal sealed class MonitorInfo
{
    internal required TargetKey Key { get; init; }
    internal required string FriendlyName { get; init; }
    internal required string DevicePath { get; init; }
    internal required DisplayConfigVideoOutputTechnology OutputTechnology { get; init; }
    internal required List<DisplayConfigPathInfo> CandidatePaths { get; init; }
    internal DisplayConfigPathInfo? ActivePath { get; init; }
    internal DisplayConfigSourceMode? ActiveSourceMode { get; init; }
    internal DisplayConfigTargetMode? ActiveTargetMode { get; init; }
    internal DisplayConfigTargetMode? PreferredTargetMode { get; init; }
    internal uint PreferredWidth { get; init; }
    internal uint PreferredHeight { get; init; }
    internal string? HardwareId { get; set; }

    internal bool Active => ActivePath.HasValue;
}

using System.Runtime.InteropServices;

namespace DisplayMode.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct Luid : IEquatable<Luid>
{
    internal uint LowPart;
    internal int HighPart;

    public readonly bool Equals(Luid other) => LowPart == other.LowPart && HighPart == other.HighPart;
    public override readonly bool Equals(object? obj) => obj is Luid other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(LowPart, HighPart);
    public override readonly string ToString() => $"0x{unchecked((ulong)((long)HighPart << 32) | LowPart):X16}";
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigRational
{
    internal uint Numerator;
    internal uint Denominator;

    internal readonly double Hertz => Denominator == 0 ? 0 : (double)Numerator / Denominator;
    public override readonly string ToString() => Denominator == 0 ? "unspecified" : $"{Hertz:0.###} Hz ({Numerator}/{Denominator})";
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfig2DRegion
{
    internal uint Cx;
    internal uint Cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PointL
{
    internal int X;
    internal int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigVideoSignalInfo
{
    internal ulong PixelRate;
    internal DisplayConfigRational HSyncFreq;
    internal DisplayConfigRational VSyncFreq;
    internal DisplayConfig2DRegion ActiveSize;
    internal DisplayConfig2DRegion TotalSize;
    internal uint AdditionalSignalInfo;
    internal DisplayConfigScanLineOrdering ScanLineOrdering;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigTargetMode
{
    internal DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigSourceMode
{
    internal uint Width;
    internal uint Height;
    internal DisplayConfigPixelFormat PixelFormat;
    internal PointL Position;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigDesktopImageInfo
{
    internal PointL PathSourceSize;
    internal RectL DesktopImageRegion;
    internal RectL DesktopImageClip;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RectL
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
}

[StructLayout(LayoutKind.Explicit)]
internal struct DisplayConfigModeInfoUnion
{
    [FieldOffset(0)] internal DisplayConfigTargetMode TargetMode;
    [FieldOffset(0)] internal DisplayConfigSourceMode SourceMode;
    [FieldOffset(0)] internal DisplayConfigDesktopImageInfo DesktopImageInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigModeInfo
{
    internal DisplayConfigModeInfoType InfoType;
    internal uint Id;
    internal Luid AdapterId;
    internal DisplayConfigModeInfoUnion ModeInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathSourceInfo
{
    internal Luid AdapterId;
    internal uint Id;
    internal uint ModeInfoIdx;
    internal uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathTargetInfo
{
    internal Luid AdapterId;
    internal uint Id;
    internal uint ModeInfoIdx;
    internal DisplayConfigVideoOutputTechnology OutputTechnology;
    internal DisplayConfigRotation Rotation;
    internal DisplayConfigScaling Scaling;
    internal DisplayConfigRational RefreshRate;
    internal DisplayConfigScanLineOrdering ScanLineOrdering;
    [MarshalAs(UnmanagedType.Bool)] internal bool TargetAvailable;
    internal uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathInfo
{
    internal DisplayConfigPathSourceInfo SourceInfo;
    internal DisplayConfigPathTargetInfo TargetInfo;
    internal uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigDeviceInfoHeader
{
    internal DisplayConfigDeviceInfoType Type;
    internal uint Size;
    internal Luid AdapterId;
    internal uint Id;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigTargetDeviceName
{
    internal DisplayConfigDeviceInfoHeader Header;
    internal uint Flags;
    internal DisplayConfigVideoOutputTechnology OutputTechnology;
    internal ushort EdidManufactureId;
    internal ushort EdidProductCodeId;
    internal uint ConnectorInstance;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string MonitorFriendlyDeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string MonitorDevicePath;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigTargetPreferredMode
{
    internal DisplayConfigDeviceInfoHeader Header;
    internal uint Width;
    internal uint Height;
    internal DisplayConfigTargetMode TargetMode;
}

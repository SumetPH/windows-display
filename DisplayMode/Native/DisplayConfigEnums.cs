namespace DisplayMode.Native;

internal enum DisplayConfigModeInfoType : uint
{
    Source = 1,
    Target = 2,
    DesktopImage = 3
}

internal enum DisplayConfigPixelFormat : uint
{
    PixelFormat8Bpp = 1,
    PixelFormat16Bpp = 2,
    PixelFormat24Bpp = 3,
    PixelFormat32Bpp = 4,
    PixelFormatNongdi = 5
}

internal enum DisplayConfigRotation : uint
{
    Identity = 1,
    Rotate90 = 2,
    Rotate180 = 3,
    Rotate270 = 4
}

internal enum DisplayConfigScaling : uint
{
    Identity = 1,
    Centered = 2,
    Stretched = 3,
    AspectRatioCenteredMax = 4,
    Custom = 5,
    Preferred = 128
}

internal enum DisplayConfigScanLineOrdering : uint
{
    Unspecified = 0,
    Progressive = 1,
    Interlaced = 2,
    InterlacedUpperFieldFirst = 2,
    InterlacedLowerFieldFirst = 3
}

internal enum DisplayConfigVideoOutputTechnology : uint
{
    Other = 0xFFFFFFFF,
    Hd15 = 0,
    Svideo = 1,
    CompositeVideo = 2,
    ComponentVideo = 3,
    Dvi = 4,
    Hdmi = 5,
    Lvds = 6,
    Djpn = 8,
    Sdi = 9,
    DisplayPortExternal = 10,
    DisplayPortEmbedded = 11,
    UdiExternal = 12,
    UdiEmbedded = 13,
    SdtvDongle = 14,
    Miracast = 15,
    IndirectWired = 16,
    IndirectVirtual = 17,
    Internal = 0x80000000
}

internal enum DisplayConfigDeviceInfoType : uint
{
    GetSourceName = 1,
    GetTargetName = 2,
    GetTargetPreferredMode = 3,
    GetAdapterName = 4
}

[Flags]
internal enum QueryDisplayConfigFlags : uint
{
    AllPaths = 0x1,
    OnlyActivePaths = 0x2,
    DatabaseCurrent = 0x4
}

[Flags]
internal enum SetDisplayConfigFlags : uint
{
    UseSuppliedDisplayConfig = 0x20,
    Validate = 0x40,
    Apply = 0x80,
    SaveToDatabase = 0x200,
    AllowChanges = 0x400
}

internal static class DisplayConfigConstants
{
    internal const uint PathActive = 0x1;
    internal const uint ModeIndexInvalid = 0xFFFFFFFF;
    internal const int ErrorSuccess = 0;
    internal const int ErrorInsufficientBuffer = 122;
}

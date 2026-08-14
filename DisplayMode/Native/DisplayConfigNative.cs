using System.Runtime.InteropServices;

namespace DisplayMode.Native;

internal static class DisplayConfigNative
{
    [DllImport("user32.dll")]
    internal static extern int GetDisplayConfigBufferSizes(
        QueryDisplayConfigFlags flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    internal static extern int QueryDisplayConfig(
        QueryDisplayConfigFlags flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        nint currentTopologyId);

    [DllImport("user32.dll")]
    internal static extern int SetDisplayConfig(
        uint numPathArrayElements,
        [In] DisplayConfigPathInfo[] pathArray,
        uint numModeInfoArrayElements,
        [In] DisplayConfigModeInfo[]? modeInfoArray,
        SetDisplayConfigFlags flags);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    internal static extern int DisplayConfigGetPreferredMode(ref DisplayConfigTargetPreferredMode requestPacket);
}

using System.Runtime.InteropServices;
using DisplayMode.Models;

namespace DisplayMode.Displays;

internal static class DesktopWallpaperManager
{
    private static readonly Guid DesktopWallpaperClassId = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

    internal static bool Apply(IReadOnlyList<MonitorProfile> profiles,
        IReadOnlyDictionary<string, MonitorInfo> monitors)
    {
        var requests = profiles.Where(profile => profile.WallpaperPath is not null).ToArray();
        if (requests.Length == 0)
            return true;

        Console.WriteLine("Applying wallpapers...");
        IDesktopWallpaper? desktopWallpaper = null;
        var failed = false;
        try
        {
            var type = Type.GetTypeFromCLSID(DesktopWallpaperClassId, throwOnError: true)
                       ?? throw new InvalidOperationException("Windows Desktop Wallpaper service is unavailable.");
            desktopWallpaper = (IDesktopWallpaper)(Activator.CreateInstance(type)
                               ?? throw new InvalidOperationException("Windows Desktop Wallpaper service could not be started."));

            var monitorIds = GetMonitorIds(desktopWallpaper);
            foreach (var profile in requests)
            {
                if (!monitors.TryGetValue(profile.HardwareId, out var monitor))
                {
                    Console.Error.WriteLine($"Wallpaper failed for {profile.Alias}: monitor information is unavailable.");
                    failed = true;
                    continue;
                }

                var monitorId = ResolveMonitorId(monitorIds, monitor);
                if (monitorId is null)
                {
                    Console.Error.WriteLine($"Wallpaper failed for {profile.Alias}: Windows did not expose a matching desktop monitor ID.");
                    failed = true;
                    continue;
                }

                var result = desktopWallpaper.SetWallpaper(monitorId, profile.WallpaperPath!);
                if (result < 0)
                {
                    Console.Error.WriteLine($"Wallpaper failed for {profile.Alias}: {FormatHResult(result)}");
                    failed = true;
                }
                else
                {
                    Console.WriteLine($"  {profile.Alias}: {profile.WallpaperPath}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Wallpaper operation failed: {ex.Message}");
            failed = true;
        }
        finally
        {
            if (desktopWallpaper is not null && Marshal.IsComObject(desktopWallpaper))
                Marshal.FinalReleaseComObject(desktopWallpaper);
        }

        if (failed)
            Console.Error.WriteLine("The display topology was applied, but one or more wallpapers could not be changed.");
        return !failed;
    }

    private static string[] GetMonitorIds(IDesktopWallpaper desktopWallpaper)
    {
        ThrowIfFailed(desktopWallpaper.GetMonitorDevicePathCount(out var count));
        var result = new List<string>(checked((int)count));
        for (uint index = 0; index < count; index++)
        {
            ThrowIfFailed(desktopWallpaper.GetMonitorDevicePathAt(index, out var pointer));
            try
            {
                var value = Marshal.PtrToStringUni(pointer);
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pointer);
            }
        }
        return result.ToArray();
    }

    private static string? ResolveMonitorId(IEnumerable<string> monitorIds, MonitorInfo monitor)
    {
        var exact = monitorIds.FirstOrDefault(id =>
            id.Equals(monitor.DevicePath, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var fallback = monitorIds.Where(id =>
            id.Contains(monitor.HardwareId ?? string.Empty, StringComparison.OrdinalIgnoreCase)).ToArray();
        return fallback.Length == 1 ? fallback[0] : null;
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);
    }

    private static string FormatHResult(int result) =>
        $"0x{unchecked((uint)result):X8} ({Marshal.GetExceptionForHR(result)?.Message ?? "unknown error"})";

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId,
            [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, out IntPtr wallpaper);
        [PreserveSig] int GetMonitorDevicePathAt(uint monitorIndex, out IntPtr monitorId);
        [PreserveSig] int GetMonitorDevicePathCount(out uint count);
        [PreserveSig] int GetMonitorRect([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out NativeRect displayRect);
        [PreserveSig] int SetBackgroundColor(uint color);
        [PreserveSig] int GetBackgroundColor(out uint color);
        [PreserveSig] int SetPosition(uint position);
        [PreserveSig] int GetPosition(out uint position);
        [PreserveSig] int SetSlideshow(IntPtr items);
        [PreserveSig] int GetSlideshow(out IntPtr items);
        [PreserveSig] int SetSlideshowOptions(uint options, uint slideshowTick);
        [PreserveSig] int GetSlideshowOptions(out uint options, out uint slideshowTick);
        [PreserveSig] int AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, uint direction);
        [PreserveSig] int GetStatus(out uint state);
        [PreserveSig] int Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

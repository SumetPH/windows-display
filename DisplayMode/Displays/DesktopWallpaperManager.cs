using System.Runtime.InteropServices;
using DisplayMode.Models;

namespace DisplayMode.Displays;

internal static class DesktopWallpaperManager
{
    private static readonly Guid DesktopWallpaperClassId = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".jpeg", ".jpg", ".png"
    };

    internal static bool HasSupportedImages(string folder) => EnumerateImages(folder).Any();

    internal static bool Apply(IReadOnlyList<MonitorProfile> profiles,
        IReadOnlyDictionary<string, MonitorInfo> monitors)
    {
        var requests = profiles.Where(profile =>
            profile.WallpaperPath is not null || profile.WallpaperFolder is not null).ToArray();
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

                var wallpaperPath = profile.WallpaperPath ?? SelectSlideshowImage(desktopWallpaper, monitorId, profile);
                var result = desktopWallpaper.SetWallpaper(monitorId, wallpaperPath);
                if (result < 0)
                {
                    Console.Error.WriteLine($"Wallpaper failed for {profile.Alias}: {FormatHResult(result)}");
                    failed = true;
                }
                else
                {
                    Console.WriteLine($"  {profile.Alias}: {wallpaperPath}");
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

    internal static bool AdvanceSlideshow(MonitorProfile profile,
        IReadOnlyDictionary<string, MonitorInfo> monitors)
    {
        if (profile.WallpaperFolder is null)
        {
            Console.Error.WriteLine($"ERROR: Monitor '{profile.Alias}' does not have a wallpaperFolder.");
            return false;
        }
        if (!monitors.TryGetValue(profile.HardwareId, out var monitor))
        {
            Console.Error.WriteLine($"ERROR: Monitor information is unavailable for '{profile.Alias}'.");
            return false;
        }

        IDesktopWallpaper? desktopWallpaper = null;
        try
        {
            var type = Type.GetTypeFromCLSID(DesktopWallpaperClassId, throwOnError: true)
                       ?? throw new InvalidOperationException("Windows Desktop Wallpaper service is unavailable.");
            desktopWallpaper = (IDesktopWallpaper)(Activator.CreateInstance(type)
                               ?? throw new InvalidOperationException("Windows Desktop Wallpaper service could not be started."));
            var monitorId = ResolveMonitorId(GetMonitorIds(desktopWallpaper), monitor)
                            ?? throw new InvalidOperationException(
                                $"Windows did not expose a matching desktop monitor ID for '{profile.Alias}'.");
            var wallpaperPath = SelectSlideshowImage(desktopWallpaper, monitorId, profile);
            ThrowIfFailed(desktopWallpaper.SetWallpaper(monitorId, wallpaperPath));
            Console.WriteLine($"{profile.Alias}: {wallpaperPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: Slideshow failed for {profile.Alias}: {ex.Message}");
            return false;
        }
        finally
        {
            if (desktopWallpaper is not null && Marshal.IsComObject(desktopWallpaper))
                Marshal.FinalReleaseComObject(desktopWallpaper);
        }
    }

    private static string SelectSlideshowImage(IDesktopWallpaper desktopWallpaper, string monitorId,
        MonitorProfile profile)
    {
        var images = EnumerateImages(profile.WallpaperFolder!).ToArray();
        if (images.Length == 0)
            throw new InvalidOperationException($"No supported images remain in {profile.WallpaperFolder}.");

        var current = GetCurrentWallpaper(desktopWallpaper, monitorId);
        if (profile.SlideshowOrder == SlideshowOrder.Sequential)
        {
            var currentIndex = Array.FindIndex(images,
                path => path.Equals(current, StringComparison.OrdinalIgnoreCase));
            return images[(currentIndex + 1 + images.Length) % images.Length];
        }

        if (images.Length == 1)
            return images[0];
        var candidates = images.Where(path => !path.Equals(current, StringComparison.OrdinalIgnoreCase)).ToArray();
        return candidates[Random.Shared.Next(candidates.Length)];
    }

    private static IEnumerable<string> EnumerateImages(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static string? GetCurrentWallpaper(IDesktopWallpaper desktopWallpaper, string monitorId)
    {
        ThrowIfFailed(desktopWallpaper.GetWallpaper(monitorId, out var pointer));
        try
        {
            return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            if (pointer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pointer);
        }
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
        // Keep every method in native interface order. The unused declarations preserve the COM vtable layout.
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

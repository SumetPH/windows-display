namespace DisplayMode.Tray;

internal static class AppIcon
{
    internal static Icon Load() =>
        Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
}

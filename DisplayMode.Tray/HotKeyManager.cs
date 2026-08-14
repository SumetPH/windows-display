using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DisplayMode.Tray;

internal sealed class HotKeyManager : NativeWindow, IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private readonly Dictionary<int, Action> actions = [];
    private int nextId = 100;

    internal HotKeyManager() => CreateHandle(new CreateParams());

    internal void Clear()
    {
        foreach (var id in actions.Keys)
            UnregisterHotKey(Handle, id);
        actions.Clear();
    }

    internal void Register(string shortcut, Action action)
    {
        if (!TryParse(shortcut, out var modifiers, out var key, out var error))
            throw new InvalidDataException(error);
        var id = nextId++;
        if (!RegisterHotKey(Handle, id, modifiers | ModNoRepeat, key))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not register hotkey '{shortcut}'. It may be used by another application.");
        actions.Add(id, action);
    }

    internal static bool TryParse(string shortcut, out uint modifiers, out uint key, out string error)
    {
        modifiers = 0;
        key = 0;
        error = string.Empty;
        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win" or "windows": modifiers |= ModWin; break;
                default:
                    if (key != 0 || !TryVirtualKey(part, out key))
                    {
                        error = $"Invalid shortcut '{shortcut}'. Use forms such as Ctrl+Alt+3 or Ctrl+Shift+F1.";
                        return false;
                    }
                    break;
            }
        }
        if (modifiers == 0 || key == 0)
        {
            error = $"Shortcut '{shortcut}' needs at least one modifier and one key.";
            return false;
        }
        return true;
    }

    private static bool TryVirtualKey(string text, out uint key)
    {
        key = 0;
        if (text.Length == 1 && char.IsLetterOrDigit(text[0]))
        {
            key = char.ToUpperInvariant(text[0]);
            return true;
        }
        if (text.Length is 2 or 3 && text[0] is 'F' or 'f' && int.TryParse(text[1..], out var function) && function is >= 1 and <= 24)
        {
            key = (uint)(Keys.F1 + function - 1);
            return true;
        }
        return Enum.TryParse<Keys>(text, true, out var parsed) && (key = (uint)parsed) != 0;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotKey && actions.TryGetValue(m.WParam.ToInt32(), out var action))
            action();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Clear();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}

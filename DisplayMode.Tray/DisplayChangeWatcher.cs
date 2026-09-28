namespace DisplayMode.Tray;

/// <summary>
/// Raises <see cref="Settled"/> on the UI thread once Windows has stopped reporting display
/// changes for a quiet period. Hot-plugging a TV produces a burst of WM_DISPLAYCHANGE
/// messages while the link trains, so acting on the first one would race the device.
/// </summary>
internal sealed class DisplayChangeWatcher : NativeWindow, IDisposable
{
    private const int WmDisplayChange = 0x007E;
    private readonly System.Windows.Forms.Timer debounce;
    private long ignoreUntil;

    internal event Action? Settled;

    internal DisplayChangeWatcher(int quietMilliseconds)
    {
        // A hidden top-level window (not message-only) is required to receive the
        // WM_DISPLAYCHANGE broadcast.
        CreateHandle(new CreateParams());
        debounce = new System.Windows.Forms.Timer { Interval = quietMilliseconds };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            Settled?.Invoke();
        };
    }

    /// <summary>Drops any pending change and ignores new ones for the given period.</summary>
    internal void Suppress(int milliseconds)
    {
        debounce.Stop();
        ignoreUntil = Environment.TickCount64 + milliseconds;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmDisplayChange && Environment.TickCount64 >= ignoreUntil)
        {
            debounce.Stop();
            debounce.Start();
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        debounce.Dispose();
        DestroyHandle();
    }
}

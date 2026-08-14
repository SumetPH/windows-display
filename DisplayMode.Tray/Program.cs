namespace DisplayMode.Tray;

internal static class Program
{
    private const string MutexName = "Local\\DisplayMode.Tray.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("DisplayMode is already running.", "DisplayMode",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
        GC.KeepAlive(mutex);
    }
}

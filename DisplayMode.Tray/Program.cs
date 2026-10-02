namespace DisplayMode.Tray;

internal static class Program
{
    private const string MutexName = "Local\\DisplayMode.Tray.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("DisplayMode is already running.", "DisplayMode",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        var launchedAtStartup = args.Contains(TrayApplicationContext.StartupArgument, StringComparer.OrdinalIgnoreCase);
        Application.Run(new TrayApplicationContext(launchedAtStartup));
        GC.KeepAlive(mutex);
    }
}

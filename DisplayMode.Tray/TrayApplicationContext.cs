using System.Diagnostics;
using Microsoft.Win32;

namespace DisplayMode.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValue = "DisplayMode";
    private const string LegacyStartupValue = "DisplayMode.Tray";
    private readonly NotifyIcon notifyIcon;
    private readonly Icon appIcon = AppIcon.Load();
    private readonly HotKeyManager hotKeys = new();
    private readonly SemaphoreSlim switchLock = new(1, 1);
    private readonly SlideshowScheduler slideshowScheduler;
    private TraySettings settings = TraySettings.Load();
    private int slideshowActivationGeneration;
    private IReadOnlyList<string> profiles = [];
    private IReadOnlyDictionary<string, IReadOnlyList<SlideshowBinding>> slideshows =
        new Dictionary<string, IReadOnlyList<SlideshowBinding>>(StringComparer.OrdinalIgnoreCase);

    internal TrayApplicationContext()
    {
        notifyIcon = new NotifyIcon
        {
            Icon = appIcon,
            Text = "DisplayMode",
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => ShowSettings();
        slideshowScheduler = new SlideshowScheduler(
            (profile, monitor) => DisplayModeClient.AdvanceSlideshowAsync(settings, profile, monitor),
            message => Notify(message, ToolTipIcon.Error));
        Reload(showSuccess: false);
        _ = ResumeSlideshowAsync();
    }

    private void Reload(bool showSuccess)
    {
        slideshowActivationGeneration++;
        try
        {
            profiles = DisplayModeClient.LoadProfiles(settings.ConfigPath);
            slideshows = DisplayModeClient.LoadSlideshows(settings.ConfigPath);
            slideshowScheduler.Stop();
            try
            {
                RegisterHotKeys();
            }
            catch (Exception ex)
            {
                hotKeys.Clear();
                Notify($"Hotkey error: {ex.Message}", ToolTipIcon.Error);
            }
            BuildMenu();
            if (showSuccess)
                Notify("Configuration reloaded", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            profiles = [];
            slideshows = new Dictionary<string, IReadOnlyList<SlideshowBinding>>(StringComparer.OrdinalIgnoreCase);
            slideshowScheduler.Stop();
            hotKeys.Clear();
            BuildMenu();
            Notify($"Configuration error: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private void RegisterHotKeys()
    {
        hotKeys.Clear();
        foreach (var binding in settings.HotKeys)
        {
            var profile = profiles.FirstOrDefault(profile =>
                profile.Equals(binding.Profile, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
                throw new InvalidDataException($"Hotkey profile '{binding.Profile}' does not exist.");
            hotKeys.Register(binding.Shortcut, () => _ = ApplyProfileAsync(profile));
        }
    }

    private void BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var heading = new ToolStripMenuItem("Display profiles") { Enabled = false };
        menu.Items.Add(heading);
        foreach (var profile in profiles)
        {
            var shortcut = settings.HotKeys.FirstOrDefault(h => h.Profile.Equals(profile, StringComparison.OrdinalIgnoreCase))?.Shortcut;
            var item = new ToolStripMenuItem(profile) { ShortcutKeyDisplayString = shortcut ?? string.Empty };
            item.Click += (_, _) => _ = ApplyProfileAsync(profile);
            menu.Items.Add(item);
        }
        if (profiles.Count == 0)
            menu.Items.Add(new ToolStripMenuItem("(no profiles loaded)") { Enabled = false });

        menu.Items.Add(new ToolStripSeparator());
        var startup = new ToolStripMenuItem("Run at startup") { Checked = IsStartupEnabled(), CheckOnClick = true };
        startup.Click += (_, _) =>
        {
            try { SetStartup(startup.Checked); }
            catch (Exception ex)
            {
                startup.Checked = !startup.Checked;
                Notify($"Could not update startup: {ex.Message}", ToolTipIcon.Error);
            }
        };
        menu.Items.Add(startup);
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        menu.Items.Add("Open profiles.json", null, (_, _) => OpenConfig());
        menu.Items.Add("Reload configuration", null, (_, _) => Reload(showSuccess: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        var previous = notifyIcon.ContextMenuStrip;
        notifyIcon.ContextMenuStrip = menu;
        previous?.Dispose();
    }

    private async Task ApplyProfileAsync(string profile)
    {
        if (!await switchLock.WaitAsync(0))
        {
            Notify("A display switch is already running.", ToolTipIcon.Warning);
            return;
        }
        try
        {
            slideshowActivationGeneration++;
            slideshowScheduler.Stop();
            notifyIcon.Text = $"DisplayMode - applying {profile}";
            Notify($"Switching to profile '{profile}'...", ToolTipIcon.Info);
            var result = await DisplayModeClient.ApplyAsync(settings, profile);
            if (result.ExitCode == 0)
            {
                if (slideshows.TryGetValue(profile, out var bindings))
                    slideshowScheduler.Activate(profile, bindings);
                // With no stock status glyph, Windows uses the application's
                // tray icon for a successful display-mode notification.
                Notify($"Profile '{profile}' applied successfully.", ToolTipIcon.None);
            }
            else
            {
                Notify(FormatApplyError(profile, result), ToolTipIcon.Error);
            }
        }
        catch (Exception ex)
        {
            Notify($"Profile '{profile}' failed: {ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            notifyIcon.Text = "DisplayMode";
            switchLock.Release();
        }
    }

    private async Task ResumeSlideshowAsync()
    {
        var generation = slideshowActivationGeneration;
        var result = await DisplayModeClient.GetActiveProfileAsync(settings);
        if (generation != slideshowActivationGeneration || result.ExitCode != 0)
            return;

        var profile = result.Output.Trim();
        if (profiles.Contains(profile, StringComparer.OrdinalIgnoreCase) &&
            slideshows.TryGetValue(profile, out var bindings))
            slideshowScheduler.Activate(profile, bindings);
    }

    private void ShowSettings()
    {
        try
        {
            using var form = new SettingsForm(settings, profiles, IsStartupEnabled());
            if (form.ShowDialog() != DialogResult.OK)
                return;
            settings = form.ResultSettings;
            settings.Save();
            SetStartup(form.StartupEnabled);
            Reload(showSuccess: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "DisplayMode settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenConfig()
    {
        try
        {
            Process.Start(new ProcessStartInfo(settings.ConfigPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notify($"Could not open config: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKey);
        return key?.GetValue(StartupValue) is string || key?.GetValue(LegacyStartupValue) is string;
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupKey);
        if (enabled)
        {
            key.SetValue(StartupValue, $"\"{Application.ExecutablePath}\"");
            key.DeleteValue(LegacyStartupValue, throwOnMissingValue: false);
        }
        else
        {
            key.DeleteValue(StartupValue, throwOnMissingValue: false);
            key.DeleteValue(LegacyStartupValue, throwOnMissingValue: false);
        }
    }

    private void Notify(string message, ToolTipIcon icon)
    {
        notifyIcon.BalloonTipTitle = "DisplayMode";
        notifyIcon.BalloonTipText = message.Length > 250 ? message[..250] : message;
        notifyIcon.BalloonTipIcon = icon;
        notifyIcon.ShowBalloonTip(3500);
    }

    private static string FormatApplyError(string profile, CommandResult result)
    {
        var lines = result.Output.Split(['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var detail = lines
            .FirstOrDefault(line => line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault(line => line.Contains("failed", StringComparison.OrdinalIgnoreCase))
            ?? lines.LastOrDefault();
        var displayWasApplied = lines.Any(line => line.Equals("Success.", StringComparison.OrdinalIgnoreCase));
        var fallback = displayWasApplied ? string.Empty : " Current display mode was kept.";
        detail = detail?.TrimEnd().TrimEnd('.');

        return string.IsNullOrWhiteSpace(detail)
            ? $"Profile '{profile}' failed (exit {result.ExitCode}).{fallback}"
            : $"Profile '{profile}' failed: {detail}.{fallback}";
    }

    protected override void ExitThreadCore()
    {
        hotKeys.Dispose();
        slideshowScheduler.Dispose();
        switchLock.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        appIcon.Dispose();
        base.ExitThreadCore();
    }
}

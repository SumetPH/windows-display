using System.Diagnostics;
using Microsoft.Win32;

namespace DisplayMode.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValue = "DisplayMode";
    private const string LegacyStartupValue = "DisplayMode.Tray";
    internal const string StartupArgument = "--startup";
    private const int DisplayChangeQuietMilliseconds = 4_000;
    private const int OwnApplySuppressMilliseconds = 3_000;
    private readonly NotifyIcon notifyIcon;
    private readonly Icon appIcon = AppIcon.Load();
    private readonly HotKeyManager hotKeys = new();
    private readonly DisplayChangeWatcher displayWatcher = new(DisplayChangeQuietMilliseconds);
    private readonly SemaphoreSlim switchLock = new(1, 1);
    private readonly SlideshowScheduler slideshowScheduler;
    private TraySettings settings = TraySettings.Load();
    private int slideshowActivationGeneration;
    private IReadOnlyList<string> profiles = [];
    private IReadOnlyDictionary<string, IReadOnlyList<SlideshowBinding>> slideshows =
        new Dictionary<string, IReadOnlyList<SlideshowBinding>>(StringComparer.OrdinalIgnoreCase);

    internal TrayApplicationContext(bool launchedAtStartup)
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
        displayWatcher.Settled += () => _ = ReapplyAfterDisplayChangeAsync();
        Reload(showSuccess: false);
        MigrateStartupCommand();
        if (launchedAtStartup && DefaultProfile() is { } defaultProfile)
            _ = ApplyProfileAsync(defaultProfile, rollbackOnFailure: true);
        else
            _ = ResumeSlideshowAsync();
    }

    private string? DefaultProfile() => string.IsNullOrWhiteSpace(settings.DefaultProfile)
        ? null
        : profiles.FirstOrDefault(p => p.Equals(settings.DefaultProfile, StringComparison.OrdinalIgnoreCase));

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
        menu.Items.Add(BuildDefaultProfileMenu());
        menu.Items.Add(BuildReapplyMenu());
        var startup =new ToolStripMenuItem("Run at startup") { Checked = IsStartupEnabled(), CheckOnClick = true };
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

    private ToolStripMenuItem BuildDefaultProfileMenu()
    {
        var parent = new ToolStripMenuItem("Default profile at startup");
        var current = DefaultProfile();
        foreach (var profile in new string?[] { null }.Concat(profiles))
        {
            var item = new ToolStripMenuItem(profile ?? "(None)") { Checked = profile == current };
            item.Click += (_, _) =>
            {
                settings.DefaultProfile = profile;
                foreach (ToolStripMenuItem sibling in parent.DropDownItems)
                    sibling.Checked = sibling == item;
                try { settings.Save(); }
                catch (Exception ex) { Notify($"Could not save settings: {ex.Message}", ToolTipIcon.Error); }
            };
            parent.DropDownItems.Add(item);
        }
        parent.Enabled = profiles.Count > 0;
        return parent;
    }

    private ToolStripMenuItem BuildReapplyMenu()
    {
        var parent = new ToolStripMenuItem("Re-apply after display change");
        foreach (var profile in profiles)
        {
            var item = new ToolStripMenuItem(profile)
            {
                CheckOnClick = true,
                Checked = settings.ReapplyOnDisplayChange.Contains(profile, StringComparer.OrdinalIgnoreCase)
            };
            item.Click += (_, _) =>
            {
                settings.ReapplyOnDisplayChange.RemoveAll(p => p.Equals(profile, StringComparison.OrdinalIgnoreCase));
                if (item.Checked)
                    settings.ReapplyOnDisplayChange.Add(profile);
                try { settings.Save(); }
                catch (Exception ex) { Notify($"Could not save settings: {ex.Message}", ToolTipIcon.Error); }
            };
            parent.DropDownItems.Add(item);
        }
        parent.Enabled = profiles.Count > 0;
        return parent;
    }

    private async Task ReapplyAfterDisplayChangeAsync()
    {
        if (settings.ReapplyOnDisplayChange.Count == 0 || switchLock.CurrentCount == 0)
            return;

        CommandResult result;
        try { result = await DisplayModeClient.GetActiveProfileAsync(settings); }
        catch { return; }
        if (result.ExitCode != 0)
            return;
        var active = result.Output.Trim();
        var profile = profiles.FirstOrDefault(p => p.Equals(active, StringComparison.OrdinalIgnoreCase));
        if (profile is null ||
            !settings.ReapplyOnDisplayChange.Contains(profile, StringComparer.OrdinalIgnoreCase))
            return;

        // Windows restores a saved topology the moment a TV hot-plugs, often before the TV
        // can show a picture. Applying the same profile again once the link has settled is
        // what a manual re-select does, and it is harmless when the picture is already fine.
        await ApplyProfileAsync(profile);
    }

    private async Task ApplyProfileAsync(string profile, bool rollbackOnFailure = false)
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
            var result = await DisplayModeClient.ApplyAsync(settings, profile, rollbackOnFailure);
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
                if (rollbackOnFailure)
                    _ = ResumeSlideshowAsync();
            }
        }
        catch (Exception ex)
        {
            Notify($"Profile '{profile}' failed: {ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            // Our own SetDisplayConfig raises WM_DISPLAYCHANGE too; reacting to it would
            // re-apply in a loop.
            displayWatcher.Suppress(OwnApplySuppressMilliseconds);
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

    private static string StartupCommand => $"\"{Application.ExecutablePath}\" {StartupArgument}";

    // Entries written by older versions start the tray without the startup flag, so the
    // default profile would never be applied at sign-in.
    private void MigrateStartupCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupKey);
            var command = key?.GetValue(StartupValue) as string ?? key?.GetValue(LegacyStartupValue) as string;
            if (command is not null && command != StartupCommand)
                SetStartup(true);
        }
        catch (Exception ex)
        {
            Notify($"Could not update startup: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupKey);
        if (enabled)
        {
            key.SetValue(StartupValue, StartupCommand);
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
        var rolledBack = lines.Any(line => line.StartsWith("Rolled back", StringComparison.OrdinalIgnoreCase));
        var fallback = rolledBack ? " Previous display mode was restored."
            : displayWasApplied ? string.Empty
            : " Current display mode was kept.";
        detail = detail?.TrimEnd().TrimEnd('.');

        return string.IsNullOrWhiteSpace(detail)
            ? $"Profile '{profile}' failed (exit {result.ExitCode}).{fallback}"
            : $"Profile '{profile}' failed: {detail}.{fallback}";
    }

    protected override void ExitThreadCore()
    {
        hotKeys.Dispose();
        displayWatcher.Dispose();
        slideshowScheduler.Dispose();
        switchLock.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        appIcon.Dispose();
        base.ExitThreadCore();
    }
}

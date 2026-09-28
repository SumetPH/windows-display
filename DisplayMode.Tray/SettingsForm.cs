namespace DisplayMode.Tray;

internal sealed class SettingsForm : Form
{
    private readonly TextBox executablePath = new() { Dock = DockStyle.Fill };
    private readonly TextBox configPath = new() { Dock = DockStyle.Fill };
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = true,
        AllowUserToDeleteRows = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false
    };
    private readonly CheckBox startup = new() { Text = "Run DisplayMode when I sign in", AutoSize = true };
    private readonly IReadOnlyList<string> profiles;

    internal TraySettings ResultSettings { get; private set; }
    internal bool StartupEnabled => startup.Checked;

    internal SettingsForm(TraySettings settings, IReadOnlyList<string> profiles, bool startupEnabled)
    {
        this.profiles = profiles;
        ResultSettings = settings;
        Text = "DisplayMode Settings";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(680, 460);
        Size = new Size(760, 520);
        Icon = AppIcon.Load();

        executablePath.Text = settings.DisplayModePath;
        configPath.Text = settings.ConfigPath;
        startup.Checked = startupEnabled;

        var profileColumn = new DataGridViewComboBoxColumn { HeaderText = "Profile", FlatStyle = FlatStyle.Flat };
        var profileItems = profiles.Concat(settings.HotKeys.Select(binding =>
                profiles.FirstOrDefault(profile =>
                    profile.Equals(binding.Profile, StringComparison.OrdinalIgnoreCase)) ?? binding.Profile))
            .Distinct(StringComparer.Ordinal)
            .Cast<object>()
            .ToArray();
        profileColumn.Items.AddRange(profileItems);
        grid.Columns.Add(profileColumn);
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Global shortcut (example: Ctrl+Alt+3)" });
        foreach (var binding in settings.HotKeys)
        {
            var configuredProfile = profiles.FirstOrDefault(profile =>
                profile.Equals(binding.Profile, StringComparison.OrdinalIgnoreCase));
            grid.Rows.Add(configuredProfile ?? binding.Profile, binding.Shortcut);
        }
        grid.DataError += (_, eventArgs) =>
        {
            if (eventArgs.Exception is ArgumentException)
                eventArgs.ThrowException = false;
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 3,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "DisplayMode.exe", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(executablePath, 1, 0);
        layout.Controls.Add(BrowseButton(executablePath, "Executable (*.exe)|*.exe"), 2, 0);
        layout.Controls.Add(new Label { Text = "profiles.json", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(configPath, 1, 1);
        layout.Controls.Add(BrowseButton(configPath, "JSON (*.json)|*.json|All files (*.*)|*.*"), 2, 1);
        layout.Controls.Add(new Label { Text = "Hotkeys", AutoSize = true, Padding = new Padding(0, 10, 0, 4) }, 0, 2);
        layout.SetColumnSpan(grid, 3);
        layout.Controls.Add(grid, 0, 3);
        layout.SetColumnSpan(startup, 3);
        layout.Controls.Add(startup, 0, 4);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.None, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => SaveAndClose();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.SetColumnSpan(buttons, 3);
        layout.Controls.Add(buttons, 0, 5);
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private static Button BrowseButton(TextBox target, string filter)
    {
        var button = new Button { Text = "Browse...", AutoSize = true };
        button.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = filter, FileName = target.Text };
            if (dialog.ShowDialog() == DialogResult.OK)
                target.Text = dialog.FileName;
        };
        return button;
    }

    private void SaveAndClose()
    {
        try
        {
            if (!File.Exists(executablePath.Text))
                throw new InvalidDataException("DisplayMode.exe path does not exist.");
            if (!File.Exists(configPath.Text))
                throw new InvalidDataException("profiles.json path does not exist.");

            var bindings = new List<HotKeyBinding>();
            var shortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                var profile = row.Cells[0].Value?.ToString()?.Trim() ?? string.Empty;
                var shortcut = row.Cells[1].Value?.ToString()?.Trim() ?? string.Empty;
                if (profile.Length == 0 && shortcut.Length == 0) continue;
                if (!profiles.Contains(profile, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unknown profile '{profile}'.");
                if (!HotKeyManager.TryParse(shortcut, out _, out _, out var error))
                    throw new InvalidDataException(error);
                if (!shortcuts.Add(shortcut))
                    throw new InvalidDataException($"Shortcut '{shortcut}' is assigned more than once.");
                var configuredProfile = profiles.First(configured =>
                    configured.Equals(profile, StringComparison.OrdinalIgnoreCase));
                bindings.Add(new HotKeyBinding { Profile = configuredProfile, Shortcut = shortcut });
            }

            ResultSettings = new TraySettings
            {
                DisplayModePath = Path.GetFullPath(executablePath.Text),
                ConfigPath = Path.GetFullPath(configPath.Text),
                HotKeys = bindings,
                ReapplyOnDisplayChange = ResultSettings.ReapplyOnDisplayChange
            };
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Invalid settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

using System.Diagnostics;
using System.Text.Json;

namespace DisplayMode.Tray;

internal static class DisplayModeClient
{
    internal static IReadOnlyList<string> LoadProfiles(string configPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(configPath), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        if (!document.RootElement.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("profiles.json does not contain a profiles object.");
        // JsonElement preserves object-property order, so the tray menu follows
        // the order chosen by the user in profiles.json.
        return profiles.EnumerateObject().Select(p => p.Name).ToArray();
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<SlideshowBinding>> LoadSlideshows(string configPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(configPath), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        var result = new Dictionary<string, IReadOnlyList<SlideshowBinding>>(StringComparer.OrdinalIgnoreCase);
        if (!document.RootElement.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var profile in profiles.EnumerateObject())
        {
            var bindings = new List<SlideshowBinding>();
            if (profile.Value.TryGetProperty("monitors", out var monitors) && monitors.ValueKind == JsonValueKind.Array)
            {
                foreach (var monitor in monitors.EnumerateArray())
                {
                    if (!monitor.TryGetProperty("wallpaperFolder", out var folder) ||
                        folder.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(folder.GetString()) ||
                        !monitor.TryGetProperty("monitor", out var alias) || alias.ValueKind != JsonValueKind.String)
                        continue;
                    var interval = monitor.TryGetProperty("slideshowInterval", out var intervalElement) &&
                                   intervalElement.TryGetInt32(out var configuredInterval)
                        ? configuredInterval
                        : 300;
                    bindings.Add(new SlideshowBinding(alias.GetString()!, interval));
                }
            }
            result[profile.Name] = bindings;
        }
        return result;
    }

    internal static async Task<CommandResult> ApplyAsync(TraySettings settings, string profile,
        bool rollbackOnFailure = false)
    {
        return await RunAsync(settings, rollbackOnFailure
            ? ["apply", profile, "--rollback-on-failure"]
            : ["apply", profile]);
    }

    internal static async Task<CommandResult> AdvanceSlideshowAsync(TraySettings settings, string profile,
        string monitorAlias)
    {
        return await RunAsync(settings, ["slideshow-next", profile, monitorAlias]);
    }

    internal static async Task<CommandResult> GetActiveProfileAsync(TraySettings settings)
    {
        return await RunAsync(settings, ["active-profile"]);
    }

    private static async Task<CommandResult> RunAsync(TraySettings settings, IReadOnlyList<string> arguments)
    {
        if (!File.Exists(settings.DisplayModePath))
            return new CommandResult(-1, $"DisplayMode.exe was not found: {settings.DisplayModePath}");
        if (!File.Exists(settings.ConfigPath))
            return new CommandResult(-1, $"profiles.json was not found: {settings.ConfigPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = settings.DisplayModePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(settings.ConfigPath);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = string.Join(Environment.NewLine,
            new[] { await stdout, await stderr }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        return new CommandResult(process.ExitCode, output);
    }
}

internal sealed record CommandResult(int ExitCode, string Output);
internal sealed record SlideshowBinding(string MonitorAlias, int IntervalSeconds);

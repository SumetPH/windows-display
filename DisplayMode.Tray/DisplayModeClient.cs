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

    internal static async Task<CommandResult> ApplyAsync(TraySettings settings, string profile)
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
        startInfo.ArgumentList.Add("apply");
        startInfo.ArgumentList.Add(profile);
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

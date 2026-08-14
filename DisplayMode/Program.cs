using System.ComponentModel;
using DisplayMode.Displays;
using DisplayMode.Models;

namespace DisplayMode;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var parsed = ParseArguments(args);
            if (parsed is null)
                return 2;

            var configuration = ConfigurationLoader.Load(parsed.ConfigPath);
            if (parsed.Command.Equals("config-path", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(configuration.Path);
                return 0;
            }
            if (parsed.Command.Equals("config-check", StringComparison.OrdinalIgnoreCase))
            {
                PrintConfiguration(configuration);
                Console.WriteLine("Configuration is valid.");
                return 0;
            }
            if (parsed.Command.Equals("profiles", StringComparison.OrdinalIgnoreCase))
            {
                PrintConfiguration(configuration);
                return 0;
            }

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                throw new PlatformNotSupportedException("DisplayMode requires Windows 11 (build 22000 or later).");

            DisplayTopologyManager.ValidateInteropLayout();
            var manager = new DisplayTopologyManager(parsed.Verbose, configuration);
            if (parsed.Verbose)
                DisplayTopologyManager.PrintInteropSizes();

            var snapshot = manager.Discover();
            if (parsed.Command.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                manager.PrintMonitors(snapshot, activeOnly: false);
                return 0;
            }
            if (parsed.Command.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                manager.PrintMonitors(snapshot, activeOnly: true);
                return 0;
            }
            if (parsed.Command.Equals("capture", StringComparison.OrdinalIgnoreCase))
                return manager.CaptureTimings(snapshot);

            var profileName = parsed.Command.Equals("apply", StringComparison.OrdinalIgnoreCase)
                ? parsed.ProfileName!
                : parsed.Command;
            if (!configuration.Profiles.TryGetValue(profileName, out var profile))
            {
                Console.Error.WriteLine($"Unknown profile: {profileName}");
                Console.Error.WriteLine("Run 'DisplayMode.exe profiles' to list configured profiles.");
                return 2;
            }

            if (!manager.TryResolveKnownMonitors(snapshot, out var monitors))
                return 1;
            if (!parsed.DryRun && !parsed.ValidateOnly)
                manager.CacheObservedTimings(snapshot);
            var proposal = manager.Build(profile, snapshot, monitors);
            return manager.ValidateAndApply(profile.Name, proposal, parsed.DryRun, parsed.ValidateOnly);
        }
        catch (Win32Exception ex)
        {
            Console.Error.WriteLine($"Windows display API error: {ex.Message}");
            Console.Error.WriteLine("No further display operation was attempted.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            Console.Error.WriteLine("No further display operation was attempted.");
            return 1;
        }
    }

    private static ParsedArguments? ParseArguments(string[] args)
    {
        var positional = new List<string>();
        var verbose = false;
        var dryRun = false;
        var validateOnly = false;
        var configPath = ConfigurationLoader.DefaultPath;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--verbose": verbose = true; break;
                case "--dry-run": dryRun = true; break;
                case "--validate-only": validateOnly = true; break;
                case "--config":
                    if (++i >= args.Length)
                    {
                        Console.Error.WriteLine("--config requires a file path.");
                        PrintUsage();
                        return null;
                    }
                    configPath = args[i];
                    break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"Unknown option: {args[i]}");
                        PrintUsage();
                        return null;
                    }
                    positional.Add(args[i]);
                    break;
            }
        }

        if (dryRun && validateOnly || positional.Count is < 1 or > 2 ||
            (positional[0].Equals("apply", StringComparison.OrdinalIgnoreCase) && positional.Count != 2) ||
            (positional.Count == 2 && !positional[0].Equals("apply", StringComparison.OrdinalIgnoreCase)))
        {
            PrintUsage();
            return null;
        }

        var command = positional[0];
        var nonApply = command.Equals("list", StringComparison.OrdinalIgnoreCase) || command.Equals("status", StringComparison.OrdinalIgnoreCase) ||
                       command.Equals("capture", StringComparison.OrdinalIgnoreCase) || command.Equals("profiles", StringComparison.OrdinalIgnoreCase) ||
                       command.Equals("config-check", StringComparison.OrdinalIgnoreCase) || command.Equals("config-path", StringComparison.OrdinalIgnoreCase);
        if ((dryRun || validateOnly) && nonApply)
        {
            Console.Error.WriteLine("--dry-run and --validate-only are valid only when applying a profile.");
            return null;
        }

        return new ParsedArguments(command, positional.Count == 2 ? positional[1] : null, configPath, verbose, dryRun, validateOnly);
    }

    private static void PrintConfiguration(DisplayModeConfiguration configuration)
    {
        Console.WriteLine($"Configuration: {configuration.Path}");
        Console.WriteLine($"Unmanaged targets allowed: {configuration.AllowUnmanagedTargets.ToString().ToLowerInvariant()}");
        Console.WriteLine("Monitors:");
        foreach (var monitor in configuration.Monitors.Values.OrderBy(m => m.Alias))
            Console.WriteLine($"  {monitor.Alias} -> contains '{monitor.Match}'");
        Console.WriteLine("Profiles:");
        foreach (var profile in configuration.Profiles.Values.OrderBy(p => p.Name))
            Console.WriteLine($"  {profile.Name}: {string.Join(", ", profile.ActiveMonitors.Select(m => $"{m.Alias} {m.Width}x{m.Height}@{m.RefreshHz:0.###}"))}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  DisplayMode.exe <profile> [--dry-run | --validate-only] [--verbose]");
        Console.WriteLine("  DisplayMode.exe apply <profile> [--dry-run | --validate-only] [--verbose]");
        Console.WriteLine("  DisplayMode.exe list | status | capture | profiles | config-check | config-path");
        Console.WriteLine("  Add --config <path> to use a different profiles.json file.");
    }

    private sealed record ParsedArguments(string Command, string? ProfileName, string ConfigPath,
        bool Verbose, bool DryRun, bool ValidateOnly);
}

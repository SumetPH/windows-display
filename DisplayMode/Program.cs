using System.ComponentModel;
using DisplayMode.Displays;
using DisplayMode.Models;

namespace DisplayMode;

internal static class Program
{
    private static int Main(string[] args)
    {
        var verbose = args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase));
        var dryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        var validateOnly = args.Any(a => a.Equals("--validate-only", StringComparison.OrdinalIgnoreCase));
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

        if (args.Any(a => a.StartsWith("--", StringComparison.Ordinal) &&
                          !a.Equals("--verbose", StringComparison.OrdinalIgnoreCase) &&
                          !a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase) &&
                          !a.Equals("--validate-only", StringComparison.OrdinalIgnoreCase)) || positional.Length != 1 ||
            (dryRun && validateOnly))
        {
            PrintUsage();
            return 2;
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Console.Error.WriteLine("DisplayMode requires Windows 11 (build 22000 or later).");
            return 1;
        }

        var command = positional[0];
        if ((dryRun || validateOnly) && (command.Equals("list", StringComparison.OrdinalIgnoreCase) ||
                       command.Equals("status", StringComparison.OrdinalIgnoreCase) ||
                       command.Equals("capture", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("--dry-run and --validate-only are only valid with modes 2, 3, or 13.");
            return 2;
        }

        try
        {
            DisplayTopologyManager.ValidateInteropLayout();
            var manager = new DisplayTopologyManager(verbose);
            if (verbose)
                DisplayTopologyManager.PrintInteropSizes();

            var snapshot = manager.Discover();
            if (command.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                manager.PrintMonitors(snapshot, activeOnly: false);
                return 0;
            }

            if (command.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                manager.PrintMonitors(snapshot, activeOnly: true);
                return 0;
            }

            if (command.Equals("capture", StringComparison.OrdinalIgnoreCase))
                return manager.CaptureTimings(snapshot);

            if (!DisplayProfile.Profiles.TryGetValue(command, out var profile))
            {
                Console.Error.WriteLine($"Unknown mode or command: {command}");
                PrintUsage();
                return 2;
            }

            // All three known targets are required before changing anything. This also makes
            // an unexpected cable/device change fail closed instead of disabling the wrong target.
            if (!manager.TryResolveKnownMonitors(snapshot, out var monitors))
                return 1;

            if (!dryRun && !validateOnly)
                manager.CacheObservedTimings(snapshot);
            var proposal = manager.Build(profile, snapshot, monitors);
            return manager.ValidateAndApply(profile.Name, proposal, dryRun, validateOnly);
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
            if (verbose)
                Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  DisplayMode.exe 3 [--dry-run | --validate-only] [--verbose]");
        Console.WriteLine("  DisplayMode.exe 13 [--dry-run | --validate-only] [--verbose]");
        Console.WriteLine("  DisplayMode.exe 2 [--dry-run | --validate-only] [--verbose]");
        Console.WriteLine("  DisplayMode.exe list [--verbose]");
        Console.WriteLine("  DisplayMode.exe status [--verbose]");
        Console.WriteLine("  DisplayMode.exe capture [--verbose]");
        Console.WriteLine("  DisplayMode.exe prepare-13 [--dry-run | --validate-only] [--verbose]");
    }
}

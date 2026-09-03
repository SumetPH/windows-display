namespace DisplayMode.Tray;

internal sealed class SlideshowScheduler : IDisposable
{
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly Func<string, string, Task<CommandResult>> advance;
    private readonly Action<string> reportError;
    private List<ScheduledMonitor> scheduled = [];
    private int generation;

    internal SlideshowScheduler(Func<string, string, Task<CommandResult>> advance, Action<string> reportError)
    {
        this.advance = advance;
        this.reportError = reportError;
        timer.Tick += TimerTick;
    }

    internal void Activate(string profile, IReadOnlyList<SlideshowBinding> bindings)
    {
        Stop();
        var now = DateTimeOffset.UtcNow;
        scheduled = bindings.Select(binding => new ScheduledMonitor(profile, binding,
            now.AddSeconds(binding.IntervalSeconds))).ToList();
        ScheduleNextTick();
    }

    internal void Stop()
    {
        generation++;
        timer.Stop();
        scheduled = [];
    }

    private async void TimerTick(object? sender, EventArgs e)
    {
        timer.Stop();
        var runGeneration = generation;
        var now = DateTimeOffset.UtcNow;
        var due = scheduled.Where(item => item.NextChange <= now).ToArray();

        foreach (var item in due)
        {
            if (runGeneration != generation)
                return;
            var result = await advance(item.Profile, item.Binding.MonitorAlias);
            if (runGeneration != generation)
                return;
            if (result.ExitCode != 0)
                reportError($"Slideshow failed for {item.Binding.MonitorAlias}: {ResultDetail(result)}");
            item.NextChange = DateTimeOffset.UtcNow.AddSeconds(item.Binding.IntervalSeconds);
        }

        if (runGeneration == generation)
            ScheduleNextTick();
    }

    private void ScheduleNextTick()
    {
        if (scheduled.Count == 0)
            return;
        var delay = scheduled.Min(item => item.NextChange) - DateTimeOffset.UtcNow;
        var milliseconds = Math.Clamp((long)Math.Ceiling(delay.TotalMilliseconds), 1_000L, int.MaxValue);
        timer.Interval = (int)milliseconds;
        timer.Start();
    }

    private static string ResultDetail(CommandResult result)
    {
        var detail = result.Output.Split(['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return string.IsNullOrWhiteSpace(detail) ? $"exit {result.ExitCode}" : detail;
    }

    public void Dispose()
    {
        Stop();
        timer.Dispose();
    }

    private sealed class ScheduledMonitor(string profile, SlideshowBinding binding, DateTimeOffset nextChange)
    {
        internal string Profile { get; } = profile;
        internal SlideshowBinding Binding { get; } = binding;
        internal DateTimeOffset NextChange { get; set; } = nextChange;
    }
}

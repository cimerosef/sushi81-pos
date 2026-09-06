using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;

namespace Sushi81.Pos.Desktop;

/// <summary>
/// Opt-in dispatcher/layout evidence for owner-side responsiveness diagnosis.
/// The default path is a single environment-variable comparison and does no I/O.
/// </summary>
internal static class PerformanceTrace
{
    private static readonly bool enabled = string.Equals(
        Environment.GetEnvironmentVariable("SUSHI81_POS_PERF_TRACE"),
        "1",
        StringComparison.Ordinal);
    private static readonly object gate = new();

    public static bool Enabled => enabled;

    public static string LogPath => Path.Combine(Path.GetTempPath(), "Sushi81-POS", "perf-trace.log");

    public static void Log(string marker)
    {
        if (!enabled) return;
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{Stopwatch.GetTimestamp().ToString(CultureInfo.InvariantCulture)} {marker}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never change application behavior.
        }
    }

    public static IDisposable StartDispatcherGapProbe(Dispatcher dispatcher)
    {
        if (!enabled) return NoopDisposable.Instance;

        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        var previous = Stopwatch.GetTimestamp();
        timer.Tick += (_, _) =>
        {
            var current = Stopwatch.GetTimestamp();
            var elapsedMilliseconds = (current - previous) * 1000d / Stopwatch.Frequency;
            previous = current;
            if (elapsedMilliseconds >= 500d)
                Log($"dispatcher.gap.ms={elapsedMilliseconds.ToString("0", CultureInfo.InvariantCulture)}");
        };
        timer.Start();
        return new DelegateDisposable(timer.Stop);
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();
        public void Dispose() { }
    }
}

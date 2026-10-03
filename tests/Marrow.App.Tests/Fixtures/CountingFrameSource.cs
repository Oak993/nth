using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Wraps the real frame source (native runs) and, after every frame it delivers, reads a watched value; each change counts as
/// one update. Used to count how often a TimelineView-driven view actually advances its clock on a real display.
/// </summary>
public sealed class CountingFrameSource(IFrameSource inner) : IFrameSource
{
    private readonly Dictionary<Action<TimeSpan>, Action<TimeSpan>> wrapped = [];
    private Func<double>? watched;
    private double last = double.NaN;
    private readonly List<double> stamps = [];

    /// <summary>The wrapped source's waiting requests (ED-252's warm-up reads it to see whether anything animates).</summary>
    public int Pending => inner.Pending;

    /// <summary>When each frame was delivered, in milliseconds on the high-resolution clock.</summary>
    public IReadOnlyList<double> Stamps => stamps;

    /// <summary>
    /// The updates ED-232 allows over the delivered frames since <paramref name="from"/>: a draw is due one interval after the
    /// previous due time, met at the first frame where elapsed + 1 ms reaches it; more than two intervals behind restarts.
    /// </summary>
    public int AllowedUpdates(int from, double intervalMs)
    {
        if (from >= stamps.Count) return 0;
        var due = stamps[from];
        var count = 0;
        for (var i = from + 1; i < stamps.Count; i++)
        {
            if (stamps[i] - due + 1 < intervalMs) continue;
            count++;
            due = stamps[i] - due >= 2 * intervalMs ? stamps[i] : due + intervalMs;
        }
        return count;
    }

    /// <summary>
    /// The gaps between the frames delivered since <paramref name="from"/>, in whole milliseconds, as "gap:count" pairs,
    /// most frequent first, then the five longest.
    /// </summary>
    public string GapHistogram(int from = 0)
    {
        var measured = stamps.Skip(from).ToList();
        var all = measured.Zip(measured.Skip(1), (a, b) => (int)Math.Round(b - a)).ToList();
        var gaps = all.GroupBy(g => g).OrderByDescending(g => g.Count()).Take(8);
        var longest = all.Select((gap, i) => (gap, at: measured[i] - measured[0])).OrderByDescending(x => x.gap).Take(5);
        return string.Join(" ", gaps.Select(g => $"{g.Key}:{g.Count()}")) + "; longest " + string.Join(" ", longest.Select(x => $"{x.gap}@{x.at:F0}"));
    }

    /// <summary>Changes of the watched value seen so far.</summary>
    public int Updates { get; private set; }

    /// <summary>Frames delivered so far.</summary>
    public int Frames { get; private set; }

    /// <summary>The value to watch; its current value is the starting point.</summary>
    public void Watch(Func<double> value)
    {
        watched = value;
        last = value();
    }

    public void RequestFrame(Action<TimeSpan> frame)
    {
        void Run(TimeSpan time)
        {
            wrapped.Remove(frame);
            Frames++;
            stamps.Add(System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            frame(time);
            if (watched is null) return;
            var now = watched();
            if (!now.Equals(last)) Updates++;
            last = now;
        }
        Action<TimeSpan> run = Run;
        wrapped[frame] = run;
        inner.RequestFrame(run);
    }

    public void CancelFrame(Action<TimeSpan> frame)
    {
        if (wrapped.Remove(frame, out var run)) inner.CancelFrame(run);
    }
}

// PORT-ONLY (ED-NEW-stutter-fix (1)): opt-in interaction timing for the Release performance profile. Off unless a marked
// copied profile asks for it (DesktopPerformanceProbe); when off every entry point is one static bool read, no allocation.
using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Rendering.Composition;

namespace Marrow.UI.Port;

/// <summary>Named work spans and per-frame render facts, recorded only while <see cref="On"/> (diagnostics only).</summary>
public static class InteractionProbe
{
    private static volatile bool on;

    /// <summary>Whether recording is on (set once, before the first window).</summary>
    public static bool On => on;

    /// <summary>Turns recording on for the rest of the process.</summary>
    public static void Enable() => on = true;

    /// <summary>A span of named work; the default value records nothing.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly string? name;
        private readonly long start;
        internal Scope(string name) { this.name = name; start = Stopwatch.GetTimestamp(); }
        public void Dispose()
        {
            if (name is not null) Record(name, start, Stopwatch.GetTimestamp());
        }
    }

    /// <summary>A named span (when off: nothing, and the name is never used).</summary>
    public static Scope Begin(string name) => on ? new Scope(name) : default;

    /// <summary>One recorded span: its name, start and end (Stopwatch ticks) and the managed thread it ran on.</summary>
    public readonly record struct Span(string Name, long Start, long End, int Thread);

    /// <summary>Totals per name: how many, their summed and longest durations (Stopwatch ticks).</summary>
    public sealed class Total
    {
        internal long count, ticks, longest;
        public long Count => Interlocked.Read(ref count);
        public long Ticks => Interlocked.Read(ref ticks);
        public long Longest => Interlocked.Read(ref longest);
    }

    private static readonly ConcurrentDictionary<string, Total> totals = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<Span> spans = new();

    /// <summary>Spans of at least this long are kept one by one (for matching against stalls); all are totalled.</summary>
    public static long KeptTicks { get; set; } = Stopwatch.Frequency / 1000;

    public static void Record(string name, long start, long end)
    {
        var elapsed = end - start;
        var total = totals.GetOrAdd(name, static _ => new Total());
        Interlocked.Increment(ref total.count);
        Interlocked.Add(ref total.ticks, elapsed);
        long seen;
        while (elapsed > (seen = Interlocked.Read(ref total.longest)) && Interlocked.CompareExchange(ref total.longest, elapsed, seen) != seen) { }
        if (elapsed >= KeptTicks) spans.Enqueue(new Span(name, start, end, Environment.CurrentManagedThreadId));
    }

    /// <summary>Counts an event with no duration.</summary>
    public static void Count(string name)
    {
        if (!on) return;
        Interlocked.Increment(ref totals.GetOrAdd(name, static _ => new Total()).count);
    }

    /// <summary>The totals so far (a copy per name).</summary>
    public static IReadOnlyDictionary<string, (long Count, long Ticks, long Longest)> Totals() =>
        totals.ToDictionary(p => p.Key, p => (p.Value.Count, p.Value.Ticks, p.Value.Longest), StringComparer.Ordinal);

    /// <summary>Takes the kept spans recorded since the last call.</summary>
    public static List<Span> DrainSpans()
    {
        var taken = new List<Span>();
        while (spans.TryDequeue(out var span)) taken.Add(span);
        return taken;
    }

    /// <summary>
    /// Called on the render thread after a window's frame is drawn: the window's dirty-rect tracker (its identity), the
    /// frame's merged dirty rect in device pixels (left, top, right, bottom) and the visuals drawn.
    /// </summary>
    public static Action<object, (int Left, int Top, int Right, int Bottom), int>? FrameDrawn { get; set; }

    /// <summary>Asks to hear <see cref="FrameDrawn"/> for the window <paramref name="handler"/>'s visual draws in (render thread).</summary>
    public static void ObserveFrames(CompositionCustomVisualHandler handler) => GlassMaterial.ObserveFrames(handler);

    /// <summary>The dirty-rect tracker of the window <paramref name="handler"/>'s visual draws in, as reported by <see cref="FrameDrawn"/>.</summary>
    public static object? TrackerOf(CompositionCustomVisualHandler handler) => GlassMaterial.TrackerOf(handler);
}

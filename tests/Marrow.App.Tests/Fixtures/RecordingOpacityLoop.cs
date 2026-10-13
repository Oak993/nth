using Avalonia;
using Avalonia.Animation.Easings;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// ED-NEW-perf-idle-2, ED-NEW-perf-idle-3: an opacity loop driver that records what was asked of the compositor (headless has no
// compositor clock to read). Start answers true; the loops it was given run until Stop.
public sealed class RecordingOpacityLoop : IOpacityLoop
{
    public List<(Visual Target, TimeSpan Duration, IReadOnlyList<OpacityKey> Keys, Easing? Ease)> Starts { get; } = [];

    public List<(Visual Target, double Opacity)> Stops { get; } = [];

    /// <summary>Whether the next Start finds a composition visual (false: the element has none yet).</summary>
    public bool HasVisual { get; set; } = true;

    /// <summary>Loops started and not stopped.</summary>
    public int Running => Starts.Count(start => start.Target is { }) - Stops.Count;

    public bool Start(Visual target, TimeSpan duration, IReadOnlyList<OpacityKey> keys, Easing? ease)
    {
        if (!HasVisual) return false;
        Starts.Add((target, duration, keys, ease));
        return true;
    }

    public void Stop(Visual target, double rest) => Stops.Add((target, rest));

    /// <summary>The keys' opacity at <paramref name="progress"/> of the loop, linear between them (what the compositor plays for a null ease).</summary>
    public static double At(IReadOnlyList<OpacityKey> keys, double progress)
    {
        progress -= Math.Floor(progress);
        for (var i = 1; i < keys.Count; i++)
        {
            if (progress > keys[i].At) continue;
            var from = keys[i - 1];
            var span = keys[i].At - from.At;
            return span <= 0 ? keys[i].Value : from.Value + (keys[i].Value - from.Value) * (progress - from.At) / span;
        }
        return keys[^1].Value;
    }
}

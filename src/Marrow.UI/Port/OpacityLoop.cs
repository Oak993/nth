// PORT-ONLY: ED-NEW-perf-idle-2, ED-NEW-perf-idle-3. A loop that never ends and changes one thing, an element's opacity (the terminal's
// blinking caret, the linked-account bridge's pulse), is a composition key-frame animation that the compositor plays by itself:
// nothing wakes the UI thread, and no frame is asked for. The pose is a function of the clock the compositor keeps, so a loop
// started and left alone is the one the frame-clock version drew (same keys, same curve), only without a callback per refresh.
// The ways in and out follow PillMotionDriver.Breathe and CompositionPillAnimator.SetOpacity (ED-NEW-pcctl-22): the control's own
// Opacity is set to the first key before the animation starts, so Avalonia's property sync (which writes the control's Opacity
// to its element visual whenever the control is invalidated, and drops a write equal to the last one) never overwrites it, and
// a value written to the visual twice ends the animation at once.
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Port.PcControl;

namespace Marrow.UI.Port;

/// <summary>One key of an opacity loop: at <paramref name="At"/> (0 to 1 of the loop) the opacity is <paramref name="Value"/>.</summary>
public readonly record struct OpacityKey(float At, float Value);

/// <summary>Runs an element's opacity round a set of keys without end, on the compositor.</summary>
public interface IOpacityLoop
{
    /// <summary>
    /// Starts <paramref name="target"/>'s opacity looping through <paramref name="keys"/> over <paramref name="duration"/>, each
    /// leg along <paramref name="ease"/> (null: linear), from the first key. Returns false (and does nothing) when the element has
    /// no composition visual (not in a window).
    /// </summary>
    bool Start(Visual target, TimeSpan duration, IReadOnlyList<OpacityKey> keys, Easing? ease);

    /// <summary>Ends the loop and leaves the opacity at <paramref name="rest"/> (the control's own property too).</summary>
    void Stop(Visual target, double rest);
}

/// <summary>The loop driver the app uses; tests override it.</summary>
public static class OpacityLoops
{
    public static Ambient<IOpacityLoop> Driver { get; } = new(new CompositionOpacityLoop());
}

/// <summary>
/// One element's opacity loop, started and stopped as its owner needs. An element gets its composition visual with the first
/// render after it joins a window, so a loop asked for before that tries again after the next few frames (at most five times,
/// each a lower-priority turn of the UI thread) rather than never starting.
/// </summary>
public sealed class OpacityLoopRunner(Visual target, TimeSpan duration, IReadOnlyList<OpacityKey> keys, Easing? ease, double rest)
{
    private IOpacityLoop? driver;
    private int generation;

    /// <summary>The loop is wanted (started and not stopped), running or waiting for the element's visual.</summary>
    public bool Wanted { get; private set; }

    /// <summary>The compositor is playing the loop.</summary>
    public bool Running { get; private set; }

    public void Start()
    {
        if (Wanted) return;
        Wanted = true;
        Try(++generation, 0);
    }

    public void Stop()
    {
        Wanted = false;
        generation++;
        if (Running) driver!.Stop(target, rest);
        Running = false;
        driver = null;
    }

    private void Try(int attemptOf, int attempt)
    {
        if (!Wanted || Running || attemptOf != generation) return;
        var loop = OpacityLoops.Driver.Current;
        if (loop.Start(target, duration, keys, ease))
        {
            driver = loop;
            Running = true;
            return;
        }
        if (attempt >= 5) return;
        Dispatcher.UIThread.Post(() => Try(attemptOf, attempt + 1), DispatcherPriority.Background);
    }
}

internal sealed class CompositionOpacityLoop : IOpacityLoop
{
    private static readonly LinearEasing Linear = new();

    public bool Start(Visual target, TimeSpan duration, IReadOnlyList<OpacityKey> keys, Easing? ease)
    {
        if (keys.Count < 2 || ElementComposition.GetElementVisual(target) is not { } visual) return false;
        target.Opacity = keys[0].Value;
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = duration;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.InsertKeyFrame(0f, keys[0].Value);
        for (var i = 1; i < keys.Count; i++) animation.InsertKeyFrame(keys[i].At, keys[i].Value, ease ?? Linear);
        visual.StartAnimation("Opacity", animation);
        return true;
    }

    public void Stop(Visual target, double rest)
    {
        target.Opacity = rest;
        if (ElementComposition.GetElementVisual(target) is { } visual) PillComposition.SetOpacity(visual, rest);
    }
}

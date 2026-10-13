using Avalonia.Controls;

namespace Marrow.Setup.Motion;

/// <summary>Where frames come from: the window's own frame clock in the app, a manual clock in tests.</summary>
public interface IFrameSource
{
    /// <summary>Asks for one call at the next frame with the frame's time.</summary>
    void RequestFrame(Action<TimeSpan> frame);
}

/// <summary>The window's frames (<c>TopLevel.RequestAnimationFrame</c>), the same clock the renderer draws with.</summary>
public sealed class TopLevelFrameSource(TopLevel topLevel) : IFrameSource
{
    public void RequestFrame(Action<TimeSpan> frame) => topLevel.RequestAnimationFrame(frame);
}

/// <summary>A clock a test advances by hand, one frame at a time.</summary>
public sealed class ManualFrameSource : IFrameSource
{
    private readonly List<Action<TimeSpan>> pending = [];

    public TimeSpan Now { get; private set; }

    public bool HasPendingFrame => pending.Count > 0;

    public void RequestFrame(Action<TimeSpan> frame) => pending.Add(frame);

    /// <summary>Runs one frame <paramref name="seconds"/> after the last one.</summary>
    public void Frame(double seconds = 1.0 / 60)
    {
        Now += TimeSpan.FromSeconds(seconds);
        var due = pending.ToArray();
        pending.Clear();
        foreach (var frame in due) frame(Now);
    }

    /// <summary>Runs frames of <paramref name="step"/> seconds for <paramref name="seconds"/>, calling <paramref name="after"/> after each.</summary>
    public void Run(double seconds, Action? after = null, double step = 1.0 / 60)
    {
        var frames = (int)Math.Round(seconds / step);
        for (var i = 0; i < frames; i++)
        {
            Frame(step);
            after?.Invoke();
        }
    }
}

/// <summary>Something that moves: advanced once per frame until it says it has stopped.</summary>
public interface IMotion
{
    /// <summary>Advances by <paramref name="seconds"/>; false once at rest (it is then dropped until started again).</summary>
    bool Advance(double seconds);
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3b, "one per-frame integrator"; unit U1, ED-NEW-inst-u1-1). The one per-frame loop every
/// motion in the window runs on: it asks for a frame only while something moves and stops when everything is at rest.
/// A frame's step is the time since the last frame (the first is one 60 Hz frame), capped at 0.1 s so a stall never
/// throws a spring across the screen.
/// </summary>
public sealed class Animator
{
    private readonly IFrameSource frames;
    private readonly List<IMotion> moving = [];
    private readonly List<IMotion> dispatch = [];
    private readonly Action<TimeSpan> frame;
    private TimeSpan? last;
    private bool requested;
    private bool inFrame;

    public Animator(IFrameSource frames)
    {
        this.frames = frames ?? throw new ArgumentNullException(nameof(frames));
        frame = OnFrame;
    }

    public bool IsIdle => moving.Count == 0;

    /// <summary>Starts advancing <paramref name="motion"/> (once, however often it is started).</summary>
    public void Start(IMotion motion)
    {
        ArgumentNullException.ThrowIfNull(motion);
        if (!moving.Contains(motion)) moving.Add(motion);
        if (requested || inFrame) return;
        requested = true;
        frames.RequestFrame(frame);
    }

    private void OnFrame(TimeSpan now)
    {
        requested = false;
        var step = last is { } previous ? (now - previous).TotalSeconds : 1.0 / 60;
        step = Math.Clamp(step, 0, 0.1);
        inFrame = true;
        try
        {
            // ED-NEW-smooth2-3: retain the snapshot capacity, preserving starts during dispatch for the next frame.
            dispatch.AddRange(moving);
            foreach (var motion in dispatch)
                if (!motion.Advance(step)) moving.Remove(motion);
        }
        finally
        {
            inFrame = false;
            dispatch.Clear();
        }
        if (moving.Count == 0)
        {
            last = null;
            return;
        }
        last = now;
        requested = true;
        frames.RequestFrame(frame);
    }
}

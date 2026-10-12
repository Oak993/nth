// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-5, ED-NEW-pcctl-9; DESIGN.md 3.1 item 5, critique C23).
// When a window's Windows.Graphics.Capture pool waits for a frame, and when it closes. An open pool copies a frame each
// time the window's content changes, so it is throttled to one frame per 200 ms where Windows has MinUpdateInterval;
// where it does not, a pool whose window produced more than 5 frames per second over the last 2 s closes after each grab.
// A pool nobody used for 30 s closes. Pure arithmetic on a millisecond clock, so it is tested without Windows.
namespace Marrow.App.Port.PcControl;

public sealed class WgcPoolPolicy(bool throttled)
{
    /// <summary>A pool no capture used for this long closes.</summary>
    public const int IdleCloseMs = 30_000;

    /// <summary>The MinUpdateInterval set on a session where Windows has the property.</summary>
    public const int MinUpdateIntervalMs = 200;

    /// <summary>Two display frames: how long after a throttled slot a pending frame can still be on its way.</summary>
    public const int FrameGraceMs = 34;

    /// <summary>The longest wait for a new pool's first frame (Windows sends one at the start even for a still window).</summary>
    public const int FirstFrameMs = 1_000;

    /// <summary>An unthrottled pool whose window went above this many frames per second closes after each grab.</summary>
    public const int BusyFramesPerSecond = 5;

    /// <summary>The span the frame rate is measured over.</summary>
    public const int BusyWindowMs = 2_000;

    private readonly Queue<long> frames = new();

    /// <summary>True when the session has MinUpdateInterval set (one frame per 200 ms at most).</summary>
    public bool Throttled => throttled;

    /// <summary>When the last frame arrived, or null before the first.</summary>
    public long? LastFrameAt { get; private set; }

    /// <summary>When a capture last used the pool.</summary>
    public long LastUseAt { get; private set; }

    /// <summary>A frame arrived at <paramref name="now"/>.</summary>
    public void Frame(long now)
    {
        LastFrameAt = now;
        frames.Enqueue(now);
        Trim(now);
    }

    /// <summary>A capture used the pool at <paramref name="now"/>.</summary>
    public void Use(long now) => LastUseAt = now;

    /// <summary>True when nobody used the pool for <see cref="IdleCloseMs"/>.</summary>
    public bool Idle(long now) => now - LastUseAt >= IdleCloseMs;

    /// <summary>
    /// How long a capture at <paramref name="now"/> waits for a newer frame before it reads the newest one it has: the
    /// first frame of a new pool; on a throttled pool whose last frame came less than one slot ago, the rest of that slot
    /// (a change the window made since then is held back until it ends); otherwise nothing, because every change already
    /// produced its frame.
    /// </summary>
    public int WaitMs(long now)
    {
        if (LastFrameAt is not { } last) return FirstFrameMs;
        if (!throttled) return 0;
        var slotEnd = last + MinUpdateIntervalMs + FrameGraceMs;
        return slotEnd > now ? (int)(slotEnd - now) : 0;
    }

    /// <summary>True when an unthrottled pool's window went above the busy rate, so the pool closes after this grab.</summary>
    public bool CloseAfterGrab(long now)
    {
        if (throttled) return false;
        Trim(now);
        return frames.Count > BusyFramesPerSecond * BusyWindowMs / 1000;
    }

    /// <summary>The frames counted in the last <see cref="BusyWindowMs"/>.</summary>
    public int RecentFrames(long now)
    {
        Trim(now);
        return frames.Count;
    }

    private void Trim(long now)
    {
        while (frames.Count > 0 && now - frames.Peek() > BusyWindowMs) frames.Dequeue();
    }
}

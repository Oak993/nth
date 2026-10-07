using Marrow.UI.Port;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Frame-source adapter shared by the compositor and deterministic tests. Idle means no request.</summary>
public sealed class WallpaperPlayback : IDisposable
{
    private readonly WallpaperRenderer renderer;
    private readonly IFrameSource frames;
    private readonly Action changed;
    private readonly Action<TimeSpan> frame;
    private bool pending, disposed;
    private TimeSpan? last;

    public WallpaperPlayback(WallpaperRenderer renderer, IFrameSource frames, Action changed)
    {
        this.renderer = renderer; this.frames = frames; this.changed = changed; frame = Tick;
    }

    public void Refresh()
    {
        if (disposed) return;
        if (!renderer.NeedsFrames)
        {
            if (pending) frames.CancelFrame(frame);
            pending = false; last = null; return;
        }
        if (pending) return;
        pending = true; frames.RequestFrame(frame);
    }

    /// <summary>The clock crossed a minute or the accent changed: the renderer re-reads both, and frames are requested only if a change now eases.</summary>
    public void Wake()
    {
        if (disposed) return;
        renderer.Wake();
        if (renderer.Visible) changed();
        Refresh();
    }

    private void Tick(TimeSpan now)
    {
        pending = false;
        if (disposed || !renderer.NeedsFrames) { last = null; return; }
        renderer.Advance(last is { } before ? Math.Max(0, (now - before).TotalSeconds) : 0);
        last = now; changed(); Refresh();
    }

    public void Dispose()
    {
        if (pending) frames.CancelFrame(frame);
        disposed = true; pending = false; last = null;
    }
}

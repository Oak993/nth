using Marrow.UI.Port;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>What the shared thumbnail ticker drives: one live picture in a wallpaper picker (WL-2, ED-NEW-wltwo-1).</summary>
public interface IWallpaperThumbnailClient
{
    /// <summary>In view and not yet drawn by the engine: waits for its turn to compile its shader.</summary>
    bool WantsAdmission { get; }
    /// <summary>Starts drawing with the engine (its shader compiles on the render thread now).</summary>
    void Admit();
    /// <summary>Drawn by the engine, an animated wallpaper, on screen, its window shown, and Reduce Motion off.</summary>
    bool Animating { get; }
    /// <summary>A change (a pack, an option, the theme) is still easing, so it needs time even when it does not animate.</summary>
    bool Easing { get; }
    /// <summary>Lower goes first when the animating ones are capped: the chosen wallpaper is 0, then the order on the page.</summary>
    int Rank { get; }
    /// <summary>Moves the picture on by this many seconds.</summary>
    void Advance(double seconds);
}

/// <summary>
/// ED-NEW-wltwo-1: one frame clock for every wallpaper thumbnail in the window, so a page of live pictures costs one frame request,
/// not one per picture. It runs only while something animates, eases or waits to be admitted, and then at most at 60 Hz; it admits
/// one new picture at a time (each first draw compiles a shader on the render thread, so they are spread out instead of landing in
/// one frame), and animates at most <see cref="MaxAnimating"/> at once, the chosen one first. A picture that is off screen, in a
/// hidden window, or under Reduce Motion is not advanced at all.
/// </summary>
public sealed class WallpaperThumbnailTicker
{
    /// <summary>How many thumbnails animate at once; the rest hold their still frame until one of these leaves the view.</summary>
    public const int MaxAnimating = 12;
    /// <summary>The seconds between two admissions (a first draw compiles a shader; the GPU program cache keeps it after the first time).</summary>
    public const double AdmitSpacing = 0.05;
    /// <summary>The shortest step: a 120 or 144 Hz display still moves the thumbnails at 60 Hz.</summary>
    public const double MinStep = 1.0 / 60 - 0.002;
    /// <summary>The longest step: after a stall the pictures carry on rather than jump.</summary>
    public const double MaxStep = 0.1;

    [ThreadStatic] private static WallpaperThumbnailTicker? shared;
    /// <summary>The UI thread's ticker.</summary>
    public static WallpaperThumbnailTicker Shared => shared ??= new();

    private readonly List<IWallpaperThumbnailClient> clients = [];
    private readonly Action<TimeSpan> frame;
    private IFrameSource? source;
    private bool pending;
    private TimeSpan? last;
    private double sinceAdmit = double.PositiveInfinity;
    private readonly List<IWallpaperThumbnailClient> scratch = [];

    public WallpaperThumbnailTicker() => frame = Frame;

    /// <summary>The thumbnails registered now (those attached to a window).</summary>
    public int Count => clients.Count;
    /// <summary>Whether a frame is requested.</summary>
    public bool Running => pending;
    /// <summary>How many were advanced at the last frame that moved them.</summary>
    public int LastAnimated { get; private set; }

    public void Add(IWallpaperThumbnailClient client)
    {
        if (!clients.Contains(client)) clients.Add(client);
        Wake();
    }

    public void Remove(IWallpaperThumbnailClient client)
    {
        clients.Remove(client);
        if (clients.Count == 0) Stop();
    }

    /// <summary>Something a client reports has changed (it came into view, was chosen, its window was shown): runs if there is work.</summary>
    public void Wake()
    {
        var current = FrameClock.Source.Current;
        if (pending && !ReferenceEquals(current, source)) Stop(); // the frame source was replaced (a window's, a test's): move to it
        if (pending || !HasWork()) return;
        source = current;
        pending = true; source.RequestFrame(frame);
    }

    private void Stop()
    {
        if (pending) source?.CancelFrame(frame);
        pending = false; last = null;
    }

    private bool HasWork()
    {
        foreach (var c in clients) if (c.WantsAdmission || c.Animating || c.Easing) return true;
        return false;
    }

    private void Frame(TimeSpan now)
    {
        pending = false;
        var step = last is { } before ? Math.Clamp((now - before).TotalSeconds, 0, MaxStep) : 0;
        if (last is not null && step < MinStep) { Continue(); return; } // paced to 60 Hz: wait for the next refresh
        last = now;
        sinceAdmit += step;
        if (sinceAdmit >= AdmitSpacing - 1e-9)
        {
            IWallpaperThumbnailClient? next = null;
            foreach (var c in clients) if (c.WantsAdmission && (next is null || c.Rank < next.Rank)) next = c;
            if (next is not null) { next.Admit(); sinceAdmit = 0; }
        }
        scratch.Clear();
        foreach (var c in clients) if (c.Animating) scratch.Add(c);
        if (scratch.Count > MaxAnimating) { scratch.Sort((a, b) => a.Rank.CompareTo(b.Rank)); scratch.RemoveRange(MaxAnimating, scratch.Count - MaxAnimating); }
        if (step > 0)
        {
            foreach (var c in scratch) c.Advance(step);
            foreach (var c in clients) if (c.Easing && !scratch.Contains(c)) c.Advance(step);
            LastAnimated = scratch.Count;
        }
        scratch.Clear();
        Continue();
    }

    private void Continue()
    {
        if (!HasWork()) { last = null; return; }
        source ??= FrameClock.Source.Current;
        pending = true; source.RequestFrame(frame);
    }
}

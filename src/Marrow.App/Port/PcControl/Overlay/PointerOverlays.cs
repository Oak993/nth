// PORT-ONLY: Tactus pointers on screen (OD-0144, OD-0145, ED-NEW-pcctl-21; DESIGN.md 3.1 overlay row, 4.5 "The Tactus
// pointer", 10.1, 10.8; critique C19). One PointerTrack per active app, fed by the engine's PcPointerFeed, the lanes, Pause
// and the borrow; each visible pointer has its own small click-through, no-activate, capture-excluded window
// (OverlayRole.Pointer) that exists only while the pointer shows and 1 s after it left. A screen point (physical pixels)
// becomes monitor-local DIP of the monitor that holds it; the window's rect goes back to that monitor's pixels, rounded
// outward, and is set only when it changes (a glide's start and end), never per frame. Nothing is drawn on a monitor in
// exclusive fullscreen or presentation mode; a pointer there, or at a point the user cannot see, parks on its pill row
// ("working behind"). A pointer whose app has had no work for 2 s and is not paused leaves (the pill's own linger).
// Frame() draws every pointer at one time and says whether more frames are wanted; the binder runs it on the UI thread
// from the render loop while it returns true, and FramesWanted asks for the loop again.
using Avalonia;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Core.Port.PcControl.Pointer;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Port.PcControl.Overlay;

/// <summary>One pointer's window.</summary>
public interface IPointerSurface
{
    /// <summary>Moves and sizes the window (physical pixels) on a monitor of <paramref name="scale"/>; never per frame.</summary>
    void Place(PixelRect rect, double scale);

    /// <summary>Draws the pose; <paramref name="origin"/> is the window's top-left in the pose's monitor DIP.</summary>
    void Draw(PointerPose pose, Point origin);

    void Close();
}

public interface IPointerSurfaces
{
    /// <summary>Makes a pointer window for the app <paramref name="aim"/> names (its badge from the app's icon); shown on its first place.</summary>
    IPointerSurface Open(PcPointerAim aim);
}

/// <summary>The monitors, and where nothing may be drawn.</summary>
public interface IPointerScreens
{
    IReadOnlyList<PcMonitor> Monitors();

    /// <summary>True when an exclusive-fullscreen or presentation-mode app is on the monitor (SHQueryUserNotificationState).</summary>
    bool NothingDrawnOn(int monitor);
}

public sealed class PointerOverlays
{
    /// <summary>A pointer leaves this long after its app's last work ended (unless paused).</summary>
    public const double LingerSeconds = 2;

    private readonly IPointerSurfaces surfaces;
    private readonly IPointerScreens screens;
    private readonly Func<double> seconds;
    private readonly Func<bool> reduceMotion;
    private readonly Dictionary<string, Entry> pointers = new(StringComparer.Ordinal);

    public PointerOverlays(IPointerSurfaces surfaces, IPointerScreens screens, Func<double> seconds, Func<bool> reduceMotion)
    {
        this.surfaces = surfaces;
        this.screens = screens;
        this.seconds = seconds;
        this.reduceMotion = reduceMotion;
    }

    /// <summary>A change started motion: run Frame from the render loop until it returns false.</summary>
    public event Action? FramesWanted;

    /// <summary>The app's pointer is parked on its pill row ("working behind").</summary>
    public bool Parked(string app) => pointers.TryGetValue(app, out var entry) && entry.Track.Parked;

    /// <summary>The app's track, for the pill and the tests.</summary>
    public PointerTrack? Track(string app) => pointers.TryGetValue(app, out var entry) ? entry.Track : null;

    /// <summary>The engine is about to act (on the UI thread).</summary>
    public void Aimed(PcPointerAim aim)
    {
        var now = seconds();
        if (!pointers.TryGetValue(aim.App, out var entry))
        {
            entry = new Entry(aim, new PointerTrack(reduceMotion()));
            pointers[aim.App] = entry;
        }
        entry.Aim = aim;
        entry.InactiveSince = null;
        entry.Ended = false;
        entry.Track.ReduceMotion = reduceMotion();
        var monitor = screens.Monitors().FirstOrDefault(m => m.Bounds.Contains(aim.Point));
        if (!aim.Visible || monitor is null || screens.NothingDrawnOn(monitor.Index))
        {
            entry.Track.Park(now);
        }
        else
        {
            var scale = Scale(monitor);
            entry.Track.Aim(now, monitor.Index, new PillVector((aim.Point.X - monitor.Bounds.X) / scale, (aim.Point.Y - monitor.Bounds.Y) / scale));
            if (aim.Act == PcPointerAct.Press) entry.Track.Press(now);
        }
        Changed();
    }

    /// <summary>The apps with work running or waiting (PcLanes.ActiveChanged): the others start their 2 s linger.</summary>
    public void Active(IReadOnlyCollection<string> apps)
    {
        var now = seconds();
        foreach (var (app, entry) in pointers)
        {
            if (apps.Contains(app)) entry.InactiveSince = null;
            else entry.InactiveSince ??= now;
        }
        Changed();
    }

    public void Paused(string app, bool paused)
    {
        if (!pointers.TryGetValue(app, out var entry)) return;
        entry.Track.Pause(seconds(), paused);
        Changed();
    }

    /// <summary>A borrow of the real mouse for <paramref name="app"/> started or ended.</summary>
    public void Borrowing(string app, bool real)
    {
        if (!pointers.TryGetValue(app, out var entry)) return;
        entry.Track.Borrow(seconds(), real);
        Changed();
    }

    /// <summary>Draws every pointer at one time; true while anything still moves or waits to leave.</summary>
    public bool Frame()
    {
        var now = seconds();
        var more = false;
        var monitors = screens.Monitors();
        foreach (var (app, entry) in pointers.ToArray())
        {
            var track = entry.Track;
            if (entry.InactiveSince is { } since && !track.Paused)
            {
                if (now - since >= LingerSeconds)
                {
                    track.Hide(now);
                    entry.InactiveSince = null;
                    entry.Ended = true;
                }
                else more = true;
            }
            var monitor = monitors.FirstOrDefault(m => m.Index == track.Monitor);
            if (!track.NeedsWindow(now) || monitor is null || screens.NothingDrawnOn(monitor.Index))
            {
                entry.Surface?.Close();
                entry.Surface = null;
                entry.Placed = null;
                if (entry.Ended && track.Phase(now) == PointerPhase.Hidden && !track.NeedsWindow(now)) pointers.Remove(app);
                continue;
            }
            var scale = Scale(monitor);
            var pixels = Pixels(monitor, track.WindowRect(now), scale);
            entry.Surface ??= surfaces.Open(entry.Aim);
            if (entry.Placed != pixels)
            {
                entry.Surface.Place(pixels, scale);
                entry.Placed = pixels;
            }
            entry.Surface.Draw(track.Pose(now), new Point((pixels.X - monitor.Bounds.X) / scale, (pixels.Y - monitor.Bounds.Y) / scale));
            // A pointer that left keeps frames coming until its window's last second is over and it can close.
            more |= track.Moving(now) || track.Phase(now) == PointerPhase.Hidden;
        }
        return more;
    }

    /// <summary>The monitor's DIP scale (its DPI over 96).</summary>
    public static double Scale(PcMonitor monitor) => monitor.Dpi > 0 ? monitor.Dpi / 96.0 : 1;

    /// <summary>A monitor-local DIP rect in that monitor's physical pixels, rounded outward.</summary>
    public static PixelRect Pixels(PcMonitor monitor, PillRect rect, double scale)
    {
        var left = (int)Math.Floor(monitor.Bounds.X + (rect.X * scale));
        var top = (int)Math.Floor(monitor.Bounds.Y + (rect.Y * scale));
        var right = (int)Math.Ceiling(monitor.Bounds.X + (rect.Right * scale));
        var bottom = (int)Math.Ceiling(monitor.Bounds.Y + (rect.Bottom * scale));
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private void Changed() => FramesWanted?.Invoke();

    private sealed class Entry(PcPointerAim aim, PointerTrack track)
    {
        public PcPointerAim Aim { get; set; } = aim;
        public PointerTrack Track { get; } = track;
        public IPointerSurface? Surface { get; set; }
        public PixelRect? Placed { get; set; }
        public double? InactiveSince { get; set; }
        public bool Ended { get; set; }
    }
}

// PORT-ONLY: Calculi (ED-NEW-calculi-wu4-1; DESIGN.md sections 5.1, 8, 9 and 11). The working beads as a control: it measures to
// the motion envelope, keeps the turn's state as one immutable NthBeadsFrame, and posts it to its composition visual only when
// something changes. Every call is stamped with the host clock's time; the render thread applies it then. Nothing here waits for
// the beads: the site's text and the composer follow the turn state at once, the exit is cosmetic (section 5.1).
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using SkiaSharp;

namespace Marrow.UI.Port.Nth.Beads;

/// <summary>The working beads (Calculi). One per site; the site maps its turn to these calls.</summary>
public sealed class NthBeadsControl : NthMotionControl
{
    /// <summary>The n box in DIP (18 on the working line; 12 to 16 at the small sites).</summary>
    public static readonly StyledProperty<double> GlyphSizeProperty = AvaloniaProperty.Register<NthBeadsControl, double>(nameof(GlyphSize), 18);

    /// <summary>The quiet family (sidebar, Hydra, chips): one slow pulse, no beats, no steps.</summary>
    public static readonly StyledProperty<bool> QuietProperty = AvaloniaProperty.Register<NthBeadsControl, bool>(nameof(Quiet));

    /// <summary>A background, sidebar or Hydra site: 30 Hz at any size (section 11).</summary>
    public static readonly StyledProperty<bool> AmbientProperty = AvaloniaProperty.Register<NthBeadsControl, bool>(nameof(Ambient));

    private NthBeadsFrame state = NthBeadsFrame.Blank;
    private readonly NthBeadsDriver driver = new(); // outlives each visual: a re-attached control goes on, never replays
    private NthBeadsHostPainter? painter;
    private AmbientWindowState? window;
    private IPlatformSettings? platform;

    public NthBeadsControl()
    {
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
    }

    public double GlyphSize { get => GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }
    public bool Quiet { get => GetValue(QuietProperty); set => SetValue(QuietProperty, value); }
    public bool Ambient { get => GetValue(AmbientProperty); set => SetValue(AmbientProperty, value); }

    /// <summary>The turn's state as last posted (or about to be).</summary>
    public NthBeadsFrame Current => state;

    /// <summary>The render thread's painter (tests; null until shown).</summary>
    public NthBeadsHostPainter? Painter => painter;

    /// <summary>Whether the beads draw nothing now: no turn has entered, or the render thread has applied the latest frame and its exit has ended
    /// (or was cut). The site moves the beads on it (a far hand-over, ED-NEW-calculi-wu5-2); no UI waits for it (section 5.1).</summary>
    public bool IsGone => state.Turn == 0 || (state.Exit != NthBeadsExit.None && driver.Gone && ReferenceEquals(driver.Latest, state));

    /// <summary>Now on the clock every call is stamped with, in seconds (the frame's times: <see cref="NthBeadsFrame.ExitAt"/> and the rest).</summary>
    public double Time => Now;

    /// <summary>Starts a turn (a new one while the last is leaving plays Rejoin, never a reset). The seed is the turn id's FNV-1a.</summary>
    public void Enter(NthBeadsEntrance entrance, string turnId) =>
        Post(state with { Turn = state.Turn + 1, Entrance = entrance, Seed = NthBeadsEngine.SeedFrom(turnId), EnterAt = Now, Exit = NthBeadsExit.None });

    /// <summary>The turn's working state (error and stopped come from <see cref="Fail"/> and <see cref="Stop"/>).</summary>
    public void SetState(NthBeadsState value)
    {
        if (value is NthBeadsState.Error or NthBeadsState.Stopped) throw new ArgumentOutOfRangeException(nameof(value), value, "set by Fail and Stop");
        if (value == state.State) return;
        Post(state with { State = value, StateAt = Now });
    }

    /// <summary>A new step (the activity text changed).</summary>
    public void Step() => Post(state with { Steps = state.Steps + 1, StepAt = Now });

    /// <summary>The turn finished, failed or was stopped: the exit plays beside the UI, which has already changed.</summary>
    public void Finish() => End(NthBeadsExit.Finish);
    public void Fail() => End(NthBeadsExit.Fail);
    public void Stop() => End(NthBeadsExit.Stop);

    /// <summary>The glyph moved in layout (not by scrolling) by (<paramref name="dx"/>, <paramref name="dy"/>) DIP: the beads keep
    /// their place on screen and glide after it (section 9).</summary>
    public void Reflow(double dx, double dy)
    {
        if (dx == 0 && dy == 0) return;
        var u = NthBeadsPainter.Unit(GlyphSize);
        Post(state with { Reflows = state.Reflows + 1, ReflowX = state.ReflowX + dx / u, ReflowY = state.ReflowY + dy / u, ReflowAt = Now });
    }

    private void End(NthBeadsExit exit)
    {
        if (state.Turn == 0 || state.Exit != NthBeadsExit.None) return;
        Post(state with { Exit = exit, ExitAt = Now });
    }

    private void Post(NthBeadsFrame next)
    {
        if (next == state) return;
        state = next;
        Send();
    }

    // The values the environment decides: size and rate, calm (stamped when it changes), contrast and whether it is shown.
    private NthBeadsFrame Stamp(NthBeadsFrame f)
    {
        var size = GlyphSize;
        var calm = Calm;
        var shown = IsEffectivelyVisible && (window?.Available ?? true);
        return f with
        {
            Size = size, Quiet = Quiet, Every = NthBeadsFrame.EveryFor(size, Ambient), Shown = shown, Contrast = ContrastColor(),
            Calm = calm, CalmAt = calm == f.Calm ? f.CalmAt : Now,
        };
    }

    private void Restamp() => Post(Stamp(state));

    protected override NthPainter CreatePainter() => painter = new NthBeadsHostPainter(driver);

    protected override object? State => state;

    protected override Size MeasureOverride(Size availableSize) => NthBeadsPainter.EnvelopeSize(GlyphSize);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GlyphSizeProperty) InvalidateMeasure();
        if (change.Property == GlyphSizeProperty || change.Property == QuietProperty || change.Property == AmbientProperty
            || change.Property == IsVisibleProperty || change.Property == EnvironmentValues.AccessibilityReduceMotionProperty)
            Restamp();
    }

    private void Attach()
    {
        window ??= AmbientWindowState.Acquire(this);
        if (window is not null) window.Changed += WindowChanged;
        platform = Application.Current?.PlatformSettings;
        if (platform is not null) platform.ColorValuesChanged += ColorsChanged;
        Restamp();
    }

    private void Detach()
    {
        if (window is not null) { window.Changed -= WindowChanged; window.Dispose(); window = null; }
        if (platform is not null) platform.ColorValuesChanged -= ColorsChanged;
        platform = null;
    }

    // the window's state also changes on every input: restamp only when availability moves
    private void WindowChanged()
    {
        if ((IsEffectivelyVisible && (window?.Available ?? true)) != state.Shown) Restamp();
    }

    private void ColorsChanged(object? sender, PlatformColorValues values) => Restamp();

    // High contrast (forced colours): the system's window text colour, flat, no gradient, no glint (section 10).
    private SKColor? ContrastColor()
    {
        if (platform?.GetColorValues() is not { ContrastPreference: ColorContrastPreference.High }) return null;
        if (!OperatingSystem.IsWindows()) return Dark ? SKColors.White : SKColors.Black;
        var bgr = GetSysColor(8); // COLOR_WINDOWTEXT
        return new SKColor((byte)(bgr & 0xff), (byte)((bgr >> 8) & 0xff), (byte)((bgr >> 16) & 0xff));
    }

    [DllImport("user32.dll")] private static extern uint GetSysColor(int index);
}

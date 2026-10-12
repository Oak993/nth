// PORT-ONLY: Ignis U4 (OD-0134, ED-NEW-ignis-views). The Kindle capsule (design "Kindle (locked)", ".kindle"): one small
// liquid-glass capsule in the composer, where the ContextMeter sits, holding the ring (context fill warming grey → ember with
// the cache's heat), the breathing ember core and a rolling countdown ("42m", "Cold", "Off"). A press opens the Ignis popover
// above it (PlainControls.Popover through the popover host, placement on the top edge, so it slides up out of the composer;
// Escape closes it as it closes every popover). No keyboard shortcuts (owner). SOURCE FACT (design): 28 pt high, 14 pt corner,
// 7 pt before the ring and 10 pt after the words, 6 pt gaps, at least 80 pt wide, 11.5 pt medium tabular words, an ember
// glow under the glass at 40 % of the heat. ENGINEERING DECISION: the glass is the app's chrome glass capsule (the existing
// material), not the page's CSS gradient; the countdown re-reads the clock once a second while warm.
// ED-NEW-kindlehover-1 (owner, 2026-10-11: "Dull pill. Doesn't even do anything when I hover mouse over it?"): the capsule
// answers the pointer in the composer chip's language (ChipButtonStyle: a primary-text wash of 7 % under the pointer, 12 %
// pressed, eased 0.12 s; here the press eases too, 0.08 s), with a hand cursor and no tooltip (OD-0138). The wash fills the
// whole capsule, so the ring, the drop and the padding take the press (before, only the words' glyphs were hit-testable and
// a press on the ring fell through to the composer). The words stay primary in every state. A Cold capsule's ring track,
// separator and drop rest a little above the design's cold grey (ColdRestLift: the drop at least 3:1 in both appearances);
// under the pointer or while the popover is open, a Cold or Off capsule's ring and drop lift fully toward the design's
// secondary tone and a Cold core kindles faintly. Everything eases; Reduce Motion takes each value at once; nothing runs at
// rest (eased values only, no timer).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.Ignis.Views;

public sealed class KindleCapsule : Decorator
{
    public const double CapsuleHeight = 28, MinCapsuleWidth = 80;
    /// <summary>The pointer wash's primary-text opacity under the pointer and pressed (ChipButtonStyle's :86-87).</summary>
    public const double HoverWash = .07, PressedWash = .12;
    /// <summary>A Cold capsule's resting lift of the ring track, separator and drop toward the design's text tones (the drop
    /// 3.3:1 on a white surface, 4.9:1 on #1E1E20; 2.4:1 and 3.4:1 at the bare cold grey). Off rests at the bare grey.</summary>
    public const double ColdRestLift = .3;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);

    private sealed class ViewState : MainActorModel
    {
        private bool presented;
        private IgnisChatStatus status;
        private int tick;
        public ViewState(IgnisChatStatus status) => this.status = status;
        public bool Presented { get => Read(presented); set => Set(ref presented, value); }
        public IgnisChatStatus Status { get => Read(status); set => Set(ref status, value); }
        /// <summary>Bumped when the clock moved on enough to change the countdown.</summary>
        public int Tick { get => Read(tick); set => Set(ref tick, value); }
    }

    private readonly IIgnisController controller;
    private readonly Guid thread;
    private readonly ViewState state;
    private readonly KindleGlyph glyph = new();
    private readonly RollingText roll;
    private readonly Border glow;
    private readonly Border wash;
    private readonly AnimatedColor washFill;
    private readonly AnimatedScalar glowLevel;
    private readonly Button button;
    private readonly Popup popup;
    private readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool attached, primed;

    public KindleCapsule(IIgnisController controller, Guid thread)
    {
        this.controller = controller;
        this.thread = thread;
        state = new ViewState(controller.Status(thread));
        roll = new RollingText(() =>
        {
            var text = new TextBlock { FontFeatures = Tabular() };
            SwiftFont.System(11.5, SwiftFont.Weight.Medium).Apply(text);
            text.Foreground = Chrome.PrimaryText.Brush(text);
            return text;
        }) { VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Height = CapsuleHeight,
            Margin = new Thickness(7, 0, 10, 0),
            Children = { glyph, roll },
        };
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glow = new Border { IsHitTestVisible = false, CornerRadius = new CornerRadius(14), Opacity = 0 };
        glowLevel = new AnimatedScalar(glow, 0, value => glow.Opacity = value);
        // the pointer wash: always a brush (clear at rest), so the whole capsule takes the pointer and the press
        wash = new Border().Capsule(RoundedCornerStyle.Continuous);
        washFill = new AnimatedColor(wash);
        washFill.Set(Chrome.Overlay(0), null);
        wash.Background = washFill.Brush;
        var face = new Panel { MinWidth = MinCapsuleWidth, Children = { wash, glow, row } };
        button = PlainControls.Button(face, () => state.Presented = !state.Presented).KeyboardFocus();
        button.Focusable = true;
        button.Cursor = new Cursor(StandardCursorType.Hand);
        button.PropertyChanged += (_, e) =>
        {
            if (primed && (e.Property == InputElement.IsPointerOverProperty || e.Property == Button.IsPressedProperty)) PaintPointer();
        };
        AutomationProperties.SetName(button, IgnisWords.CapsuleAccessibleName);
        var capsule = button.ChromeGlassCapsule();
        capsule.VerticalAlignment = VerticalAlignment.Center;
        var binding = new Binding<bool>(() => state.Presented, value => state.Presented = value);
        popup = PlainControls.Popover(button, binding, () => new KindlePopover(this), PlacementMode.TopEdgeAlignedRight);
        Child = new Panel { Children = { capsule, popup } };

        ticker.Tick += (_, _) => OnMain(Refresh);
        AttachedToVisualTree += (_, _) => { attached = true; controller.Changed += OnChanged; Refresh(); };
        DetachedFromVisualTree += (_, _) => { attached = false; controller.Changed -= OnChanged; ticker.Stop(); if (state.Presented) state.Presented = false; };
        ActualThemeVariantChanged += (_, _) => PaintGlow();
        this.Track(() => (state.Status, state.Tick), _ => Apply());
        this.Track(() => state.Presented, open => { popoverOpen = open; if (primed) PaintPointer(); });
    }

    // ---- what the popover and tests read

    internal IIgnisController Controller => controller;
    internal Guid ThreadID => thread;
    internal IgnisChatStatus Status => state.Status;
    internal int Tick => state.Tick;
    internal Button Button => button;
    internal Popup Popover => popup;
    internal KindleGlyph Glyph => glyph;
    internal RollingText Countdown => roll;
    internal Border Wash => wash;
    /// <summary>The pointer wash's colour drawn now.</summary>
    internal Color WashColor => washFill.Brush.Color;
    /// <summary>The words' colour drawn now (each character's own brush; all alike).</summary>
    internal Color? WordsColor => roll.Children.OfType<Panel>().SelectMany(cell => cell.Children.OfType<TextBlock>()).Select(text => (text.Foreground as ISolidColorBrush)?.Color).FirstOrDefault();
    /// <summary>Whether the capsule shows its lifted (pointer or open popover) state.</summary>
    internal bool IsLifted => liftedShown;
    internal bool IsPresented { get => state.Presented; set => state.Presented = value; }

    /// <summary>The capsule's words now: "Off", "{m}m" while warm, "Cold".</summary>
    public string Text => roll.Text;

    /// <summary>The cache state as the clock sees it now (a warm cache whose time ran out reads cold before the controller says so).</summary>
    internal static IgnisCacheState Effective(IgnisChatStatus status, DateTimeOffset now) =>
        status.Mode == IgnisMode.Off ? IgnisCacheState.Off
        : status.Cache == IgnisCacheState.Warm && status.ExpiresAt is { } expires && expires <= now ? IgnisCacheState.Cold
        : status.Cache;

    internal static TimeSpan? Left(IgnisChatStatus status, DateTimeOffset now) => status.ExpiresAt is { } expires ? expires - now : null;

    /// <summary>0 when off or cold; while warm 0.3 + 0.7 × the share of the cache's life left (the page's --heat).</summary>
    internal static double HeatOf(IgnisChatStatus status, DateTimeOffset now)
    {
        if (Effective(status, now) != IgnisCacheState.Warm || Left(status, now) is not { } left) return 0;
        var ttl = status.Ttl is { } t && t > TimeSpan.Zero ? t : DefaultTtl;
        return .3 + .7 * Math.Clamp(left / ttl, 0, 1);
    }

    /// <summary>The share of the cache's life left (the quick memory track's fill, the page's --cache).</summary>
    internal static double CacheLeftOf(IgnisChatStatus status, DateTimeOffset now)
    {
        if (Effective(status, now) != IgnisCacheState.Warm || Left(status, now) is not { } left) return 0;
        var ttl = status.Ttl is { } t && t > TimeSpan.Zero ? t : DefaultTtl;
        return Math.Clamp(left / ttl, 0, 1);
    }

    /// <summary>Reads the controller and the clock again (the controller's Changed, the once-a-second tick, tests).</summary>
    public void Refresh()
    {
        var next = controller.Status(thread);
        if (!Equals(next, state.Status)) state.Status = next;
        state.Tick++;
        var warm = Effective(next, SwiftDate.Now) == IgnisCacheState.Warm;
        if (attached && warm && FrameClock.IsBound) ticker.Start();
        else ticker.Stop();
    }

    private void OnChanged(Guid changed)
    {
        if (changed != Guid.Empty && changed != thread) return;
        OnMain(Refresh);
    }

    private static void OnMain(Action work)
    {
        if (MainActor.IsIsolated) work();
        else MainActor.Post(work);
    }

    private IgnisChatStatus? shown;
    private IgnisCacheState cacheShown = IgnisCacheState.Off;
    private bool popoverOpen, liftedShown;
    private double washShown, liftShown, kindleShown;

    // hover is the chip's ease-out (0.12 s, ChipButtonStyle :80-81); a press lands a little faster
    private const double HoverSeconds = .12, PressSeconds = .08;
    private static readonly Animation HoverCurve = Animation.EaseOut(HoverSeconds);
    private static readonly Animation PressCurve = Animation.EaseOut(PressSeconds);

    /// <summary>Paints the pointer's state (the wash, the ring and drop's lift, a cold core's kindling) along the pointer's
    /// curves, or along <paramref name="change"/> for a change of the cache's state (Apply); <paramref name="immediate"/>: at once
    /// (the first showing). Each value is set only when its target moves, so the once-a-second tick restarts nothing.</summary>
    private void PaintPointer(Animation? change = null, bool immediate = false)
    {
        var over = button.IsPointerOver;
        var pressed = over && button.IsPressed;
        var lifted = over || popoverOpen;
        liftedShown = lifted;
        bool? reduced = immediate ? true : null;
        bool Reduce() => reduced ??= KindleMotion.Reduce(this); // read only when something is to move
        var warm = cacheShown == IgnisCacheState.Warm;
        var washTo = pressed ? PressedWash : lifted ? HoverWash : 0;
        if (washTo != washShown)
        {
            var curve = change ?? (pressed || washShown == PressedWash ? PressCurve : HoverCurve);
            washShown = washTo;
            washFill.Set(Chrome.Overlay(washTo), Reduce() ? null : curve);
        }
        var liftTo = warm ? 0 : lifted ? 1 : cacheShown == IgnisCacheState.Cold ? ColdRestLift : 0;
        var kindleTo = cacheShown == IgnisCacheState.Cold && lifted ? 1 : 0;
        if (liftTo != liftShown || kindleTo != kindleShown)
        {
            var kindleCurve = change ?? (kindleTo > kindleShown ? KindleMotion.Smooth(.45) : Animation.EaseOut(.25));
            (liftShown, kindleShown) = (liftTo, kindleTo);
            glyph.Hover(liftTo, kindleTo, Reduce() ? null : change ?? Animation.EaseOut(.15), Reduce() ? null : kindleCurve);
        }
    }

    private void Apply()
    {
        var status = state.Status;
        var now = SwiftDate.Now;
        var cache = Effective(status, now);
        var text = IgnisWords.CapsuleText(cache, Left(status, now));
        var animate = primed;
        roll.Show(text, animate);
        var heat = HeatOf(status, now);
        glyph.Show(heat, status.ContextFill ?? 0, cache == IgnisCacheState.Warm, status.Tidying, animate);
        glowLevel.Set(heat * .4, animate ? KindleMotion.Ease(this, Animation.EaseOut(.6)) : null);
        cacheShown = cache;
        PaintPointer(Animation.EaseOut(.6), immediate: !animate);
        if (shown is { } before && primed)
        {
            if (status.PingsToday > before.PingsToday && cache == IgnisCacheState.Warm) glyph.Spark();
            if (before.Tidying && !status.Tidying || !before.JustTidied && status.JustTidied) glyph.Settle();
        }
        shown = status;
        primed = true;
        AutomationProperties.SetHelpText(button, IgnisWords.CacheStatus(cache) + ", " + IgnisWords.TimeLeft(cache, Left(status, now)));
        PaintGlow();
    }

    private void PaintGlow()
    {
        var palette = KindlePalette.For(this);
        glow.Background = new RadialGradientBrush
        {
            Center = new RelativePoint(.34, 1.2, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(.34, 1.2, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(.7, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(1.3, RelativeUnit.Relative),
            GradientStops = [new GradientStop(palette.EmberGlow, 0), new GradientStop(Color.FromArgb(0, palette.EmberGlow.R, palette.EmberGlow.G, palette.EmberGlow.B), .7)],
        };
    }

    internal static FontFeatureCollection Tabular() => [FontFeature.Parse("tnum")];
}

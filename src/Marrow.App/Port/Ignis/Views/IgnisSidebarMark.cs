// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-guards). The sidebar's keep-warm mark (owner 2026-10-10): the control lives only in
// the left chat list. A chat Ignis keeps warm shows a thin ember line at its row's leading edge; "Turn off" puts it out and
// leaves it dim and cold (cooled); Ignis cools it too when it stops keeping the chat warm by itself (the idle limit, three
// chats, the day's allowance); "Turn on" or the next message lights it again; a row Ignis never kept warm shows nothing.
//
// The state and the look are apart (lead 2026-10-10), so the look can change without touching the rules: IgnisRowWarmth is
// what a row knows (lit / cooled / none, until when it stays warm, what Ignis saved in it today), IgnisRowWarmthSource keeps
// it current from the controller, and IgnisSidebarMark is the look the owner picked over it (variant B of the review mock
// work/design/nth-brand/ignis-sidebar-review.html, with the owner's two changes):
// - At rest: the ember line only (2.5 pt wide, 2 pt corner, 7 pt in from the top and bottom, 2 pt from the leading edge), the
//   ember gradient (core #ffd57e to outer #ff6a1f) with a soft glow, breathing slowly (opacity .8 to 1 over 2.6 s on the paced
//   ambient clock, so it never wakes main; Reduce Motion: still). No flame at rest.
// - A completely invisible hover zone at the row's trailing end (the row's full height, 32 pt wide; with the pill one hover
//   region, 120 ms of grace on leave): the flame fades in (+14 % brightness) and a compact glass pill slides out to its left
//   (0.2 s ease-out, 6 pt), the title's end masked under it.
// - Lit, the pill reads "Turn off": the flame douses (0.42 s, squashes toward its base and settles, its glow out, its colour to
//   the cold grey) and the line turns dim cold grey (.5 opacity, no glow, no breathing).
// - Cooled, hovering shows the dim flame and the pill reads "Turn on" (owner: "the pill must then change to turn on, to provide
//   the user an option to turn Ignis back on for that chat"): the line and the flame light again and keep-warm resumes.
// - No tooltip at all (owner: "tooltip looks bad, remove it"). The row's context menu offers the same item (keyboard access).
// - Reduce Motion: every change is instant.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Port.Ignis.Views;

/// <summary>
/// What a sidebar row shows about Ignis for its chat, whatever the look: lit, cooled or nothing; until when Ignis keeps it warm
/// (its idle limit; null with no limit or when not lit); what Ignis saved in it today.
/// </summary>
public sealed record IgnisRowWarmth(IgnisEmber Ember, DateTimeOffset? WarmUntil, decimal SavedToday)
{
    public static IgnisRowWarmth None { get; } = new(IgnisEmber.None, null, 0m);

    /// <summary>How long it stays warm from <paramref name="now"/>; null with no limit.</summary>
    public TimeSpan? Left(DateTimeOffset now) => WarmUntil is { } until && until > now ? until - now : null;

    public static IgnisRowWarmth From(IIgnisController controller, Guid thread)
    {
        var ember = controller.Ember(thread).Ember;
        if (ember == IgnisEmber.None) return None;
        var status = controller.Status(thread);
        return new IgnisRowWarmth(ember, ember == IgnisEmber.Lit ? status.IdleLimitAt : null, status.SavedToday);
    }
}

/// <summary>One row's <see cref="IgnisRowWarmth"/>, kept current from the controller while its view is on screen.</summary>
public sealed class IgnisRowWarmthSource : MainActorModel
{
    private readonly IIgnisController controller;
    private IgnisRowWarmth value;

    public IgnisRowWarmthSource(IIgnisController controller, Guid thread)
    {
        this.controller = controller;
        Thread = thread;
        value = IgnisRowWarmth.From(controller, thread);
    }

    public Guid Thread { get; }

    /// <summary>The row's warmth now (observed).</summary>
    public IgnisRowWarmth Value { get => Read(value); private set => Set(ref this.value, value); }

    /// <summary>Follows the controller (the view came on screen) and reads it again.</summary>
    public void Attach()
    {
        controller.Changed -= OnChanged;
        controller.Changed += OnChanged;
        Refresh();
    }

    /// <summary>Stops following the controller (the view left the screen).</summary>
    public void Detach() => controller.Changed -= OnChanged;

    /// <summary>Reads the controller again.</summary>
    public void Refresh()
    {
        var next = IgnisRowWarmth.From(controller, Thread);
        if (next != value) Value = next;
    }

    /// <summary>"Turn off" on a lit chat: Ignis stops keeping it warm until "Turn on" or its next message.</summary>
    public void TurnOff()
    {
        controller.LetItCool(Thread);
        Refresh();
    }

    /// <summary>"Turn on" on a cooled chat: Ignis keeps it warm again.</summary>
    public void TurnOn()
    {
        controller.KeepWarmAgain(Thread);
        Refresh();
    }

    private void OnChanged(Guid changed)
    {
        if (changed != Guid.Empty && changed != Thread) return;
        if (MainActor.IsIsolated) Refresh();
        else MainActor.Post(Refresh);
    }
}

/// <summary>Where the sidebar asks for a row's mark.</summary>
public static class IgnisSidebar
{
    /// <summary>The row's keep-warm mark when Ignis is bound; null keeps the row exactly as it was.</summary>
    public static IgnisSidebarMark? MarkFor(Guid thread, Action? select = null) =>
        IgnisSeams.Controller.Current() is { } controller ? new IgnisSidebarMark(new IgnisRowWarmthSource(controller, thread), select) : null;
}

/// <summary>The sidebar row's keep-warm mark (see the file's head). It spans the row; only its zone and its pill take the pointer.</summary>
public sealed class IgnisSidebarMark : Panel
{
    /// <summary>The ember line: 2.5 pt wide, 2 pt corner, 7 pt in from the top and the bottom, 2 pt from the leading edge.</summary>
    public const double LineWidth = 2.5, LineRadius = 2, LineInset = 7, LineLead = 2;

    /// <summary>The line's breath: opacity .8 to 1 and back over 2.6 s.</summary>
    public const double BreathLow = .8, BreathSeconds = 2.6;

    /// <summary>A cooled line's opacity (no glow, no breath).</summary>
    public const double CooledLineOpacity = .5;

    /// <summary>The hover zone's width (lead: about 32 pt, the row's full height).</summary>
    public const double ZoneWidth = 32;

    /// <summary>The flame glyph's box (the mock's 14 pt), centred in the zone.</summary>
    public const double GlyphSize = 14;

    /// <summary>The pill: 20 pt high, 9 pt in, 11.5 pt words, its right edge 2 pt into the zone, 6 pt of slide.</summary>
    public const double PillHeight = 20, PillPadding = 9, PillFontSize = 11.5, PillOverlap = 2, PillTravel = 6;

    /// <summary>Hover in: the pill slides out and the flame fades in over this (0.18-0.22 s, ease-out); leaving reverses it.</summary>
    public const double RevealSeconds = .2;

    /// <summary>The shown flame's brightness (the mock's brightness(1.14); owner: +10-15 %).</summary>
    public const double HoverBrightness = 1.14;

    /// <summary>"Turn off": the flame and the line go out over this (the mock's 0.42 s).</summary>
    public const double DouseSeconds = .42;

    /// <summary>"Turn on" or the next message: they light again over this.</summary>
    public const double RelightSeconds = .25;

    /// <summary>A cooled flame's opacity (the mock's .55).</summary>
    public const double CooledFlameOpacity = .55;

    /// <summary>The zone and the pill are one hover region: leaving it waits this long before the pill goes (about 120 ms).</summary>
    public static readonly TimeSpan LeaveGrace = TimeSpan.FromMilliseconds(120);

    private readonly EmberLine line = new();
    private readonly Panel cluster;
    private readonly Border zone;
    private readonly EmberGlyph glyph = new();
    private readonly Border pillFace;
    private readonly TextBlock words;
    private readonly Button pill;
    private readonly TranslateTransform pillShift = new();
    private readonly AnimatedScalar reveal, brightness, warmth, douse;
    private IgnisEmber shown = IgnisEmber.None;
    private bool overZone, overPill, pressed;
    private TaskHandle? grace;

    /// <param name="select">A click on the zone (not the pill) selects the row, as a click anywhere else on it does.</param>
    public IgnisSidebarMark(IgnisRowWarmthSource source, Action? select = null)
    {
        Source = source;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        line.Width = LineWidth;
        line.UseLayoutRounding = false; // 2.5 pt exactly, never rounded to 3
        line.HorizontalAlignment = HorizontalAlignment.Left;
        line.VerticalAlignment = VerticalAlignment.Stretch;
        line.Margin = new Thickness(LineLead, LineInset, 0, LineInset);
        Children.Add(line);

        glyph.Width = glyph.Height = GlyphSize;
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        // The zone: transparent (it takes the pointer) and never drawn: no fill, border, highlight or circle, ever (owner).
        zone = new Border { Width = ZoneWidth, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch, Child = glyph };

        words = new TextBlock { Text = IgnisWords.TurnOff, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(PillFontSize, SwiftFont.Weight.Medium).Apply(words);
        pillFace = new Border
        {
            Height = PillHeight, Padding = new Thickness(PillPadding, 0), CornerRadius = new CornerRadius(PillHeight / 2), BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 2 8 0 #1F000000"), Child = words,
        };
        pill = PlainControls.Button(pillFace, Act).KeyboardFocus();
        pill.HorizontalAlignment = HorizontalAlignment.Right;
        pill.VerticalAlignment = VerticalAlignment.Center;
        pill.Margin = new Thickness(0, 0, ZoneWidth - PillOverlap, 0);
        pill.RenderTransform = pillShift;
        pill.Opacity = 0;
        pill.IsHitTestVisible = false;
        pill.SizeChanged += (_, _) => TitleFadeChanged?.Invoke();
        cluster = new Panel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch, Children = { zone, pill } };
        Children.Add(cluster);
        void Paint()
        {
            words.Foreground = Chrome.PrimaryText.Brush(words);
            pillFace.Background = (pill.IsPointerOver ? PillHover : PillFill).Brush(pillFace);
            pillFace.BorderBrush = PillEdge.Brush(pillFace);
        }
        pill.PropertyChanged += (_, e) => { if (e.Property == InputElement.IsPointerOverProperty) Paint(); };
        ActualThemeVariantChanged += (_, _) => { Paint(); glyph.InvalidateVisual(); line.InvalidateVisual(); };
        Paint();

        reveal = new AnimatedScalar(this, 0, value =>
        {
            pill.Opacity = value;
            pillShift.X = (1 - value) * PillTravel;
            pill.IsHitTestVisible = value > .01;
            glyph.Opacity = value;
            TitleFadeChanged?.Invoke();
        });
        brightness = new AnimatedScalar(this, 1, value => { glyph.Brightness = value; glyph.InvalidateVisual(); });
        warmth = new AnimatedScalar(this, 0, value => { glyph.Warmth = value; line.SetWarmth(value); glyph.InvalidateVisual(); });
        douse = new AnimatedScalar(this, 0, value => { glyph.Douse = value; glyph.InvalidateVisual(); });

        zone.PointerEntered += (_, _) => { overZone = true; HoverChanged(); };
        zone.PointerExited += (_, _) => { overZone = false; pressed = false; HoverChanged(); };
        pill.PointerEntered += (_, _) => { overPill = true; HoverChanged(); };
        pill.PointerExited += (_, _) => { overPill = false; HoverChanged(); };
        zone.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(zone).Properties.IsLeftButtonPressed) return;
            pressed = true;
            e.Handled = true;
        };
        zone.PointerReleased += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            e.Handled = true;
            select?.Invoke(); // the flame itself never cools the chat (owner)
        };
        AttachedToVisualTree += (_, _) => Source.Attach();
        DetachedFromVisualTree += (_, _) => { Source.Detach(); grace?.Cancel(); grace = null; overZone = overPill = false; };

        this.Track(() => Source.Value.Ember, Show);
    }

    public IgnisRowWarmthSource Source { get; }

    /// <summary>Whether the row shows the mark now (lit or cooled).</summary>
    public bool Shows => Source.Value.Ember != IgnisEmber.None;

    /// <summary>The pill's and the row menu's item now: "Turn off" (lit), "Turn on" (cooled), none.</summary>
    public string? ActionTitle => Source.Value.Ember switch
    {
        IgnisEmber.Lit => IgnisWords.TurnOff,
        IgnisEmber.Cooled => IgnisWords.TurnOn,
        _ => null,
    };

    /// <summary>Whether the pointer is over the zone or the pill (or was, within the grace).</summary>
    public bool IsHot { get; private set; }

    /// <summary>The pill's and the flame's reveal, 0 hidden to 1 shown.</summary>
    public double Reveal => reveal.Value;

    /// <summary>The flame's brightness now (1 hidden, <see cref="HoverBrightness"/> shown).</summary>
    public double FlameBrightness => brightness.Value;

    /// <summary>The line's and the flame's warmth now (1 lit, 0 cooled).</summary>
    public double Warmth => warmth.Value;

    /// <summary>How far the extinguish has run (0 to 1).</summary>
    public double DouseProgress => douse.Value;

    /// <summary>The line's opacity at a warmth (1 lit, 0 cooled) and a point of its breath (<paramref name="seconds"/> on the ambient clock).</summary>
    public static double LineOpacityAt(double warmth, double seconds) => EmberLine.OpacityAt(warmth, seconds, still: false);

    internal Control Zone => zone;
    internal Button Pill => pill;
    internal string PillWords => words.Text ?? "";
    internal Control Glyph => glyph;
    internal AmbientCanvasLine Line => line;

    /// <summary>Raised whenever the pill moves or resizes, so the row can mask its title's end under it (<see cref="TitleFade"/>).</summary>
    public event Action? TitleFadeChanged;

    /// <summary>
    /// The title's mask under the pill, in points from the row's trailing edge: opaque beyond <c>From</c>, faded by
    /// <c>Amount</c> (0 none, 1 gone) from <c>To</c> to the trailing edge (under the pill and the flame).
    /// </summary>
    public (double From, double To, double Amount) TitleFade
    {
        get
        {
            var pillLeft = cluster.Margin.Right + ZoneWidth - PillOverlap + pill.Bounds.Width;
            return (pillLeft + 30, pillLeft - 4, reveal.Value);
        }
    }

    /// <summary>Where the zone's right edge sits, in points from the row's trailing edge (just before the row's trailing content, as it glides).</summary>
    public void Place(double trailing)
    {
        if (cluster.Margin.Right == trailing) return;
        cluster.Margin = new Thickness(0, 0, trailing, 0);
        TitleFadeChanged?.Invoke();
    }

    /// <summary>The pill or the row menu's item: "Turn off" while lit, "Turn on" once cooled.</summary>
    public void Act()
    {
        switch (Source.Value.Ember)
        {
            case IgnisEmber.Lit: Source.TurnOff(); break;
            case IgnisEmber.Cooled: Source.TurnOn(); break;
        }
    }

    /// <summary>The pointer entered or left the zone or the pill (tests drive it the same way).</summary>
    internal void SetPointer(bool zoneOver, bool pillOver)
    {
        overZone = zoneOver;
        overPill = pillOver;
        HoverChanged();
    }

    private void HoverChanged()
    {
        if (overZone || overPill)
        {
            grace?.Cancel();
            grace = null;
            SetHot(true);
            return;
        }
        if (!IsHot || grace is not null) return;
        grace = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(LeaveGrace);
            if (SwiftTask.IsCancelled) return;
            grace = null;
            if (!overZone && !overPill) SetHot(false);
        });
    }

    private void SetHot(bool hot)
    {
        IsHot = hot;
        var shows = hot && shown != IgnisEmber.None;
        var motion = KindleMotion.Ease(this, Animation.EaseOut(RevealSeconds));
        reveal.Set(shows ? 1 : 0, motion);
        brightness.Set(shows && shown == IgnisEmber.Lit ? HoverBrightness : 1, motion);
    }

    private void Show(IgnisEmber ember)
    {
        var before = shown;
        shown = ember;
        IsVisible = ember != IgnisEmber.None;
        words.Text = ember == IgnisEmber.Cooled ? IgnisWords.TurnOn : IgnisWords.TurnOff;
        AutomationProperties.SetName(pill, words.Text);
        AutomationProperties.SetName(line, ember == IgnisEmber.Cooled ? IgnisWords.EmberCooledAccessibleName : IgnisWords.EmberLitAccessibleName);
        if (ember == IgnisEmber.None) { reveal.Set(0, null); brightness.Set(1, null); }
        else if (IsHot) SetHot(true);
        switch (ember)
        {
            case IgnisEmber.Lit:
                douse.Set(0, null);
                warmth.Set(1, before == IgnisEmber.Cooled ? KindleMotion.Ease(this, Animation.EaseOut(RelightSeconds)) : null);
                break;
            case IgnisEmber.Cooled when before == IgnisEmber.Lit:
                var motion = KindleMotion.Ease(this, Animation.TimingCurve(.3, 0, .2, 1, duration: DouseSeconds));
                douse.Set(0, null);
                douse.Set(1, motion);
                warmth.Set(0, motion);
                break;
            default:
                douse.Set(0, null);
                warmth.Set(0, null);
                break;
        }
    }

    // The mock's glass pill (--pill, --pill-edge, --pill-hover).
    private static readonly SwiftColor PillFill = SwiftColor.Dynamic(new SwiftColor(1, .86), new SwiftColor(52 / 255.0, 57 / 255.0, 70 / 255.0, .92));
    private static readonly SwiftColor PillHover = SwiftColor.Dynamic(new SwiftColor(1, 1), new SwiftColor(66 / 255.0, 72 / 255.0, 88 / 255.0, .96));
    private static readonly SwiftColor PillEdge = SwiftColor.Dynamic(new SwiftColor(24 / 255.0, 34 / 255.0, 64 / 255.0, .12), new SwiftColor(1, .14));

    /// <summary>The ember line (the base the look draws it with, kept apart so a test can read it).</summary>
    internal abstract class AmbientCanvasLine : AmbientCanvas
    {
        public double LineWarmth { get; protected set; }
    }

    /// <summary>
    /// The ember line at the row's leading edge: the ember gradient with a soft glow, breathing on the paced ambient clock while
    /// lit; cooled, the cold grey at <see cref="CooledLineOpacity"/>, still and without glow. Its warmth crossfades the two.
    /// </summary>
    private sealed class EmberLine : AmbientCanvasLine
    {
        public void SetWarmth(double value)
        {
            var moving = Moving;
            LineWarmth = value;
            if (moving != Moving) MovingChanged();
            else InvalidateVisual();
        }

        protected override bool Moving => LineWarmth > .001;

        protected override object Snapshot() => (KindlePalette.For(this), LineWarmth);

        /// <summary>The line's opacity at <paramref name="seconds"/>: lit, .8 to 1 and back over 2.6 s (eased); cooled, .5.</summary>
        public static double OpacityAt(double warmth, double seconds, bool still)
        {
            var p = seconds / BreathSeconds % 1;
            var half = p < .5 ? p * 2 : (1 - p) * 2;
            var breath = still ? 1 : BreathLow + (1 - BreathLow) * (half * half * (3 - 2 * half));
            var w = Math.Clamp(warmth, 0, 1);
            return CooledLineOpacity + (breath - CooledLineOpacity) * w;
        }

        protected override void Draw(SKCanvas canvas, Size size, double seconds, bool still, object snapshot)
        {
            var (palette, w) = ((KindlePalette, double))snapshot;
            w = Math.Clamp(w, 0, 1);
            var rect = new SKRoundRect(new SKRect(0, 0, (float)size.Width, (float)size.Height), (float)LineRadius);
            using var layer = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(255 * OpacityAt(w, seconds, still))) };
            var saved = canvas.SaveLayer(layer);
            if (w > .001)
            {
                using var glow = new SKPaint { IsAntialias = true, Color = KindlePalette.Sk(palette.EmberGlow, w), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f) };
                canvas.DrawRoundRect(rect, glow);
            }
            var top = KindlePalette.Mix(palette.EmberCold, palette.EmberCore, w);
            var bottom = KindlePalette.Mix(palette.EmberCold, palette.EmberOuter, w);
            using var fill = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, (float)size.Height), [KindlePalette.Sk(top), KindlePalette.Sk(bottom)], SKShaderTileMode.Clamp),
            };
            canvas.DrawRoundRect(rect, fill);
            canvas.RestoreToCount(saved);
        }
    }

    /// <summary>
    /// The flame (the Kindle's glyph and colours, KindleFlame): its warmth fades the ember's colour and glow over the cold grey,
    /// its opacity falls to <see cref="CooledFlameOpacity"/>, and the extinguish squashes it toward its base and back.
    /// </summary>
    internal sealed class EmberGlyph : Control
    {
        public EmberGlyph() => IsHitTestVisible = false;

        public double Warmth { get; set; }
        public double Brightness { get; set; } = 1;
        public double Douse { get; set; }

        /// <summary>The squash at a point of the extinguish: in to 45 %, back out by the end (the mock's keyframes).</summary>
        public static (double ScaleX, double ScaleY, double Down) Squash(double douse)
        {
            var d = Math.Clamp(douse, 0, 1);
            var s = d < .45 ? d / .45 : (1 - d) / .55;
            s = s * s * (3 - 2 * s);
            return (1 - .08 * s, 1 - .38 * s, 1.5 * s);
        }

        public double DrawnOpacity => CooledFlameOpacity + (1 - CooledFlameOpacity) * Math.Clamp(Warmth, 0, 1);

        public override void Render(DrawingContext context) =>
            context.Custom(new Operation(new Rect(Bounds.Size), KindlePalette.For(this), Warmth, Brightness, Douse, DrawnOpacity));

        private sealed class Operation(Rect bounds, KindlePalette palette, double warmth, double brightness, double douse, double opacity) : ICustomDrawOperation
        {
            public Rect Bounds => bounds.Inflate(6);
            public bool HitTest(Point point) => false;
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }

            public void Render(ImmediateDrawingContext context)
            {
                if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
                using var lease = feature.Lease();
                var canvas = lease.SkCanvas;
                var (sx, sy, down) = Squash(douse);
                var w = (float)bounds.Width;
                var h = (float)bounds.Height;
                using var layer = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(255 * Math.Clamp(opacity, 0, 1))) };
                var saved = canvas.SaveLayer(layer);
                canvas.Translate(w / 2, h + (float)down);
                canvas.Scale((float)sx, (float)sy);
                canvas.Translate(-w / 2, -h);
                KindleFlame.Draw(canvas, new SKRect(0, 0, w, h), palette, Math.Clamp(warmth, 0, 1), brightness);
                canvas.RestoreToCount(saved);
            }
        }
    }
}

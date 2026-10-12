// PORT-ONLY: Tactus pill v2, the capsule (OD-0145, ED-NEW-pcctl-24; DESIGN.md 4.5, 10.1, 10.3 "What the capsule shows",
// "Drag", 10.5, 10.6 "Accessibility"; canvas pc-control-ui pill v2, `.pv`). Left to right: the Tactus mark in its 32 DIP
// slot (with the count badge), the live glyph, the two lines of text, the state's button group, and Stop in its own slot.
// Under it, the hint row and the per-app rows. Every change is a pose: text lines slide out and in under their own slot's
// clip, button groups do the same under the group's clip, the dot, the pause bars, the badge and Stop swap by scale, the
// rows and the hint unroll from under their top edge and roll back, the pill arrives and leaves by scale (the owner's
// no-fades rule, ED-NEW-pcctl-21 item 3); Reduce Motion turns each into a 150 ms fade and stops all breathing. A part that
// has left is hidden once its leave is over (not drawn, not in the accessibility tree), so nothing hidden rests on a
// composition-only value. The capsule's width, Stop's key-cap fold, travels, the lift and the scales go through the motion
// driver: laid out once at their end, then on composition key frames and the render-thread glass. A press on a button
// never becomes a drag; a press elsewhere drags past the threshold, lifts the pill, and its release hands the host a
// velocity to choose an anchor.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Marrow.Core.Port.PcControl.Pill;
using Path = Avalonia.Controls.Shapes.Path;

namespace Marrow.UI.Port.PcControl;

/// <summary>What a press on the pill asks for.</summary>
public enum PillCommandKind
{
    PauseAll,
    ResumeAll,
    Stop,
    PauseApp,
    ResumeApp,
    StopApp,
    StopThisApp,
    DoItNow,
    NotNow,
    PutBack,
    Settings,
    /// <summary>A click on the pill, not on a button: the card when asks wait, else the rows.</summary>
    Click,
    Later,
    PreviousAsk,
    NextAsk,
}

public readonly record struct PillCommand(PillCommandKind Kind, string? App = null);

public sealed class ControlPill : Canvas, IPillTargets
{
    /// <summary>The rows and hint start this far below the pill's top (canvas `.pvb{top:55px}`), 440 DIP wide.</summary>
    public const double BelowTop = 55;

    public const double BelowWidth = 440;

    /// <summary>The hint row's height and the gap to the rows (canvas `.phint`, `.pvb{gap:7px}`).</summary>
    public const double HintHeight = 30;

    public const double BelowGap = 7;

    /// <summary>The hint's and the rows' clips reach past their panels for the panels' outer shadow (0 16 40 -8): sides and bottom.</summary>
    public const double BelowBleedX = 32;

    public const double BelowBleedBottom = 48;

    /// <summary>The text swap: 150 ms on the canvas's ease, cubic-bezier(.32, .72, 0, 1) (DESIGN.md 10.3).</summary>
    public const double TextSwapSeconds = 0.15;

    /// <summary>The dot and the pause bars swap on a spring of response 0.18 s (DESIGN.md 10.1).</summary>
    public const double GlyphResponse = 0.18;

    /// <summary>The badge and Stop scale in and out on the arrive spring.</summary>
    public const double BadgeResponse = PillMotion.ArriveResponse;

    /// <summary>The group slot's width (groups sit at its right edge, under its clip).</summary>
    public const double GroupBox = 320;

    /// <summary>The text column's layout width (its visible width is the driver's clip).</summary>
    public const double TextBox = 640;

    /// <summary>Stop's content clip's layout width (its visible width is the driver's clip, so its layout never changes).</summary>
    public const double StopBox = 640;

    /// <summary>A part is hidden this long after its leave ends (the compositor's last frame of it is drawn first).</summary>
    public const double HideMargin = 0.05;

    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI, fonts:Inter#Inter");
    public static readonly FontFamily MonoFont = new("fonts:CascadiaMono#Cascadia Mono");

    internal static readonly IBrush Text1 = new ImmutableSolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEE));
    internal static readonly IBrush Text2 = new ImmutableSolidColorBrush(Color.FromArgb(0xA8, 0xEC, 0xEC, 0xEE));
    internal static readonly IBrush Ink = new ImmutableSolidColorBrush(Color.FromRgb(0x12, 0x12, 0x14));
    internal static readonly IBrush SilverHigh = new ImmutableSolidColorBrush(Color.FromRgb(0xF6, 0xF6, 0xF8));
    internal static readonly IBrush Silver = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Color.FromRgb(0xF6, 0xF6, 0xF8)), new ImmutableGradientStop(1, Color.FromRgb(0xCD, 0xCD, 0xD2))],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));

    private static readonly IBrush Dense = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Color.FromArgb(0xFF, 0x30, 0x30, 0x34)), new ImmutableGradientStop(1, Color.FromArgb(0xFF, 0x1D, 0x1D, 0x21))],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative), opacity: 0.86);

    private static readonly IBrush KeyFill = new ImmutableSolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush KeyInk = new ImmutableSolidColorBrush(Color.FromArgb(0xD1, 0xF2, 0xF2, 0xF3));
    private static readonly BoxShadows KeyRing = BoxShadows.Parse("inset 0 0 0 0.5 #29FFFFFF");

    private static readonly BoxShadows DenseRim = BoxShadows.Parse("inset 0 0.75 0 0 #66FFFFFF, inset 0 -0.5 0 0 #1AFFFFFF, inset 0 0 0 0.5 #24FFFFFF, 0 1 2 0 #73000000, 0 16 40 -8 #80000000");

    private readonly TimeProvider clock;
    private readonly IPillAnimator animator;
    private readonly PillMotionDriver driver;
    private readonly Canvas stage = new();
    private readonly Border hitArea = new() { Background = Brushes.Transparent };
    private readonly Canvas contentLayer = new();
    private readonly Canvas row = new() { IsHitTestVisible = false };
    private readonly TactusMark mark = new() { GlyphSize = 24, InSlot = true, Width = TactusMark.SlotSize, Height = TactusMark.SlotSize };
    private readonly Border badge;
    private readonly TextBlock badgeText;
    private readonly Ellipse dot;
    private readonly StackPanel bars;
    private readonly Canvas textClip = new() { ClipToBounds = true, Width = TextBox, Height = PillMotionDriver.TextClipHeight };
    private readonly PillLineSlot title;
    private readonly PillLineSlot line;
    private readonly Canvas group = new() { ClipToBounds = true, Width = GroupBox, Height = PillLayout.Height };
    private readonly Dictionary<PillGroupKind, Control> groups = [];
    private readonly Canvas stopSlot = new();
    private readonly Canvas stopClip = new() { ClipToBounds = true, Width = StopBox, Height = PillSurfacePainter.StopHeight, IsHitTestVisible = false };
    private readonly StackPanel stopContent;
    private readonly TextBlock capText;
    private readonly Border cap;
    private readonly PillButton stopButton;
    private readonly Canvas below = new();
    private readonly Canvas hintClip = new() { ClipToBounds = true, Width = BelowWidth + (2 * BelowBleedX), Height = HintHeight + BelowBleedBottom };
    private readonly Border hint;
    private readonly StackPanel hintText;
    private IReadOnlyList<PillHintPart> hintParts = [];
    private readonly Canvas rowsClip = new() { ClipToBounds = true, Width = BelowWidth + (2 * BelowBleedX) };
    private readonly Border rows;
    private readonly StackPanel rowList = new() { Spacing = 2 };
    private readonly LiveStatusText live = new();
    private readonly LiveStatusText alert = new();
    private readonly TextBlock measure = new() { FontFamily = TextFont };
    private readonly PillVelocityEstimator velocity = new();
    private readonly Dictionary<Control, int> hides = [];

    private PillGroupKind shownGroup = (PillGroupKind)(-1);
    private PillGlyph shownGlyph = PillGlyph.None;
    private int shownBadge;
    private bool stopShown;
    private bool hintOpen;
    private bool rowsOpen;
    private bool reduceMotion;
    private double rowsHeight;
    private PillParts parts;
    private PillPress? press;
    private bool dragging;
    private PillVector pressAnchor;
    private PillVector anchorShift;
    private PillVector lastPoint;
    private PillVector layoutAnchor;
    private PillVector origin;

    /// <param name="follower">
    /// A mirror (the shadow band's copy): it shows the same content, takes no input, and takes its motion from its leader
    /// through <see cref="Follow"/> instead of making its own.
    /// </param>
    public ControlPill(PillDriverKind? driverKind = null, IPillAnimator? animator = null, TimeProvider? clock = null, bool follower = false)
    {
        Follower = follower;
        this.clock = clock ?? TimeProvider.System;
        this.animator = animator ?? CompositionPillAnimator.Instance;
        var kind = driverKind ?? PillDriverSwitch.Current();
        Body = new PillSurface(PillSurfaceKind.Body, kind);
        StopSurface = new PillSurface(PillSurfaceKind.Stop, kind);
        driver = new PillMotionDriver(this, this.animator);
        ClipToBounds = false;

        badgeText = new TextBlock { FontFamily = TextFont, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        badge = new Border
        {
            MinWidth = 18, Height = 18, Padding = new Thickness(5, 0), CornerRadius = new CornerRadius(9), Background = Silver,
            BoxShadow = BoxShadows.Parse("0 0 0 1.5 #FF121214"), Child = badgeText, IsVisible = false,
        };
        dot = new Ellipse { Width = 6, Height = 6, Fill = SilverHigh, IsVisible = false, Effect = new DropShadowEffect { OffsetX = 0, OffsetY = 0, BlurRadius = 6, Color = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF) } };
        bars = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2, Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false,
            Children = { Bar(), Bar() },
        };
        static Border Bar() => new() { Width = 2, Height = 8, CornerRadius = new CornerRadius(1), Background = SilverHigh, VerticalAlignment = VerticalAlignment.Center };
        title = new PillLineSlot(this.animator, After, 18, 13.5, FontWeight.SemiBold, Text1, TextSwapSeconds);
        line = new PillLineSlot(this.animator, After, 17, 13, FontWeight.Normal, Text2, TextSwapSeconds);

        foreach (var part in new Control[] { mark, badge, dot, bars }) AutomationProperties.SetAccessibilityView(part, AccessibilityView.Raw);
        Place(row, mark, 0, (PillLayout.Height - TactusMark.SlotSize) / 2);
        Place(row, dot, 40 + 2, (PillLayout.Height / 2) - 3);
        Place(row, bars, 40, (PillLayout.Height / 2) - 5);
        Place(row, badge, TactusMark.SlotSize + 7 - 18, ((PillLayout.Height - TactusMark.SlotSize) / 2) - 5);
        Place(textClip, title, 0, PillMotionDriver.TextClipBleed);
        Place(textClip, line, 0, PillMotionDriver.TextClipBleed + 18);
        Place(row, textClip, PillPlaces.TextStart - 8, ((PillLayout.Height - 35) / 2) - PillMotionDriver.TextClipBleed);

        capText = new TextBlock { FontFamily = MonoFont, FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = new ImmutableSolidColorBrush(Color.FromArgb(0xB8, 0x12, 0x12, 0x14)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        cap = new Border
        {
            MinWidth = 84, Padding = new Thickness(6, 5), CornerRadius = new CornerRadius(6), Background = new ImmutableSolidColorBrush(Color.FromArgb(0x14, 0, 0, 0)),
            BoxShadow = BoxShadows.Parse("inset 0 0 0 1 #29000000, inset 0 -1 0 0 #1F000000"), Child = capText, VerticalAlignment = VerticalAlignment.Center,
        };
        stopContent = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 7, Height = PillSurfacePainter.StopHeight, Margin = new Thickness(12, 0, 5, 0),
            Children =
            {
                PillIcons.Stop(15, Ink),
                new TextBlock { Text = "Stop", FontFamily = TextFont, FontSize = 13.5, FontWeight = FontWeight.Bold, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center },
                cap,
            },
        };
        AutomationProperties.SetAccessibilityView(stopClip, AccessibilityView.Raw);
        stopClip.Children.Add(stopContent);
        stopButton = new PillButton(PillButtonLook.Clear) { Height = PillSurfacePainter.StopHeight };
        stopButton.Click += (_, _) => Raise(PillCommandKind.Stop);
        stopButton.PointerEntered += (_, _) => StopLook();
        stopButton.PointerExited += (_, _) => StopLook();
        stopButton.FocusRingChanged += StopLook;
        // Stop's capsule, its content and its hit area share the slot, so they move and scale as one
        stopSlot.Children.Add(StopSurface);
        stopSlot.Children.Add(stopClip);
        stopSlot.Children.Add(stopButton);
        stopSlot.IsVisible = false;
        stopButton.IsEnabled = false;

        hintText = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        hint = new Border { Width = BelowWidth, Height = HintHeight, CornerRadius = new CornerRadius(15), Padding = new Thickness(12, 0), Background = Dense, BoxShadow = DenseRim, Child = hintText, IsVisible = false };
        rows = new Border { Width = BelowWidth, CornerRadius = new CornerRadius(18), Padding = new Thickness(6), Background = Dense, BoxShadow = DenseRim, Child = rowList, IsVisible = false };
        Place(hintClip, hint, BelowBleedX, 0);
        Place(rowsClip, rows, BelowBleedX, 0);
        Place(below, hintClip, 0, 0);
        Place(below, rowsClip, 0, HintHeight + BelowGap);

        live.Width = alert.Width = 1;
        live.Height = alert.Height = 1;
        live.Foreground = alert.Foreground = Brushes.Transparent;
        AutomationProperties.SetLiveSetting(alert, AutomationLiveSetting.Assertive);

        contentLayer.Children.Add(row);
        contentLayer.Children.Add(group);
        contentLayer.Children.Add(stopSlot);
        // the rows and hint first, so the pill is always drawn over them (canvas order); then the body, then its content
        stage.Children.Add(below);
        stage.Children.Add(Body);
        stage.Children.Add(hitArea);
        stage.Children.Add(contentLayer);
        Children.Add(stage);
        Children.Add(live);
        Children.Add(alert);

        hitArea.PointerPressed += Pressed;
        hitArea.PointerMoved += Moved;
        hitArea.PointerReleased += Released;
        hitArea.PointerCaptureLost += CaptureLost;
        AutomationProperties.SetAccessibilityView(hitArea, AccessibilityView.Raw);
        AutomationProperties.SetName(this, "Tactus");
        if (follower)
        {
            // the mirror never takes input and is not in the accessibility tree (its band is click-through and Raw)
            IsHitTestVisible = false;
            AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        }

        Motion = PillMotion.Hidden(new PillVector(0, PillAnchors.TopGap), 0.5, PillLayout.MinWidth, PillLayout.Height, Now);
        Layout(Motion.Final, PillPlaces.Of(Motion.Final, parts));
    }

    // ---- IPillTargets ----
    public Control Stage => stage;

    public Control Content => contentLayer;

    public Control Below => below;

    public Control Row => row;

    public Control Group => group;

    public Control StopSlot => stopSlot;

    public Control StopClip => stopClip;

    public Control TextClip => textClip;

    public PillSurface Body { get; }

    public PillSurface StopSurface { get; }

    /// <summary>The pill's motion now (its pose at any time, the shape the overlays read), in the monitor's DIP.</summary>
    public PillMotion Motion { get; private set; }

    /// <summary>True for a mirror that follows its leader's motion.</summary>
    public bool Follower { get; }

    /// <summary>The band's top-left in the monitor's DIP: motions are in the monitor's DIP, the controls in the band's.</summary>
    public PillVector Origin
    {
        get => origin;
        set
        {
            if (origin == value) return;
            origin = value;
            driver.Run(Motion, parts, Now);
        }
    }

    /// <summary>What the pill shows now.</summary>
    public PillView? View { get; private set; }

    /// <summary>The measured content (group, Stop, cap) the places follow.</summary>
    public PillParts Parts => parts;

    /// <summary>The last fit of the content (the width and the key cap's fold it asked for).</summary>
    public PillLayoutResult? Fitted { get; private set; }

    /// <summary>The group showing now (None while none does).</summary>
    public PillGroupKind ShownGroup => shownGroup;

    /// <summary>The button group of a kind, once it was first shown (tests and the host's focus moves).</summary>
    public Control? GroupOf(PillGroupKind kind) => groups.TryGetValue(kind, out var control) ? control : null;

    /// <summary>The two text lines (tests read their current and previous text).</summary>
    internal PillLineSlot TitleSlot => title;

    internal PillLineSlot LineSlot => line;

    internal PillButton StopButton => stopButton;

    internal Control Hint => hint;

    internal Control RowsPanel => rows;

    internal Control Dot => dot;

    internal Control Bars => bars;

    internal Control BadgePart => badge;

    internal Border HitArea => hitArea;

    /// <summary>Reduce Motion: every pose change becomes a 150 ms fade, and nothing breathes.</summary>
    public bool ReduceMotion
    {
        get => reduceMotion;
        set
        {
            if (reduceMotion == value) return;
            reduceMotion = value;
            if (!value) return;
            animator.StopBreathing(dot);
            animator.StopBreathing(bars);
        }
    }

    /// <summary>The pill's own clock, seconds (the one every curve runs on).</summary>
    public double Now => clock.GetTimestamp() / (double)clock.TimestampFrequency;

    /// <summary>A command from a press on the pill.</summary>
    public event Action<PillCommand>? Command;

    /// <summary>A new motion started (or a pose was set): the host publishes its shape and keeps the glass on it.</summary>
    public event Action<PillMotion>? Moving;

    /// <summary>A drag frame: the pose was set at once (the mirror follows it the same way).</summary>
    public event Action<PillMotion>? Dragged;

    /// <summary>A drag began.</summary>
    public event Action? DragStarted;

    /// <summary>A drag ended: the release point (band DIP), the anchor point there, and the release velocity (DIP/s).</summary>
    public event Action<PillVector, PillVector, PillVector>? DragEnded;

    /// <summary>Which layers this window draws (the mirror in the shadow band draws the shadow, or everything while moving).</summary>
    public PillPaint Paint
    {
        get => Body.Look.Paint;
        set
        {
            Body.Restyle(Body.Look with { Paint = value });
            StopSurface.Restyle(StopSurface.Look with { Paint = value });
            var content = value.HasFlag(PillPaint.Content);
            contentLayer.IsVisible = below.IsVisible = content;
            hitArea.IsHitTestVisible = content;
        }
    }

    /// <summary>The blur is under the body (else the dense panel, ED-238).</summary>
    public bool Glass
    {
        get => Body.Look.Glass;
        set => Body.Restyle(Body.Look with { Glass = value });
    }

    public bool HighContrast
    {
        get => Body.Look.HighContrast;
        set
        {
            Body.Restyle(Body.Look with { HighContrast = value });
            StopSurface.Restyle(StopSurface.Look with { HighContrast = value });
        }
    }

    // ---- what it shows ----

    /// <summary>Shows <paramref name="view"/>: text, glyph, group, badge, hint and rows, then the width and fold it needs.</summary>
    public void Show(PillView view, double? at = null)
    {
        var now = at ?? Now;
        var instant = View is null || Motion.Presence == PillPresence.Gone;
        var reduce = ReduceMotion;
        View = view;
        title.Set(view.Title, instant, reduce, now);
        line.Set(view.Line, instant, reduce, now);
        Glyph(view.Glyph, instant, reduce, now);
        mark.State = view.Mark;
        Badge(view.Badge, instant, reduce, now);
        GroupTo(view, instant, reduce, now);
        AutomationProperties.SetName(stopButton, view.StopName);
        AutomationProperties.SetHelpText(stopButton, Chord(view.StopName));
        capText.Text = string.Join(' ', Chord(view.StopName).Split('+'));
        HintTo(view);
        Rows(view.Rows);
        Fold(ref hintOpen, hint, view.HintShown, HintHeight, instant, reduce, now);
        Fold(ref rowsOpen, rows, view.RowsShown, rowsHeight, instant, reduce, now);
        if (AutomationProperties.GetName(live) != view.Announcement)
        {
            AutomationProperties.SetName(live, view.Announcement);
            live.Text = view.Announcement;
        }
        var measured = parts;
        Fit(view);
        // the same size and parts as the motion already goes to: nothing to move (a view refreshed with no change)
        var unchanged = !instant && parts == measured && Math.Abs(Motion.Final.Width - Fitted!.Width) < 1e-9 && Math.Abs(Motion.Final.Cap - (Fitted.CapFolded ? 1 : 0)) < 1e-9;
        var next = unchanged ? Motion : Motion.Widen(now, Fitted!.Width, reduce || instant).Fold(now, Fitted.CapFolded, reduce || instant);
        if (instant) next = PillMotion.Still(next.Final, now);
        var stop = PillPlaces.Of(next.Final, parts).StopVisible;
        stopButton.IsEnabled = view.ShowsStop;
        Swap(stopSlot, view.ShowsStop, stopShown, instant, reduce, now, BadgeResponse, new Point(stop / 2, PillSurfacePainter.StopHeight / 2));
        stopShown = view.ShowsStop;
        if (unchanged) return;
        if (Follower) driver.Run(Motion, parts, now);
        else Run(next, now);
    }

    /// <summary>The mirror takes its leader's motion (<paramref name="held"/>: a drag frame, set at once).</summary>
    public void Follow(PillMotion motion, bool held, double? at = null)
    {
        var now = at ?? Now;
        if (held)
        {
            Motion = motion;
            var pose = motion.At(now);
            animator.SetTranslation(stage, pose.Anchor.X - layoutAnchor.X + pose.ScaleOffset.X, pose.Anchor.Y - layoutAnchor.Y + pose.ScaleOffset.Y);
            return;
        }
        Motion = motion;
        driver.Run(motion, parts, now);
    }

    /// <summary>
    /// The motion is over (the host's settle timer): the pill holds its final pose as a still motion, so its presence reads
    /// Shown or Gone. Nothing moves or is laid out again (the last layout is the final one).
    /// </summary>
    public void Settled(double? at = null)
    {
        var now = at ?? Now;
        if (Motion.Done(now) && !Motion.AtRest) Motion = PillMotion.Still(Motion.Final, now);
    }

    /// <summary>Shows the pill at full presence at once, where it is (the first frames' warm-up; the host's tests).</summary>
    public void Present(double? at = null)
    {
        var now = at ?? Now;
        Run(PillMotion.Still(Motion.Final with { Presence = 1, Opacity = 1 }, now), now);
    }

    /// <summary>Raises the assertive notification a new ask carries ("Tactus needs your OK for Outlook").</summary>
    public void Alert(string text)
    {
        AutomationProperties.SetName(alert, text);
        alert.Text = text;
    }

    // ---- where it is ----

    /// <summary>Puts the pill at <paramref name="anchor"/> (side <paramref name="side"/>) at once: a first show, a new monitor before it arrives.</summary>
    public void Place(PillVector anchor, double side, double? at = null)
    {
        var now = at ?? Now;
        Run(PillMotion.Still(Motion.At(now) with { Anchor = anchor, Factor = side }, now), now);
    }

    /// <summary>A travel of the anchor to <paramref name="anchor"/> on side <paramref name="side"/>, keeping the pill's speed (a dodge, a release, a return).</summary>
    public void TravelTo(PillVector anchor, double side, PillVector? speed = null, double? at = null)
    {
        var now = at ?? Now;
        var motion = Math.Abs(side - Motion.Factor) > 1e-9 ? Motion.Reanchored(now, side) : Motion;
        Run(motion.TravelTo(now, anchor, ReduceMotion, speed), now);
    }

    /// <summary>Grows out of its anchor (Reduce Motion: a 150 ms fade at full size).</summary>
    public void Arrive(double? at = null)
    {
        var now = at ?? Now;
        Run(Motion.Arrive(now, ReduceMotion), now);
    }

    /// <summary>Shrinks into its anchor (Reduce Motion: a 150 ms fade at full size).</summary>
    public void Leave(double? at = null)
    {
        var now = at ?? Now;
        Run(Motion.Leave(now, ReduceMotion), now);
    }

    /// <summary>A DPI change in place: the scale springs from the old physical size.</summary>
    public void Rescaled(double oldOverNew, double? at = null)
    {
        var now = at ?? Now;
        Run(Motion.Rescaled(now, oldOverNew, ReduceMotion), now);
    }

    private void Run(PillMotion motion, double now)
    {
        Motion = motion;
        driver.Run(motion, parts, now);
        Moving?.Invoke(motion);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // New element visuals start from the controls' layout: put the composition-only values (the clips' widths, the
        // stage's place and scale, the content's opacity) back before the first frame. The children attach after this.
        Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(this) is not null) driver.Run(Motion, parts, Now);
        }, DispatcherPriority.Send);
    }

    // ---- the drag ----

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || press is not null) return;
        var now = Now;
        // a press in flight grabs the pill where it is drawn now (DESIGN.md 10.3 "Grab in flight")
        if (!Motion.AtRest) Run(Motion.Frozen(now), now);
        var at = Vector(point.Position);
        var kind = e.Pointer.Type switch { PointerType.Pen => PillPointerKind.Pen, PointerType.Touch => PillPointerKind.Touch, _ => PillPointerKind.Mouse };
        pressAnchor = Motion.At(now).Anchor;
        press = new PillPress(at, pressAnchor, onButton: false, kind);
        dragging = false;
        lastPoint = at;
        velocity.Clear();
        velocity.Add(e.Timestamp, at);
        e.Pointer.Capture(hitArea);
        e.Handled = true;
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        if (press is null) return;
        var at = Vector(e.GetPosition(this));
        lastPoint = at;
        velocity.Add(e.Timestamp, at);
        if (press.Move(at) is not { } target) return;
        var now = Now;
        if (!dragging)
        {
            // the drag holds the pill by its centre (the release picks the side again) and lifts it
            dragging = true;
            var centred = Motion.Reanchored(now, 0.5);
            anchorShift = centred.At(now).Anchor - Motion.At(now).Anchor;
            Run(centred.Lifted(now, true, ReduceMotion), now);
            DragStarted?.Invoke();
        }
        // 1:1 sideways; the rubber band down (120 DIP) and up (8 DIP) from the row
        var offset = target - pressAnchor;
        Hold(new PillVector(pressAnchor.X + anchorShift.X + offset.X, pressAnchor.Y + anchorShift.Y + PillRubberBand.Vertical(offset.Y)), now);
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (press is not { } p) return;
        press = null;
        e.Pointer.Capture(null);
        var outcome = p.Release(stillOnButton: false);
        if (outcome == PillPressOutcome.DragEnd) EndDrag(Vector(e.GetPosition(this)), velocity.Velocity(e.Timestamp));
        else if (outcome == PillPressOutcome.Click) Raise(PillCommandKind.Click);
        e.Handled = true;
    }

    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (press is null) return;
        press = null;
        if (dragging) EndDrag(lastPoint, PillVector.Zero);
    }

    private void EndDrag(PillVector at, PillVector speed)
    {
        dragging = false;
        var now = Now;
        Run(Motion.Lifted(now, false, ReduceMotion), now);
        DragEnded?.Invoke(at, Motion.At(now).Anchor, speed);
    }

    // A drag frame: the anchor where the pointer puts it, applied at once (the lift keeps its spring on the stage).
    private void Hold(PillVector anchor, double now)
    {
        Motion = Motion.Held(now, anchor);
        var pose = Motion.At(now);
        animator.SetTranslation(stage, anchor.X - layoutAnchor.X + pose.ScaleOffset.X, anchor.Y - layoutAnchor.Y + pose.ScaleOffset.Y);
        Dragged?.Invoke(Motion);
    }

    // ---- IPillTargets.Layout: the final places, once per change ----
    public void Layout(PillPose pose, PillPlaces places)
    {
        layoutAnchor = pose.Anchor;
        SetLeft(stage, pose.Anchor.X - origin.X);
        SetTop(stage, pose.Anchor.Y - origin.Y);
        SetLeft(hitArea, pose.LocalLeft);
        SetTop(hitArea, 0);
        hitArea.Width = pose.Width;
        hitArea.Height = pose.Height;
        SetLeft(row, places.RowLeft);
        SetLeft(group, places.GroupLeft + parts.GroupWidth - GroupBox);
        SetLeft(stopSlot, places.StopLeft);
        SetTop(stopSlot, (PillLayout.Height - PillSurfacePainter.StopHeight) / 2);
        stopButton.Width = Math.Max(0, places.StopVisible);
        title.SetWidth(Math.Max(0, places.TextClip));
        line.SetWidth(Math.Max(0, places.TextClip));
        SetLeft(below, (-pose.Factor * BelowWidth) - BelowBleedX);
        SetTop(below, BelowTop);
    }

    // ---- parts ----

    // The content's sizes: the group, Stop (always measured, so it can leave where it was) and its key cap; then the fit.
    private void Fit(PillView view)
    {
        var groupControl = groups.TryGetValue(view.Group, out var g) ? g : null;
        groupControl?.Measure(Size.Infinity);
        stopContent.Measure(Size.Infinity);
        var stopFull = stopContent.DesiredSize.Width;
        var capWidth = cap.DesiredSize.Width + stopContent.Spacing;
        var groupWidth = groupControl?.DesiredSize.Width ?? 0;
        parts = new PillParts(groupWidth, stopFull, capWidth, view.ShowsStop);
        Fitted = PillLayout.Compute(new PillLayoutInput(TextWidth(view.Title, 13.5, FontWeight.SemiBold), TextWidth(view.Line, 13, FontWeight.Normal), groupWidth, stopFull, capWidth, view.ShowsStop, view.Working, view.Ask));
    }

    private double TextWidth(string text, double size, FontWeight weight)
    {
        measure.Text = text;
        measure.FontSize = size;
        measure.FontWeight = weight;
        measure.Measure(Size.Infinity);
        return measure.DesiredSize.Width;
    }

    private void Glyph(PillGlyph next, bool instant, bool reduce, double now)
    {
        if (next == shownGlyph && !instant) return;
        var dotOn = next == PillGlyph.Live;
        var barsOn = next is PillGlyph.Pause or PillGlyph.Pausing;
        Swap(dot, dotOn, shownGlyph == PillGlyph.Live, instant, reduce, now, GlyphResponse, new Point(3, 3));
        Swap(bars, barsOn, shownGlyph is PillGlyph.Pause or PillGlyph.Pausing, instant, reduce, now, GlyphResponse, new Point(5, 5));
        // the dot breathes while it shows; the bars breathe while pausing; Reduce Motion: nothing breathes
        if (!reduce)
        {
            if (dotOn) animator.Breathe(dot, 0.45, 2.2);
            else animator.StopBreathing(dot);
            if (next == PillGlyph.Pausing) animator.Breathe(bars, 0.5, 1.0);
            else animator.StopBreathing(bars);
        }
        shownGlyph = next;
    }

    private void Badge(int count, bool instant, bool reduce, double now)
    {
        badgeText.Text = count > 0 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        badge.Measure(Size.Infinity);
        Swap(badge, count > 0, shownBadge > 0, instant, reduce, now, BadgeResponse, new Point(badge.DesiredSize.Width / 2, badge.DesiredSize.Height / 2));
        shownBadge = count;
    }

    // A scale swap about centre (a pose; Reduce Motion: a 150 ms fade at full size). A part that leaves is hidden after.
    private void Swap(Control part, bool on, bool wasOn, bool instant, bool reduce, double now, double response, Point centre)
    {
        animator.SetCentre(part, centre.X, centre.Y);
        if (on) Reveal(part);
        if (instant)
        {
            animator.SetOpacity(part, 1);
            animator.SetScale(part, 1);
            if (!on) Conceal(part);
            return;
        }
        if (on == wasOn) return;
        if (reduce)
        {
            animator.SetScale(part, 1);
            animator.Fade(part, Faded(now, on));
            if (!on) ConcealAfter(part, PillCurve.ReducedFade);
            return;
        }
        animator.SetOpacity(part, 1);
        var curve = PillCurve.Spring(on ? 0 : 1, on ? 1 : 0, 0, response, now, cap: 0.001);
        var end = SpringEnd(curve, now);
        animator.Scale(part, PillTrack.Of(now, end, t => curve.Done(t) ? curve.To : Math.Max(0, curve.Value(t))));
        if (!on) ConcealAfter(part, end - now);
    }

    private static PillTrack Faded(double now, bool on) =>
        PillTrack.Of(now, now + PillCurve.ReducedFade, t => Ease(on ? 0 : 1, on ? 1 : 0, now, t, PillCurve.ReducedFade));

    private static double SpringEnd(PillCurve curve, double now)
    {
        var end = now;
        while (!curve.Done(end) && end < now + PillMotion.LongestSeconds) end += PillTrack.Step;
        return end;
    }

    private static double Ease(double from, double to, double start, double t, double duration) =>
        from + ((to - from) * PillEase.Standard(Math.Clamp((t - start) / duration, 0, 1)));

    private void GroupTo(PillView view, bool instant, bool reduce, double now)
    {
        var next = view.Group;
        var control = GroupFor(next, view);
        if (next == shownGroup && !instant) return;
        var old = next != shownGroup && groups.TryGetValue(shownGroup, out var o) ? o : null;
        shownGroup = next;
        if (old is not null) GroupLeave(old, instant, reduce, now);
        if (control is not null) GroupEnter(control, instant, reduce, now);
    }

    // The group's slide up and out under its clip (150 ms on the canvas's ease); Reduce Motion: a 150 ms fade in place.
    private void GroupLeave(Control part, bool instant, bool reduce, double now)
    {
        part.IsEnabled = false;
        if (instant)
        {
            Conceal(part);
            return;
        }
        if (reduce)
        {
            animator.SetTranslation(part, 0, 0);
            animator.Fade(part, Faded(now, on: false));
            ConcealAfter(part, PillCurve.ReducedFade);
            return;
        }
        animator.SetOpacity(part, 1);
        animator.Translate(part, PillTrack.Of(now, now + TextSwapSeconds, _ => 0), PillTrack.Of(now, now + TextSwapSeconds, t => -PillLayout.Height * Swept(now, t)));
        ConcealAfter(part, TextSwapSeconds);
    }

    // The group's slide in from below; Reduce Motion: a 150 ms fade in place.
    private void GroupEnter(Control part, bool instant, bool reduce, double now)
    {
        part.Measure(Size.Infinity);
        SetLeft(part, GroupBox - part.DesiredSize.Width);
        SetTop(part, (PillLayout.Height - part.DesiredSize.Height) / 2);
        part.IsEnabled = true;
        Reveal(part);
        if (instant)
        {
            animator.SetTranslation(part, 0, 0);
            animator.SetOpacity(part, 1);
            return;
        }
        if (reduce)
        {
            animator.SetTranslation(part, 0, 0);
            animator.Fade(part, Faded(now, on: true));
            return;
        }
        animator.SetOpacity(part, 1);
        animator.Translate(part, PillTrack.Of(now, now + TextSwapSeconds, _ => 0), PillTrack.Of(now, now + TextSwapSeconds, t => PillLayout.Height * (1 - Swept(now, t))));
    }

    private static double Swept(double start, double t) => PillEase.CubicBezier(0.32, 0.72, 0, 1, Math.Clamp((t - start) / TextSwapSeconds, 0, 1));

    private Control? GroupFor(PillGroupKind kind, PillView view)
    {
        if (kind is PillGroupKind.None or PillGroupKind.StopOnly) return null;
        if (groups.TryGetValue(kind, out var existing))
        {
            Rename(kind, existing, view);
            return existing;
        }
        var made = kind switch
        {
            PillGroupKind.Run => Buttons(new PillButton(PillButtonLook.Icon28) { Content = PillIcons.Pause(12, Text1) }.On(PillCommandKind.PauseAll, this)),
            PillGroupKind.Paused => Buttons(Labelled(PillButtonLook.Primary, PillIcons.Play(12, Text1), "Resume").On(PillCommandKind.ResumeAll, this)),
            PillGroupKind.PausingStuck => Buttons(
                Labelled(PillButtonLook.Primary, PillIcons.Play(12, Text1), "Resume").On(PillCommandKind.ResumeAll, this),
                Labelled(PillButtonLook.Plain, null, "Stop this app").On(PillCommandKind.StopThisApp, this)),
            PillGroupKind.HandsOff => Buttons(
                Labelled(PillButtonLook.Primary, null, "Do it now").On(PillCommandKind.DoItNow, this),
                Labelled(PillButtonLook.Plain, null, "Not now").On(PillCommandKind.NotNow, this)),
            PillGroupKind.NotNow => Buttons(Labelled(PillButtonLook.Plain, null, "Not now").On(PillCommandKind.NotNow, this)),
            PillGroupKind.PutBack => Buttons(Labelled(PillButtonLook.Plain, null, "Put them back").On(PillCommandKind.PutBack, this)),
            PillGroupKind.Settings => Buttons(Labelled(PillButtonLook.Plain, null, "Settings").On(PillCommandKind.Settings, this)),
            PillGroupKind.Card => CardGroup(),
            _ => null,
        };
        if (made is null) return null;
        made.IsVisible = false;
        made.IsEnabled = false;
        groups[kind] = made;
        group.Children.Add(made);
        Rename(kind, made, view);
        return made;
    }

    private static void Rename(PillGroupKind kind, Control control, PillView view)
    {
        if (control is not StackPanel panel) return;
        var first = panel.Children.Count > 0 ? panel.Children[0] : null;
        switch (kind)
        {
            case PillGroupKind.Run when first is not null:
                AutomationProperties.SetName(first, view.PauseName);
                AutomationProperties.SetHelpText(first, Chord(view.PauseName));
                break;
            case PillGroupKind.Paused or PillGroupKind.PausingStuck when first is not null:
                AutomationProperties.SetName(first, view.ResumeName);
                AutomationProperties.SetHelpText(first, Chord(view.ResumeName));
                break;
            case PillGroupKind.Card:
                if (first is StackPanel pager)
                {
                    pager.IsVisible = view.Pager is not null;
                    if (pager.Children.Count > 1 && pager.Children[1] is TextBlock count) count.Text = view.Pager ?? "";
                }
                break;
        }
    }

    private StackPanel CardGroup()
    {
        var count = new TextBlock { FontFamily = MonoFont, FontSize = 11, FontWeight = FontWeight.Medium, Foreground = Text2, MinWidth = 40, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var pager = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Named(new PillButton(PillButtonLook.Pager) { Content = PillIcons.Back(11, Text2) }.On(PillCommandKind.PreviousAsk, this), "Previous ask"),
                count,
                Named(new PillButton(PillButtonLook.Pager) { Content = PillIcons.Chevron(11, Text2) }.On(PillCommandKind.NextAsk, this), "Next ask"),
            },
        };
        var later = Labelled(PillButtonLook.Later, PillIcons.Up(14, Text1), "Later").On(PillCommandKind.Later, this);
        AutomationProperties.SetName(later, "Later, Esc");
        var x = Named(new PillButton(PillButtonLook.Round30) { Content = PillIcons.Close(13, Text1) }.On(PillCommandKind.StopApp, this), "Stop");
        return Buttons(pager, later, x);
    }

    private static T Named<T>(T control, string name) where T : Control
    {
        AutomationProperties.SetName(control, name);
        return control;
    }

    private static StackPanel Buttons(params Control?[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in buttons)
            if (b is not null) panel.Children.Add(b);
        return panel;
    }

    private static PillButton Labelled(PillButtonLook look, Control? icon, string label)
    {
        var text = new TextBlock { Text = label, FontFamily = TextFont, FontSize = look == PillButtonLook.Later ? 12.5 : 13.5, FontWeight = look == PillButtonLook.Later ? FontWeight.Medium : FontWeight.SemiBold, Foreground = Text1, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = look == PillButtonLook.Later ? 2 : 6, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null) content.Children.Add(icon);
        content.Children.Add(text);
        var button = new PillButton(look) { Content = content };
        AutomationProperties.SetName(button, label);
        return button;
    }

    private void Rows(IReadOnlyList<PillRowView> next)
    {
        // one row per app, rebuilt only when the apps change; their lines and buttons follow the view
        var keys = next.Select(r => r.Key).ToList();
        var same = rowList.Children.Count == next.Count && rowList.Children.Select(c => c.Tag as string).SequenceEqual(keys);
        if (!same)
        {
            rowList.Children.Clear();
            for (var i = 0; i < next.Count; i++) rowList.Children.Add(new PillRow(this, next[i].Key, i > 0));
        }
        for (var i = 0; i < next.Count; i++) ((PillRow)rowList.Children[i]).Show(next[i]);
        rows.Measure(new Size(BelowWidth, double.PositiveInfinity));
        rowsHeight = rows.DesiredSize.Height;
        rowsClip.Height = rowsHeight + BelowBleedBottom;
    }

    // The rows and the hint unroll from under their top edge and roll back (a pose; Reduce Motion: a 150 ms fade).
    private void Fold(ref bool open, Control part, bool next, double height, bool instant, bool reduce, double now)
    {
        part.IsHitTestVisible = next;
        if (open == next && !instant) return;
        open = next;
        var hidden = -(height + 8);
        if (next) Reveal(part);
        if (instant)
        {
            animator.SetOpacity(part, 1);
            animator.SetTranslation(part, 0, next ? 0 : hidden);
            if (!next) Conceal(part);
            return;
        }
        if (reduce)
        {
            animator.SetTranslation(part, 0, 0);
            animator.Fade(part, Faded(now, next));
            if (!next) ConcealAfter(part, PillCurve.ReducedFade);
            return;
        }
        animator.SetOpacity(part, 1);
        var curve = PillCurve.Spring(next ? hidden : 0, next ? 0 : hidden, 0, next ? PillCurve.ExpandResponse : PillCurve.CollapseResponse, now);
        var end = SpringEnd(curve, now);
        animator.Translate(part, PillTrack.Of(now, end, _ => 0), PillTrack.Of(now, end, t => curve.Done(t) ? curve.To : curve.Value(t)));
        if (!next) ConcealAfter(part, end - now);
    }

    // The hint row's pieces (canvas `.phint`): text, key chips (`kbd`) and the separating dot; its accessible text is the sentence.
    private void HintTo(PillView view)
    {
        AutomationProperties.SetName(hint, view.Hint);
        var next = view.HintPieces;
        if (next.SequenceEqual(hintParts)) return;
        hintParts = next;
        hintText.Children.Clear();
        foreach (var part in next) hintText.Children.Add(HintPiece(part));
    }

    private static Control HintPiece(PillHintPart part)
    {
        Control piece = part.Kind switch
        {
            PillHintPartKind.Key => new Border
            {
                // canvas `kbd`: mono 500 10.5, padding 2 5 in the hint, radius 5, .08 white, a .5 inset ring of .16 white
                Padding = new Thickness(5, 2), CornerRadius = new CornerRadius(5), Background = KeyFill, BoxShadow = KeyRing,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = part.Text, FontFamily = MonoFont, FontSize = 10.5, FontWeight = FontWeight.Medium, LineHeight = 10.5, Foreground = KeyInk },
            },
            PillHintPartKind.Dot => new TextBlock { Text = part.Text, FontFamily = TextFont, FontSize = 12.5, LineHeight = 20, Foreground = Text2, Opacity = 0.45, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center },
            _ => new TextBlock { Text = part.Text, FontFamily = TextFont, FontSize = 12.5, LineHeight = 20, Foreground = Text2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
        };
        AutomationProperties.SetAccessibilityView(piece, AccessibilityView.Raw);
        return piece;
    }

    // ---- hiding what has left ----

    private int Generation(Control part) => hides[part] = (hides.TryGetValue(part, out var g) ? g : 0) + 1;

    private void Reveal(Control part)
    {
        Generation(part);
        part.IsVisible = true;
    }

    private void Conceal(Control part)
    {
        Generation(part);
        part.IsVisible = false;
    }

    // Hides part once its leave is over, unless it was shown again before.
    private void ConcealAfter(Control part, double seconds)
    {
        var generation = Generation(part);
        After(seconds + HideMargin, () =>
        {
            if (hides.TryGetValue(part, out var g) && g == generation) part.IsVisible = false;
        });
    }

    // Runs action on the UI thread after seconds on the pill's clock.
    private void After(double seconds, Action action)
    {
        ITimer? timer = null;
        timer = clock.CreateTimer(_ =>
        {
            timer?.Dispose();
            Dispatcher.UIThread.Post(action);
        }, null, TimeSpan.FromSeconds(Math.Max(0, seconds)), Timeout.InfiniteTimeSpan);
    }

    internal void Raise(PillCommandKind kind, string? app = null) => Command?.Invoke(new PillCommand(kind, app));

    private void StopLook() => StopSurface.Restyle(StopSurface.Look with { Hover = stopButton.IsPointerOver, Focused = stopButton.FocusRing });

    // The chord after a name's comma ("Stop all, Ctrl+Alt+Esc" gives "Ctrl+Alt+Esc").
    private static string Chord(string name)
    {
        var comma = name.IndexOf(", ", StringComparison.Ordinal);
        return comma < 0 ? "" : name[(comma + 2)..];
    }

    // A point in the band (this control's coordinates) in the monitor's DIP.
    private PillVector Vector(Point p) => new(p.X + origin.X, p.Y + origin.Y);

    private static void Place(Panel host, Control child, double x, double y)
    {
        SetLeft(child, x);
        SetTop(child, y);
        host.Children.Add(child);
    }

    /// <summary>One app's row (canvas `.prw`): the name, its line, pause or resume, and its stop (x).</summary>
    private sealed class PillRow : Border
    {
        private readonly TextBlock name = new() { FontFamily = TextFont, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Text1, Width = 56, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock text = new() { FontFamily = TextFont, FontSize = 13, Foreground = Text2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock behind = new() { Text = "working behind", FontFamily = TextFont, FontSize = 11.5, Foreground = Text2, VerticalAlignment = VerticalAlignment.Center };
        private readonly PillButton pause;
        private readonly PillButton stop;
        private readonly ContentControl pauseIcon = new();
        private bool paused;

        public PillRow(ControlPill owner, string key, bool rule)
        {
            Tag = key;
            Height = 42;
            CornerRadius = new CornerRadius(12);
            Padding = new Thickness(10, 0, 5, 0);
            if (rule) BoxShadow = BoxShadows.Parse("0 -1 0 0 #0FFFFFFF");
            pause = new PillButton(PillButtonLook.Round30) { Content = pauseIcon };
            pause.Click += (_, _) => owner.Raise(paused ? PillCommandKind.ResumeApp : PillCommandKind.PauseApp, key);
            stop = new PillButton(PillButtonLook.Round30) { Content = PillIcons.Close(13, Text1) };
            stop.Click += (_, _) => owner.Raise(PillCommandKind.StopApp, key);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 10 };
            grid.Children.Add(name);
            Grid.SetColumn(behind, 1);
            grid.Children.Add(behind);
            Grid.SetColumn(text, 2);
            grid.Children.Add(text);
            Grid.SetColumn(pause, 3);
            grid.Children.Add(pause);
            Grid.SetColumn(stop, 4);
            grid.Children.Add(stop);
            Child = grid;
        }

        public void Show(PillRowView view)
        {
            name.Text = view.Name;
            text.Text = view.Line;
            behind.IsVisible = view.Behind;
            name.Opacity = text.Opacity = view.Paused ? 0.55 : 1;
            if (paused != view.Paused || pauseIcon.Content is null) pauseIcon.Content = view.Paused ? PillIcons.Play(12, Text1) : PillIcons.Pause(12, Text1);
            paused = view.Paused;
            pause.IsVisible = stop.IsVisible = !view.Off;
            AutomationProperties.SetName(pause, view.PauseName);
            AutomationProperties.SetName(stop, view.StopName);
        }
    }
}

/// <summary>One of the two text lines: two blocks under the slot's clip, the old one slides up and out as the new one slides in.</summary>
internal sealed class PillLineSlot : Canvas
{
    private readonly IPillAnimator animator;
    private readonly Action<double, Action> after;
    private readonly double height;
    private readonly double seconds;
    private TextBlock current;
    private TextBlock previous;
    private int generation;

    public PillLineSlot(IPillAnimator animator, Action<double, Action> after, double height, double size, FontWeight weight, IBrush brush, double seconds)
    {
        this.animator = animator;
        this.after = after;
        this.height = height;
        this.seconds = seconds;
        Height = height;
        ClipToBounds = true;
        TextBlock Block() => new()
        {
            FontFamily = ControlPill.TextFont, FontSize = size, FontWeight = weight, Foreground = brush, LineHeight = height,
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, Height = height,
        };
        current = Block();
        previous = Block();
        Children.Add(previous);
        Children.Add(current);
        AutomationProperties.SetAccessibilityView(previous, AccessibilityView.Raw);
    }

    public string Text => current.Text ?? "";

    /// <summary>The line before the latest change, until its slide is over (tests).</summary>
    public string Previous => previous.Text ?? "";

    /// <summary>The slot's and both blocks' width (the text column's final clip).</summary>
    public void SetWidth(double width)
    {
        Width = width;
        current.Width = previous.Width = width;
    }

    public void Set(string text, bool instant, bool reduce, double now)
    {
        if (text == Text)
        {
            if (instant) Snap();
            return;
        }
        (previous, current) = (current, previous);
        current.Text = text;
        AutomationProperties.SetAccessibilityView(current, AccessibilityView.Content);
        AutomationProperties.SetAccessibilityView(previous, AccessibilityView.Raw);
        if (instant)
        {
            Snap();
            return;
        }
        var g = ++generation;
        after(seconds + ControlPill.HideMargin, () =>
        {
            if (g == generation) previous.Text = "";
        });
        if (reduce)
        {
            animator.SetTranslation(current, 0, 0);
            animator.SetTranslation(previous, 0, 0);
            var end = now + PillCurve.ReducedFade;
            animator.Fade(current, PillTrack.Of(now, end, t => PillEase.Standard(Math.Clamp((t - now) / PillCurve.ReducedFade, 0, 1))));
            animator.Fade(previous, PillTrack.Of(now, end, t => 1 - PillEase.Standard(Math.Clamp((t - now) / PillCurve.ReducedFade, 0, 1))));
            return;
        }
        animator.SetOpacity(current, 1);
        animator.SetOpacity(previous, 1);
        var x = PillTrack.Of(now, now + seconds, _ => 0);
        double Eased(double t) => PillEase.CubicBezier(0.32, 0.72, 0, 1, Math.Clamp((t - now) / seconds, 0, 1));
        animator.Translate(previous, x, PillTrack.Of(now, now + seconds, t => -height * Eased(t)));
        animator.Translate(current, x, PillTrack.Of(now, now + seconds, t => height * (1 - Eased(t))));
    }

    private void Snap()
    {
        ++generation;
        previous.Text = "";
        animator.SetTranslation(current, 0, 0);
        animator.SetOpacity(current, 1);
        animator.SetTranslation(previous, 0, 0);
        animator.SetOpacity(previous, 1);
    }
}

/// <summary>The pill's button looks (canvas `.pb`, `.pb.i28`, `.pb.dn`, `.pka`, `.pkl`, `.rx`, `.rq`, and Stop's clear hit area).</summary>
public enum PillButtonLook
{
    Plain,
    Primary,
    Icon28,
    Round30,
    Pager,
    Later,
    Clear,
}

/// <summary>
/// A pill button: a plain template (no theme chrome), the canvas's fill, inset rim and radius, a hover fill, a 0.96 press
/// scale, and a 2 DIP focus ring for keyboard focus only (:focus-visible). Presses never take the foreground (the overlay
/// is no-activate).
/// </summary>
public sealed class PillButton : Button
{
    private static readonly IBrush Ring = new ImmutableSolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
    private static readonly TransformOperations PressScale = TransformOperations.Parse("scale(0.96)");

    private readonly PillButtonLook look;
    private readonly Border ring = new() { BorderBrush = Ring, BorderThickness = new Thickness(2), IsHitTestVisible = false, IsVisible = false };
    private bool focusRing;

    public PillButton(PillButtonLook look)
    {
        this.look = look;
        Focusable = true;
        FocusAdorner = null;
        var (height, width, radius, padding) = look switch
        {
            PillButtonLook.Icon28 => (28.0, 28.0, 14.0, new Thickness(0)),
            PillButtonLook.Round30 => (30.0, 30.0, 15.0, new Thickness(0)),
            PillButtonLook.Pager => (18.0, 18.0, 9.0, new Thickness(0)),
            PillButtonLook.Later => (28.0, double.NaN, 14.0, new Thickness(6, 0, 10, 0)),
            PillButtonLook.Clear => (PillSurfacePainter.StopHeight, double.NaN, 18.0, new Thickness(0)),
            _ => (36.0, double.NaN, 18.0, new Thickness(14, 0)),
        };
        Height = height;
        if (!double.IsNaN(width)) Width = width;
        CornerRadius = new CornerRadius(radius);
        Padding = padding;
        Background = Rest;
        RenderTransformOrigin = RelativePoint.Center;
        RenderTransform = TransformOperations.Identity;
        Transitions = new Transitions
        {
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new SplineEasing(0.32, 0.72, 0, 1) },
        };
        ring.CornerRadius = new CornerRadius(radius + 4);
        ring.Margin = new Thickness(-4);
        AutomationProperties.SetAccessibilityView(ring, AccessibilityView.Raw);
        Template = new FuncControlTemplate<Button>((button, _) =>
        {
            var presenter = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                BoxShadow = Rim,
            };
            presenter.Bind(ContentPresenter.ContentProperty, button.GetObservable(ContentProperty));
            presenter.Bind(ContentPresenter.BackgroundProperty, button.GetObservable(BackgroundProperty));
            presenter.Bind(ContentPresenter.CornerRadiusProperty, button.GetObservable(CornerRadiusProperty));
            presenter.Bind(ContentPresenter.PaddingProperty, button.GetObservable(PaddingProperty));
            return new Panel { Children = { presenter, ring } };
        });
        PointerEntered += (_, _) => Background = Hover;
        PointerExited += (_, _) => Background = Rest;
        Classes.CollectionChanged += (_, _) => SetRing(IsEffectivelyEnabled && Classes.Contains(":focus-visible"));
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsEffectivelyEnabledProperty) SetRing(IsEffectivelyEnabled && Classes.Contains(":focus-visible"));
        };
    }

    /// <summary>The keyboard focus ring shows (tests; Stop draws its own on its capsule).</summary>
    public bool FocusRing => focusRing;

    public event Action? FocusRingChanged;

    public PillButton On(PillCommandKind kind, ControlPill pill)
    {
        Click += (_, _) => pill.Raise(kind);
        return this;
    }

    private IBrush Rest => look switch
    {
        PillButtonLook.Primary => new ImmutableSolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        PillButtonLook.Round30 => new ImmutableSolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
        PillButtonLook.Pager or PillButtonLook.Clear => Brushes.Transparent,
        _ => new ImmutableSolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
    };

    private IBrush Hover => look switch
    {
        PillButtonLook.Primary => new ImmutableSolidColorBrush(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF)),
        PillButtonLook.Clear => Brushes.Transparent,
        PillButtonLook.Pager => new ImmutableSolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
        _ => new ImmutableSolidColorBrush(Color.FromArgb(0x2B, 0xFF, 0xFF, 0xFF)),
    };

    private BoxShadows Rim => look switch
    {
        PillButtonLook.Primary => BoxShadows.Parse("inset 0 0.75 0 0 #66FFFFFF, inset 0 0 0 1 #5CFFFFFF"),
        PillButtonLook.Plain or PillButtonLook.Icon28 => BoxShadows.Parse("inset 0 0.5 0 0 #3DFFFFFF, inset 0 0 0 0.5 #1AFFFFFF"),
        _ => default,
    };

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        RenderTransform = PressScale;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        RenderTransform = TransformOperations.Identity;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        RenderTransform = TransformOperations.Identity;
    }

    private void SetRing(bool on)
    {
        if (focusRing == on) return;
        focusRing = on;
        ring.IsVisible = on && look != PillButtonLook.Clear;
        FocusRingChanged?.Invoke();
    }
}

/// <summary>The canvas's 20-unit icons (`#i-stop`, `#i-pause`, `#i-play`, `#i-x`, `#i-up`, `#i-back`, `#i-chev`), drawn at a size.</summary>
public static class PillIcons
{
    public static Control Stop(double size, IBrush brush) => Filled(size, brush, "M7.2,5 L12.8,5 A2.2,2.2 0 0 1 15,7.2 L15,12.8 A2.2,2.2 0 0 1 12.8,15 L7.2,15 A2.2,2.2 0 0 1 5,12.8 L5,7.2 A2.2,2.2 0 0 1 7.2,5 Z");

    public static Control Pause(double size, IBrush brush) => Filled(size, brush, "M6.7,4.5 L7.3,4.5 A1.2,1.2 0 0 1 8.5,5.7 L8.5,14.3 A1.2,1.2 0 0 1 7.3,15.5 L6.7,15.5 A1.2,1.2 0 0 1 5.5,14.3 L5.5,5.7 A1.2,1.2 0 0 1 6.7,4.5 Z M12.7,4.5 L13.3,4.5 A1.2,1.2 0 0 1 14.5,5.7 L14.5,14.3 A1.2,1.2 0 0 1 13.3,15.5 L12.7,15.5 A1.2,1.2 0 0 1 11.5,14.3 L11.5,5.7 A1.2,1.2 0 0 1 12.7,4.5 Z");

    public static Control Play(double size, IBrush brush) => Filled(size, brush, "M6.5,4.6 V15.4 C6.5,16 7.2,16.4 7.7,16 L15.7,10.6 A0.7,0.7 0 0 0 15.7,9.4 L7.7,4 C7.2,3.6 6.5,4 6.5,4.6 Z");

    public static Control Close(double size, IBrush brush) => Stroked(size, brush, 2, "M6,6 L14,14 M14,6 L6,14");

    public static Control Up(double size, IBrush brush) => Stroked(size, brush, 1.8, "M5,12.5 L10,7.5 L15,12.5");

    public static Control Back(double size, IBrush brush) => Stroked(size, brush, 2, "M12.5,4.5 L7,10 L12.5,15.5");

    public static Control Chevron(double size, IBrush brush) => Stroked(size, brush, 2, "M7.5,4.5 L13,10 L7.5,15.5");

    private static Control Filled(double size, IBrush brush, string data) => Icon(size, new Path { Data = Geometry.Parse(data), Fill = brush });

    private static Control Stroked(double size, IBrush brush, double width, string data) =>
        Icon(size, new Path { Data = Geometry.Parse(data), Stroke = brush, StrokeThickness = width, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round });

    private static Control Icon(double size, Path path)
    {
        var box = new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = new Canvas { Width = 20, Height = 20, Children = { path } }, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        AutomationProperties.SetAccessibilityView(box, AccessibilityView.Raw);
        return box;
    }
}

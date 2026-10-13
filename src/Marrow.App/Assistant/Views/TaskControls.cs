// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 12.6b "Layout, controls and access" and "Motion"; the canvas's .tst, .tb, .tctl, paintLbl,
// setK, work/design/assistant/index.html lines 273-302 and 1904-1948; ED-NEW-dextra-as8-4). No Swift twin.
// The parts every task row, head line and mission card share: the state glyph, the state label (read-only text with a glyph, a
// fixed 86 px so a change of state never moves the row; the new word rolls in on a spring, the outgoing word is hidden from
// assistive technology at once), and the two fixed control slots (Pause, Resume or Restart; Stop or Revert changes), whose icon
// morphs between pause and play on a spring and scales Restart and Revert in within the slot. A control with nothing to do is
// disabled and out of the tab order, never removed. On a pointer the slots are 28 px, 2 px apart; with touch 40 px, 8 px apart.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

/// <summary>The task lists' fixed measures (12.6b, r4 B13 and B14).</summary>
public static class TaskMetrics
{
    public const double LabelWidth = 86;
    public const double NarrowLabelWidth = 16;
    public const double LabelHeight = 18;

    /// <summary>Below this width the label is its glyph alone and titles take two lines (r4 B15).</summary>
    public const double NarrowWidth = 520;

    public const double PointerButton = 28;
    public const double TouchButton = 40;
    public const double PointerGap = 2;
    public const double TouchGap = 8;
    public const double Glyph = 14;
    public const double MarkSize = 20;

    /// <summary>The row's own trailing padding, which the heads panel and the ask keep, so heads line up with their task.</summary>
    public const double TrailingPadding = 6;

    /// <summary>The same below 520 px (the canvas's narrow .tkr and .tkh).</summary>
    public const double NarrowTrailingPadding = 4;

    public static readonly TimeSpan LabelRoll = TimeSpan.FromMilliseconds(520);
    public static readonly TimeSpan Morph = TimeSpan.FromMilliseconds(500);

    /// <summary>The canvas's spring curve, cubic-bezier(.2, 1.1, .3, 1).</summary>
    public static Easing Spring() => new SplineEasing(.2, 1.1, .3, 1);
}

/// <summary>
/// Whether the controls are for touch: matched by "(hover:none), (any-pointer:coarse)" on the canvas, here any touch digitizer on the
/// PC, including a touch laptop that also has a mouse. Tests override it.
/// </summary>
public static class TaskTouch
{
    public static Ambient<Func<bool>> Coarse { get; } = new(() => HasTouch);

    public static bool IsCoarse => Coarse.Current();

    private static bool HasTouch => OperatingSystem.IsWindows() && GetSystemMetrics(95) > 0; // SM_MAXIMUMTOUCHES

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}

/// <summary>One still glyph per state, the same in head chips, head lines and state labels (no blinking dots).</summary>
public sealed class StateGlyph : Control
{
    public static readonly StyledProperty<string> KeyProperty = AvaloniaProperty.Register<StateGlyph, string>(nameof(Key), "idle");

    static StateGlyph() => AffectsRender<StateGlyph>(KeyProperty);

    public StateGlyph()
    {
        Width = Height = 8;
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The canvas's state key: working, queued, planning, reviewing, done, paused, pausing, stopped, stopping, needs, blocked, failed.</summary>
    public string Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }

    public static string KeyOf(MissionState state) => state switch
    {
        MissionState.NeedsYou => "needs",
        _ => state.ToString().ToLowerInvariant(),
    };

    public static string KeyOf(HeadState state) => state.ToString().ToLowerInvariant();

    public override void Render(DrawingContext context)
    {
        var theme = ActualThemeVariant;
        var fg = Chrome.PrimaryText.Resolve(theme);
        var fg2 = Chrome.SecondaryText.Resolve(theme);
        var fg3 = Chrome.PrimaryText.Opacity(.5).Resolve(theme);
        var box = new Rect(0, 0, 8, 8);
        var centre = box.Center;
        switch (Key)
        {
            case "working" or "reviewing":
                context.DrawEllipse(new SolidColorBrush(fg), null, centre, 4, 4);
                break;
            case "queued" or "planning":
                context.DrawEllipse(null, new Pen(new SolidColorBrush(fg3), 1.25), centre, 3.375, 3.375);
                break;
            case "done":
            {
                var pen = new Pen(new SolidColorBrush(fg), 1.5) { LineCap = PenLineCap.Square };
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(1.2, 4.3), false);
                    c.LineTo(new Point(3.2, 6.3));
                    c.LineTo(new Point(6.9, 1.6));
                    c.EndFigure(false);
                }
                context.DrawGeometry(null, pen, g);
                break;
            }
            case "paused":
                context.FillRectangle(new SolidColorBrush(fg2), new Rect(0, 0, 3, 8));
                context.FillRectangle(new SolidColorBrush(fg2), new Rect(5, 0, 3, 8));
                break;
            case "pausing":
                context.FillRectangle(new SolidColorBrush(fg2), new Rect(0, 0, 1.25, 8));
                context.FillRectangle(new SolidColorBrush(fg2), new Rect(6.75, 0, 1.25, 8));
                break;
            case "stopped":
                context.DrawRectangle(new SolidColorBrush(fg2), null, box, 1.5, 1.5);
                break;
            case "stopping":
                context.DrawRectangle(null, new Pen(new SolidColorBrush(fg2), 1.25), box.Deflate(.625), 1.5, 1.5);
                break;
            case "needs":
            {
                context.DrawEllipse(new SolidColorBrush(fg), null, centre, 4, 4);
                // the exclamation cut out of the dot, in the surface colour (so the glyph is not colour alone)
                var cut = new SolidColorBrush(theme == Avalonia.Styling.ThemeVariant.Dark ? Color.FromRgb(23, 23, 27) : Color.FromRgb(248, 248, 250));
                context.FillRectangle(cut, new Rect(3.25, 1.5, 1.5, 2.9));
                context.FillRectangle(cut, new Rect(3.25, 5.2, 1.5, 1.3));
                break;
            }
            default:
                context.DrawEllipse(new SolidColorBrush(theme == Avalonia.Styling.ThemeVariant.Dark ? fg3 : fg3), null, centre, 4, 4);
                break;
        }
    }
}

/// <summary>
/// The state label: read-only text with a small glyph, never a pill or a button. A fixed width (86 px, or the 16 px glyph alone in a
/// narrow list), so a change of state never moves the row. A new word rolls up in on a spring (520 ms), the old one rolls up and out;
/// transform only, no fade and no change of width. The outgoing word is out of the accessibility tree at once.
/// </summary>
public sealed class StateLabel : Panel
{
    private string? key;
    private StateWord? current;
    private bool narrow;

    public StateLabel()
    {
        Width = TaskMetrics.LabelWidth;
        Height = TaskMetrics.LabelHeight;
        ClipToBounds = true;
        Focusable = false;
        IsTabStop = false;
        VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Text);
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Off);
    }

    /// <summary>The word shown now ("Paused").</summary>
    public string Text => current?.Word ?? "";

    /// <summary>The words in the label now, the outgoing ones first (tests: the outgoing word is out of the tree).</summary>
    internal IReadOnlyList<StateWord> Words => [.. Children.OfType<StateWord>()];

    /// <summary>Below 520 px the label is its glyph alone; the word stays its accessible name.</summary>
    public bool Narrow
    {
        get => narrow;
        set
        {
            if (narrow == value) return;
            narrow = value;
            Width = value ? TaskMetrics.NarrowLabelWidth : TaskMetrics.LabelWidth;
            foreach (var word in Children.OfType<StateWord>()) word.Narrow = value;
        }
    }

    public void Show(MissionState state) => Show(StateGlyph.KeyOf(state), MissionStates.Label(state), state is MissionState.NeedsYou or MissionState.Failed or MissionState.Blocked);

    public void Show(HeadState state) => Show(StateGlyph.KeyOf(state), MissionStates.Label(state), state == HeadState.Blocked);

    private void Show(string glyph, string word, bool strong)
    {
        if (key == glyph + word) return;
        var first = key is null;
        key = glyph + word;
        AutomationProperties.SetName(this, word);
        var next = new StateWord(word, glyph, strong) { Narrow = narrow };
        var old = current;
        current = next;
        // the outgoing word is gone for assistive technology at once, while it rolls out
        foreach (var leaving in Children.OfType<StateWord>()) AutomationProperties.SetAccessibilityView(leaving, AccessibilityView.Raw);
        Children.Add(next);
        var still = first || old is null || EnvironmentValues.GetAccessibilityReduceMotion(this) || Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;
        if (still)
        {
            foreach (var leaving in Children.OfType<StateWord>().Where(w => !ReferenceEquals(w, next)).ToList()) Children.Remove(leaving);
            return;
        }
        var shift = TaskMetrics.LabelHeight * 1.15;
        next.Roll.Y = shift;
        next.Animate(0);
        foreach (var leaving in Children.OfType<StateWord>().Where(w => !ReferenceEquals(w, next)).ToList())
        {
            leaving.Animate(-shift);
            DispatcherTimer.RunOnce(() => Children.Remove(leaving), TaskMetrics.LabelRoll);
        }
    }

    /// <summary>One word of the label: its glyph and its text, moved by a transform only.</summary>
    internal sealed class StateWord : StackPanel
    {
        private readonly TextBlock text;

        public StateWord(string word, string glyph, bool primary)
        {
            Word = word;
            Orientation = Orientation.Horizontal;
            Spacing = 6;
            VerticalAlignment = VerticalAlignment.Center;
            Height = TaskMetrics.LabelHeight;
            Roll = new TranslateTransform();
            RenderTransform = Roll;
            IsHitTestVisible = false;
            Children.Add(new StateGlyph { Key = glyph, VerticalAlignment = VerticalAlignment.Center });
            text = new TextBlock { Text = word, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
            SwiftFont.System(11.5, primary ? SwiftFont.Weight.Semibold : SwiftFont.Weight.Medium).Apply(text);
            text.Foreground = (primary ? Chrome.PrimaryText : Chrome.SecondaryText).Brush(text);
            AutomationProperties.SetAccessibilityView(text, AccessibilityView.Raw);
            Children.Add(text);
        }

        public string Word { get; }

        public TranslateTransform Roll { get; }

        public bool Narrow { set => text.IsVisible = !value; }

        internal bool Hidden => AutomationProperties.GetAccessibilityView(this) == AccessibilityView.Raw;

        public void Animate(double y)
        {
            Roll.Transitions ??= [new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TaskMetrics.LabelRoll, Easing = TaskMetrics.Spring() }];
            Roll.Y = y;
        }
    }
}

/// <summary>A slot's icon: Pause's two bars morph into Resume's play triangle; Restart and Revert scale in within the slot.</summary>
public sealed class SlotGlyph : Control
{
    /// <summary>0 shows the pause bars, 1 the play triangle (slot A); sprung.</summary>
    public static readonly StyledProperty<double> MorphProperty = AvaloniaProperty.Register<SlotGlyph, double>(nameof(Morph));

    /// <summary>1 shows the slot's second icon (Restart in slot A, Revert in slot B) and hides the first; sprung.</summary>
    public static readonly StyledProperty<double> AltProperty = AvaloniaProperty.Register<SlotGlyph, double>(nameof(Alt));

    private static readonly Geometry RestartIcon = Geometry.Parse("M5.2 12.5a6.8 6.8 0 1 0 2-5.3 M5 4.2v4h4");
    private static readonly Geometry RevertIcon = Geometry.Parse("M9 7L5 11l4 4 M5 11h9a5 5 0 0 1 0 10h-2");

    static SlotGlyph() => AffectsRender<SlotGlyph>(MorphProperty, AltProperty);

    public SlotGlyph(bool slotA)
    {
        SlotA = slotA;
        Width = Height = TaskMetrics.Glyph;
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    public bool SlotA { get; }

    public double Morph { get => GetValue(MorphProperty); set => SetValue(MorphProperty, value); }

    public double Alt { get => GetValue(AltProperty); set => SetValue(AltProperty, value); }

    public IBrush? Ink { get; set; }

    /// <summary>Moves to <paramref name="kind"/>: on a spring unless <paramref name="still"/>.</summary>
    public void Show(SlotKind kind, bool still)
    {
        var morph = kind is SlotKind.Resume or SlotKind.Restart ? 1.0 : 0.0;
        var alt = kind is SlotKind.Restart or SlotKind.Revert ? 1.0 : 0.0;
        if (still) Transitions = null;
        else Transitions ??=
        [
            new DoubleTransition { Property = MorphProperty, Duration = TaskMetrics.Morph, Easing = TaskMetrics.Spring() },
            new DoubleTransition { Property = AltProperty, Duration = TaskMetrics.Morph, Easing = TaskMetrics.Spring() },
        ];
        Morph = morph;
        Alt = alt;
    }

    // the canvas's two paths, point for point (24-unit box): pause bars, then the play triangle in two halves
    private static readonly Point[] Bars = [new(7.5, 5.5), new(10.7, 5.5), new(10.7, 18.5), new(7.5, 18.5), new(13.3, 5.5), new(16.5, 5.5), new(16.5, 18.5), new(13.3, 18.5)];
    private static readonly Point[] Play = [new(7.5, 5.2), new(13, 8.6), new(13, 15.4), new(7.5, 18.8), new(12.6, 8.35), new(18.5, 12), new(18.5, 12), new(12.6, 15.65)];

    public override void Render(DrawingContext context)
    {
        var ink = Ink ?? Brushes.Gray;
        var unit = Bounds.Width / 24;
        var alt = Math.Clamp(Alt, 0, 1.2);
        var main = Math.Max(0, 1 - alt);
        using (context.PushTransform(Matrix.CreateScale(unit, unit)))
        {
            if (main > .001)
            {
                using (context.PushTransform(Matrix.CreateTranslation(-12, -12) * Matrix.CreateScale(main, main) * Matrix.CreateTranslation(12, 12)))
                {
                    if (SlotA)
                    {
                        var k = Math.Clamp(Morph, -.2, 1.2);
                        var g = new StreamGeometry();
                        using (var c = g.Open())
                        {
                            for (var half = 0; half < 2; half++)
                            {
                                for (var i = 0; i < 4; i++)
                                {
                                    var a = Bars[half * 4 + i];
                                    var b = Play[half * 4 + i];
                                    var p = new Point(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k);
                                    if (i == 0) c.BeginFigure(p, true); else c.LineTo(p);
                                }
                                c.EndFigure(true);
                            }
                        }
                        context.DrawGeometry(ink, null, g);
                    }
                    else context.DrawRectangle(ink, null, new Rect(6.5, 6.5, 11, 11), 2.5, 2.5);
                }
            }
            if (alt > .001)
            {
                using (context.PushTransform(Matrix.CreateTranslation(-12, -12) * Matrix.CreateScale(alt, alt) * Matrix.CreateTranslation(12, 12)))
                    context.DrawGeometry(null, new Pen(ink, 1.7, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), SlotA ? RestartIcon : RevertIcon);
            }
        }
    }
}

/// <summary>
/// One of the two fixed control slots. Its kind changes in place (the icon morphs); with nothing to do it is disabled and out of the
/// tab order; with no control at all (a Done task) it keeps its place but shows and says nothing.
/// </summary>
public sealed class SlotButton : Decorator
{
    private readonly Border face;
    private readonly SlotGlyph glyph;
    private readonly IBrush inkRest, inkHot, faceHot;
    private bool painted;
    private SlotKind kind = SlotKind.None;
    private bool enabled;
    private bool coarse;

    public SlotButton(bool slotA)
    {
        glyph = new SlotGlyph(slotA) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        face = new Border { Child = glyph, Background = Brushes.Transparent };
        Button = PlainControls.Button(face, () => { if (enabled && kind != SlotKind.None) Clicked?.Invoke(this, kind); });
        Button.KeyboardFocus(9);
        Child = Button;
        inkRest = Chrome.SecondaryText.Brush(this);
        inkHot = Chrome.PrimaryText.Brush(this);
        faceHot = Chrome.Overlay(.07).Brush(face);
        Button.PointerEntered += (_, _) => Paint();
        Button.PointerExited += (_, _) => Paint();
        ActualThemeVariantChanged += (_, _) => glyph.InvalidateVisual();
        Coarse = TaskTouch.IsCoarse;
        Set(SlotKind.None, false, "", still: true);
    }

    public Button Button { get; }

    public SlotKind Kind => kind;

    public bool IsActionable => enabled && kind != SlotKind.None;

    /// <summary>Raised on a click of a live control.</summary>
    public event Action<SlotButton, SlotKind>? Clicked;

    internal SlotGlyph Glyph => glyph;

    /// <summary>40 px targets with touch, 28 px on a pointer.</summary>
    public bool Coarse
    {
        get => coarse;
        set
        {
            coarse = value;
            var size = value ? TaskMetrics.TouchButton : TaskMetrics.PointerButton;
            Width = Height = size;
            face.Width = face.Height = size;
            face.RoundedRectangle(value ? 12 : 9, RoundedCornerStyle.Continuous);
        }
    }

    public void Set(SlotKind next, bool isEnabled, string subject, bool still)
    {
        var changed = next != kind;
        kind = next;
        enabled = isEnabled && next != SlotKind.None;
        Button.IsEnabled = enabled;
        Button.Focusable = enabled;
        Button.IsTabStop = enabled;
        // a slot with no control keeps its place, invisible and silent
        Button.Opacity = next == SlotKind.None ? 0 : enabled ? 1 : .3;
        Button.IsHitTestVisible = next != SlotKind.None;
        AutomationProperties.SetAccessibilityView(Button, next == SlotKind.None ? AccessibilityView.Raw : AccessibilityView.Default);
        AutomationProperties.SetName(Button, next == SlotKind.None ? "" : TaskWords.Name(next, subject));
        ToolTip.SetTip(Button, next == SlotKind.None ? null : TaskWords.Tip(next));
        if (changed && next != SlotKind.None) glyph.Show(next, still);
        Paint();
    }

    private void Paint()
    {
        var hot = Button.IsPointerOver && enabled;
        var ink = hot ? inkHot : inkRest;
        if (painted && ReferenceEquals(glyph.Ink, ink)) return;
        painted = true;
        glyph.Ink = ink;
        face.Background = hot ? faceHot : Brushes.Transparent;
        glyph.InvalidateVisual();
    }
}

/// <summary>The two fixed slots of a task or head, side by side.</summary>
public sealed class TaskSlots : StackPanel
{
    public TaskSlots()
    {
        Orientation = Orientation.Horizontal;
        A = new SlotButton(slotA: true);
        B = new SlotButton(slotA: false);
        Children.Add(A);
        Children.Add(B);
        VerticalAlignment = VerticalAlignment.Center;
        Coarse = TaskTouch.IsCoarse;
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) }];
    }

    public SlotButton A { get; }

    public SlotButton B { get; }

    public bool Coarse
    {
        get => A.Coarse;
        set
        {
            A.Coarse = B.Coarse = value;
            Spacing = value ? TaskMetrics.TouchGap : TaskMetrics.PointerGap;
        }
    }

    public void Set(ControlSlots slots, string subject, bool still)
    {
        A.Set(TaskWords.KindOf(slots.A), slots.AEnabled, subject, still);
        B.Set(TaskWords.KindOf(slots.B), slots.BEnabled, subject, still);
    }

    /// <summary>The first live control (where focus goes when the one that opened an ask is now disabled, r4 B5).</summary>
    public Button? FirstLive => A.IsActionable ? A.Button : B.IsActionable ? B.Button : null;
}

// PORT-ONLY: Tactus pill v2, the parts every card body is built from (OD-0145, ED-NEW-pcctl-26; DESIGN.md 10.4; canvas
// pc-control-ui pill v2 `.pk`: `.pkb` the part that scrolls, `.pkx` the part that never does, `.kb` / `.kb.pri` buttons,
// `.kre` links, `.qc` quick replies, `.gch` the See and Use chips, `.pkf` the footer and its flash, `kbd` chips). The
// buttons never answer by themselves: a press goes to the card, which asks the shield (CardShield) whether it counts. The
// card's Enter and Space rules are the card's: a card button ignores both keys itself (a focused Button would otherwise
// click on Enter). "Always allow" is laid out at its armed width from the start and its capsule opens into the free space
// on the render thread, so nothing in its row moves.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Transformation;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Marrow.Core.Port.PcControl.Pill;
using SkiaSharp;
using Path = Avalonia.Controls.Shapes.Path;

namespace Marrow.UI.Port.PcControl;

/// <summary>The canvas's card tokens (`--ctl2`, `--v2-w*`, `--t1..3`).</summary>
internal static class CardLook
{
    public static IBrush White(double alpha) => new ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), 0xFF, 0xFF, 0xFF));

    public static readonly IBrush Ctl2 = White(0.10);
    public static readonly IBrush W14 = White(0.14);
    public static readonly IBrush W15 = White(0.15);
    public static readonly IBrush W16 = White(0.16);
    public static readonly IBrush W17 = White(0.17);
    public static readonly IBrush Field = White(0.06);
    public static readonly IBrush Change = White(0.07);
    public static readonly IBrush Well = new ImmutableSolidColorBrush(Color.FromArgb(0x3D, 0, 0, 0));
    public static readonly IBrush Rows = new ImmutableSolidColorBrush(Color.FromArgb(0x3D, 0, 0, 0));
    public static readonly IBrush RowLine = White(0.06);
    public static readonly IBrush Label = new ImmutableSolidColorBrush(Color.FromArgb(0x73, 0xF0, 0xF0, 0xF2));
    public static readonly IBrush Text1 = ControlPill.Text1;
    public static readonly IBrush Text2 = ControlPill.Text2;
    public static readonly IBrush Text3 = new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0xEC, 0xEC, 0xEE));
    public static readonly IBrush Path = new ImmutableSolidColorBrush(Color.FromArgb(0x61, 0xEC, 0xEC, 0xEE));
    public static readonly IBrush Bright = Brushes.White;
    public static readonly IBrush Element = new ImmutableSolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBD));
    public static readonly IBrush KeyFill = White(0.08);
    public static readonly IBrush KeyInk = new ImmutableSolidColorBrush(Color.FromArgb(0xD1, 0xF2, 0xF2, 0xF3));
    public static readonly IBrush Underline = White(0.25);

    public static readonly BoxShadows KeyRing = BoxShadows.Parse("inset 0 0 0 0.5 #29FFFFFF");
    public static readonly BoxShadows RowsRing = BoxShadows.Parse("inset 0 0 0 0.5 #14FFFFFF");
    public static readonly BoxShadows FieldRing = BoxShadows.Parse("inset 0 0 0 1 #1AFFFFFF");
    public static readonly BoxShadows FieldRingOn = BoxShadows.Parse("inset 0 0 0 1 #57FFFFFF, 0 0 0 4 #0DFFFFFF");
    public static readonly BoxShadows RiskBar = BoxShadows.Parse("inset 3 0 0 0 #9EFFFFFF");
    public static readonly BoxShadows ChangeBar = BoxShadows.Parse("inset 2 0 0 0 #E6FFFFFF");
    public static readonly BoxShadows ElementRing = BoxShadows.Parse("0 0 0 1.5 #8CFFFFFF");
    public static readonly BoxShadows TileRim = BoxShadows.Parse("inset 0 0.5 0 0 #59FFFFFF, inset 0 0 0 0.5 #24FFFFFF, 0 1 2 0 #66000000");

    public static readonly FontFamily Text = ControlPill.TextFont;
    public static readonly FontFamily Mono = ControlPill.MonoFont;

    public static TextBlock Line(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal, double lineHeight = double.NaN, TextWrapping wrap = TextWrapping.Wrap) => new()
    {
        Text = text, FontFamily = Text, FontSize = size, FontWeight = weight, Foreground = brush, TextWrapping = wrap, LineHeight = lineHeight,
        TextTrimming = wrap == TextWrapping.NoWrap ? TextTrimming.CharacterEllipsis : TextTrimming.None,
    };

    /// <summary>One piece of a hint or footer row: text, a key chip (`kbd`) or the separating dot.</summary>
    public static Control Piece(PillHintPart part, double size, IBrush brush)
    {
        Control piece = part.Kind switch
        {
            PillHintPartKind.Key => new Border
            {
                Padding = new Thickness(5, 2), CornerRadius = new CornerRadius(5), Background = KeyFill, BoxShadow = KeyRing, VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = part.Text, FontFamily = Mono, FontSize = 10.5, FontWeight = FontWeight.Medium, LineHeight = 10.5, Foreground = KeyInk },
            },
            PillHintPartKind.Dot => new TextBlock { Text = part.Text, FontFamily = Text, FontSize = size, Foreground = brush, Opacity = 0.45, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center },
            _ => new TextBlock { Text = part.Text, FontFamily = Text, FontSize = size, Foreground = brush, VerticalAlignment = VerticalAlignment.Center },
        };
        AutomationProperties.SetAccessibilityView(piece, AccessibilityView.Raw);
        return piece;
    }

    /// <summary>The canvas's stroked 20-unit icons (`.ic`: no fill, 1.5 stroke, round caps and joins).</summary>
    public static Control Icon(string data, double size, IBrush brush, double stroke = 1.5)
    {
        var path = new Path { Data = Geometry.Parse(data), Stroke = brush, StrokeThickness = stroke, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
        var box = new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = new Canvas { Width = 20, Height = 20, Children = { path } }, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        AutomationProperties.SetAccessibilityView(box, AccessibilityView.Raw);
        return box;
    }

    public const string Eye = "M2,10 C2,10 5,4.5 10,4.5 C15,4.5 18,10 18,10 C18,10 15,15.5 10,15.5 C5,15.5 2,10 2,10 Z M12.4,10 A2.4,2.4 0 1 1 7.6,10 A2.4,2.4 0 1 1 12.4,10 Z";
    public const string Click = "M8.5,8.5 L16.5,11.5 L13.1,12.8 L15.8,15.5 L14.5,16.8 L11.8,14.1 L10.5,17.5 Z M8,2.5 V4.5 M3.7,4.2 L5.1,5.6 M2.5,8.5 H4.5 M12.3,4.2 L10.9,5.6";
    public const string Check = "M5,10.5 L8.2,13.7 L15,6.8";
    public const string Send = "M3,9.8 L17,3.5 L12.4,17.1 L9.8,11.5 Z M9.8,11.5 L17,3.5";
    public const string Shield = "M10,2.5 L16,4.7 V9.3 C16,13.3 13.4,15.9 10,17.5 C6.6,15.9 4,13.3 4,9.3 V4.7 Z M7.4,10 L9.3,11.9 L12.7,8.3";
    public const string Cpu = "M7,5 H13 A2,2 0 0 1 15,7 V13 A2,2 0 0 1 13,15 H7 A2,2 0 0 1 5,13 V7 A2,2 0 0 1 7,5 Z M8,2.5 V5 M12,2.5 V5 M8,15 V17.5 M12,15 V17.5 M2.5,8 H5 M2.5,12 H5 M15,8 H17.5 M15,12 H17.5";
    public const string Alert = "M10,4.5 V11.5 M10,15.2 V15.5";
}

/// <summary>The card buttons' looks (canvas `.kb`, `.kb.pri`, `.kre`, `.qc`, `.gch`, `.mcs`).</summary>
public enum CardButtonLook
{
    Plain,
    Primary,
    Link,
    Quick,
    Toggle,
    Send,
    Always,
}

/// <summary>
/// A card button: the canvas's fill, rim and radius, a hover fill, a 0.97 press scale, and a 2 DIP focus ring for keyboard
/// focus only. It never clicks on Enter or Space by itself (the card's key rules decide), and it remembers how the last
/// press came (mouse, pen or touch, and the click count), which the shield needs.
/// </summary>
public class CardButton : Button, ICustomHitTest
{
    private static readonly IBrush Ring = CardLook.White(0.75);
    private static readonly TransformOperations PressScale = TransformOperations.Parse("scale(0.97)");

    private readonly Border ring = new() { BorderBrush = Ring, BorderThickness = new Thickness(2), IsHitTestVisible = false, IsVisible = false };
    private bool pressed;

    public CardButton(CardButtonLook look, object? content = null)
    {
        Look = look;
        Focusable = true;
        FocusAdorner = null;
        Content = content;
        Cursor = new Cursor(StandardCursorType.Arrow);
        var (height, radius, padding, size, weight) = look switch
        {
            CardButtonLook.Link => (double.NaN, 0.0, new Thickness(0), 12.5, FontWeight.Normal),
            CardButtonLook.Quick => (30.0, 15.0, new Thickness(12, 0), 13.0, FontWeight.Normal),
            CardButtonLook.Toggle => (30.0, 15.0, new Thickness(12, 0), 13.5, FontWeight.Normal),
            CardButtonLook.Send => (30.0, 15.0, new Thickness(0), 13.0, FontWeight.Normal),
            CardButtonLook.Primary => (34.0, 17.0, new Thickness(12, 0), 13.5, FontWeight.SemiBold),
            _ => (34.0, 17.0, new Thickness(12, 0), 13.5, FontWeight.Medium),
        };
        if (!double.IsNaN(height)) Height = height;
        if (look == CardButtonLook.Send) Width = 30;
        CornerRadius = new CornerRadius(radius);
        Padding = padding;
        FontFamily = CardLook.Text;
        FontSize = size;
        FontWeight = weight;
        Foreground = look == CardButtonLook.Link ? CardLook.Text2 : CardLook.Text1;
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
            presenter.Bind(ContentPresenter.ForegroundProperty, button.GetObservable(ForegroundProperty));
            presenter.Bind(ContentPresenter.BoxShadowProperty, button.GetObservable(BoxShadowProperty));
            return new Panel { Children = { presenter, ring } };
        });
        BoxShadow = Rim;
        PointerEntered += (_, _) => Hovered(true);
        PointerExited += (_, _) => Hovered(false);
        Classes.CollectionChanged += (_, _) => SetRing(IsEffectivelyEnabled && Classes.Contains(":focus-visible"));
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsEffectivelyEnabledProperty)
            {
                SetRing(IsEffectivelyEnabled && Classes.Contains(":focus-visible"));
                Opacity = IsEffectivelyEnabled ? 1 : 0.35;
            }
        };
    }

    public static readonly StyledProperty<BoxShadows> BoxShadowProperty = Border.BoxShadowProperty.AddOwner<CardButton>();

    public BoxShadows BoxShadow
    {
        get => GetValue(BoxShadowProperty);
        set => SetValue(BoxShadowProperty, value);
    }

    public CardButtonLook Look { get; }

    /// <summary>How the last press came (the shield's rest rule is for the mouse only).</summary>
    public PillPointerKind LastKind { get; private set; } = PillPointerKind.Mouse;

    /// <summary>The last press's click count (2 for the second click of a double-click).</summary>
    public int LastClickCount { get; private set; } = 1;

    /// <summary>The toggle chip's state (`aria-pressed`).</summary>
    public bool IsOn
    {
        get => pressed;
        set
        {
            pressed = value;
            Background = Rest;
            BoxShadow = Rim;
            if (Look == CardButtonLook.Toggle) Foreground = value ? CardLook.Bright : CardLook.Text3;
            AutomationProperties.SetItemStatus(this, value ? "On" : "Off");
            Toggled?.Invoke();
        }
    }

    /// <summary>The toggle chip turned on or off.</summary>
    public event Action? Toggled;

    /// <summary>The left part of the button that does not take input (the closed part of "Always allow"'s capsule).</summary>
    public double HitInset { get; set; }

    /// <summary>The keyboard focus ring shows (tests).</summary>
    public bool FocusRing { get; private set; }

    public bool HitTest(Point point) => point.X >= HitInset && point.X <= Bounds.Width && point.Y >= 0 && point.Y <= Bounds.Height;

    private IBrush Rest => Look switch
    {
        CardButtonLook.Primary => CardLook.W17,
        CardButtonLook.Link or CardButtonLook.Always => Brushes.Transparent,
        CardButtonLook.Send => CardLook.W14,
        CardButtonLook.Toggle => pressed ? CardLook.W16 : Brushes.Transparent,
        CardButtonLook.Quick => pressed ? CardLook.W17 : CardLook.Ctl2,
        _ => CardLook.Ctl2,
    };

    private IBrush Hover => Look switch
    {
        CardButtonLook.Plain => CardLook.W15,
        CardButtonLook.Quick => CardLook.W16,
        _ => Rest,
    };

    private BoxShadows Rim => Look switch
    {
        CardButtonLook.Primary => BoxShadows.Parse("inset 0 0.75 0 0 #66FFFFFF, inset 0 0 0 1 #57FFFFFF, 0 1 2 0 #73000000"),
        CardButtonLook.Plain => BoxShadows.Parse("inset 0 0.5 0 0 #26FFFFFF, inset 0 0 0 0.5 #1AFFFFFF"),
        CardButtonLook.Quick => BoxShadows.Parse("inset 0 0 0 0.5 #2EFFFFFF"),
        CardButtonLook.Toggle => pressed ? BoxShadows.Parse("inset 0 0.5 0 0 #4DFFFFFF, inset 0 0 0 1 #4DFFFFFF") : BoxShadows.Parse("inset 0 0 0 1 #24FFFFFF"),
        _ => default,
    };

    private void Hovered(bool on)
    {
        Background = on ? Hover : Rest;
        if (Look == CardButtonLook.Link) Foreground = on ? CardLook.Text1 : CardLook.Text2;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        LastKind = e.Pointer.Type switch
        {
            PointerType.Pen => PillPointerKind.Pen,
            PointerType.Touch => PillPointerKind.Touch,
            _ => PillPointerKind.Mouse,
        };
        LastClickCount = Math.Max(1, e.ClickCount);
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

    // Enter never presses a card button and Space goes through the card's shield (CardShield.Key), so the Button's own
    // key handling is not used.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space) return;
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space) return;
        base.OnKeyUp(e);
    }

    /// <summary>The card counted a keyboard press (Space through the shield): the button acts as if clicked.</summary>
    public void Press()
    {
        LastKind = PillPointerKind.Mouse;
        LastClickCount = 1;
        OnClick();
    }

    private void SetRing(bool on)
    {
        if (FocusRing == on) return;
        FocusRing = on;
        ring.IsVisible = on;
    }
}

/// <summary>One protected or free control of a body, for the shield and the placement.</summary>
public sealed record CardBodyControl(string Id, CardButton Button, CardControlKind Kind);

/// <summary>
/// A card body: what the ask is about (it scrolls, with a 12 DIP fade at its cut edge) over the buttons, the reply field
/// and the footer (they never scroll, so Confirm, Allow and Send are always in view). Its presses go to the card.
/// </summary>
public abstract class CardBody : Grid
{
    public const double Width440 = PillMorph.CardWidth;
    public const double CutFade = 12;

    private readonly List<CardBodyControl> controls = [];

    protected CardBody(PillCardBody model)
    {
        Model = model;
        Width = Width440;
        RowDefinitions = new RowDefinitions("Auto,Auto");
        Above = new StackPanel { Margin = new Thickness(16, 2, 16, 0) };
        Below = new StackPanel { Margin = new Thickness(16, 0, 16, 14) };
        Scroll = new ScrollViewer
        {
            Content = Above,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
        };
        SetRow(Scroll, 0);
        SetRow(Below, 1);
        Children.Add(Scroll);
        Children.Add(Below);
        Scroll.ScrollChanged += (_, _) => CutEdge();
        Scroll.SizeChanged += (_, _) => CutEdge();
    }

    public PillCardBody Model { get; }

    /// <summary>What the ask is about (scrolls).</summary>
    public StackPanel Above { get; }

    /// <summary>The buttons, the reply field and the footer (never scroll).</summary>
    public StackPanel Below { get; }

    public ScrollViewer Scroll { get; }

    public IReadOnlyList<CardBodyControl> Controls => controls;

    public MiniComposer? Composer { get; protected set; }

    public CardFooter? Footer { get; protected set; }

    /// <summary>A control was pressed (the card applies the shield before it acts).</summary>
    public event Action<CardBodyControl>? Pressed;

    /// <summary>The body's height may have changed (the reply field opened or grew).</summary>
    public event Action? Relaid;

    /// <summary>Whether what scrolls is cut (the fade shows at its bottom edge).</summary>
    public bool Cut { get; private set; }

    /// <summary>The most the scrolling part may take, so the whole card stays inside its height cap.</summary>
    public double ScrollCap
    {
        get => Scroll.MaxHeight;
        set => Scroll.MaxHeight = Math.Max(0, value);
    }

    /// <summary>
    /// What pressing <paramref name="id"/> means once the shield let it through: an answer, or null for an action inside the
    /// body (a chip toggled, the reply field opened).
    /// </summary>
    public abstract PillCardAnswer? Act(string id);

    /// <summary>Hands the keyboard to the reply field when it shows (the field is the one place a click activates the card).</summary>
    public virtual void Opened()
    {
    }

    protected CardButton Add(string id, CardControlKind kind, CardButton button)
    {
        var entry = new CardBodyControl(id, button, kind);
        controls.Add(entry);
        button.Name = id;
        button.Click += (_, e) =>
        {
            e.Handled = true;
            Pressed?.Invoke(entry);
        };
        return button;
    }

    protected void Relayout() => Relaid?.Invoke();

    protected static CardButton Labelled(CardButtonLook look, string text) => new(look, new TextBlock { Text = text, FontFamily = CardLook.Text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

    /// <summary>A row of buttons with a spring between them (canvas `.kbs` with `.sp`).</summary>
    protected static Grid Row(Control left, Control? right, double top = 14)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, top, 0, 0) };
        SetColumn(left, 0);
        row.Children.Add(left);
        if (right is not null)
        {
            SetColumn(right, 2);
            row.Children.Add(right);
        }
        return row;
    }

    // canvas `.pkb.cut`: a 12 DIP fade at the bottom edge while what scrolls is cut
    private void CutEdge()
    {
        var cut = Scroll.Extent.Height > Scroll.Viewport.Height + 1;
        if (cut == Cut && (!cut || Scroll.OpacityMask is not null)) return;
        Cut = cut;
        var h = Math.Max(1, Scroll.Bounds.Height);
        Scroll.OpacityMask = cut
            ? new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(0, h, RelativeUnit.Absolute),
                GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Black, Math.Max(0, (h - CutFade) / h)), new GradientStop(Colors.Transparent, 1) },
            }
            : null;
    }
}

/// <summary>
/// The card's footer (canvas `.pkf`): its quiet text, which a flash replaces for 1.3 s in full white with a small nudge
/// ("Enter does nothing here", "Rest on a button for a moment, then click", "Click again, a moment later").
/// </summary>
public sealed class CardFooter : Grid
{
    public const double FlashSeconds = 1.3;
    public const double NudgeSeconds = 0.4;

    private readonly StackPanel text = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly IReadOnlyList<PillHintPart> quiet;
    private int flashes;

    public CardFooter(IReadOnlyList<PillHintPart> quiet, Control? right = null)
    {
        this.quiet = quiet;
        Margin = new Thickness(0, 12, 0, 0);
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        SetColumn(text, 0);
        Children.Add(text);
        if (right is not null)
        {
            SetColumn(right, 1);
            Children.Add(right);
        }
        Fill(quiet, CardLook.Text3);
        AutomationProperties.SetName(this, string.Concat(quiet.Select(p => p.Text)));
    }

    /// <summary>The words shown now (tests).</summary>
    public string Shown => string.Concat(text.Children.Select(c => c is TextBlock t ? t.Text : c is Border { Child: TextBlock k } ? k.Text : ""));

    public bool Flashing => flashes > 0;

    /// <summary>
    /// Flashes <paramref name="message"/> (or the quiet text) for 1.3 s; <paramref name="after"/> runs an action later on the
    /// card's clock and <paramref name="nudge"/> moves the text by the canvas's 3 DIP nudge.
    /// </summary>
    public void Flash(string? message, Action<double, Action> after, Action<Visual, PillTrack, PillTrack> nudge, double now)
    {
        Fill(message is null ? quiet : [new PillHintPart(message)], CardLook.Text1);
        AutomationProperties.SetName(this, message ?? string.Concat(quiet.Select(p => p.Text)));
        var flash = ++flashes;
        nudge(text, PillTrack.Of(now, now + NudgeSeconds, t => Nudge((t - now) / NudgeSeconds)), PillTrack.Of(now, now + NudgeSeconds, _ => 0));
        after(FlashSeconds, () =>
        {
            if (flash != flashes) return;
            flashes = 0;
            Fill(quiet, CardLook.Text3);
            AutomationProperties.SetName(this, string.Concat(quiet.Select(p => p.Text)));
        });
    }

    // canvas @keyframes nudge: 0, -3 at 25 %, +3 at 75 %, 0 (linear between, on the card ease)
    private static double Nudge(double p) => p < 0.25 ? -3 * (p / 0.25) : p < 0.75 ? -3 + (6 * ((p - 0.25) / 0.5)) : 3 * (1 - ((p - 0.75) / 0.25));

    private void Fill(IReadOnlyList<PillHintPart> parts, IBrush brush)
    {
        text.Children.Clear();
        foreach (var part in parts) text.Children.Add(CardLook.Piece(part, 12, brush));
    }
}

/// <summary>
/// A capsule drawn on the render thread whose left edge eases between two insets (canvas `.kb.alw`'s rounded clip-path,
/// 220 ms on cubic-bezier(.32,.72,0,1)): "Always allow"'s fill opens leftward into free space without any layout.
/// </summary>
public sealed class PillCapsule : Control
{
    public const double Seconds = 0.22;

    private readonly CapsuleHandler handler = new();
    private CompositionCustomVisual? visual;
    private CapsuleFrame? last;

    public PillCapsule()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    /// <summary>The last frame sent (tests).</summary>
    public CapsuleFrame? Last => last;

    /// <summary>Eases the left inset to <paramref name="inset"/> (or sets it at once) with the fill and the ring of the state.</summary>
    public void To(double inset, Color fill, Color? ring, double now, bool instant)
    {
        var from = last is { } f ? f.At(now) : inset;
        Send(new CapsuleFrame(instant ? inset : from, inset, now, instant ? 0 : Seconds, fill, ring));
    }

    private void Send(CapsuleFrame frame)
    {
        last = frame;
        visual?.SendHandlerMessage(frame);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (visual is not null || ElementComposition.GetElementVisual(this) is not { } host) return;
        visual = host.Compositor.CreateCustomVisual(handler);
        visual.Size = new Vector(Bounds.Width, Bounds.Height);
        ElementComposition.SetElementChildVisual(this, visual);
        if (last is { } frame) visual.SendHandlerMessage(frame);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (visual is not null) visual.Size = new Vector(e.NewSize.Width, e.NewSize.Height);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (visual is null) return;
        ElementComposition.SetElementChildVisual(this, null);
        visual = null;
    }

    public sealed record CapsuleFrame(double From, double To, double Start, double Duration, Color Fill, Color? Ring)
    {
        public double At(double now) => Duration <= 0 ? To : From + ((To - From) * PillEase.CubicBezier(0.32, 0.72, 0, 1, Math.Clamp((now - Start) / Duration, 0, 1)));

        public bool Done(double now) => Duration <= 0 || now >= Start + Duration;
    }

    private sealed class CapsuleHandler : CompositionCustomVisualHandler
    {
        private CapsuleFrame? frame;
        private TimeSpan arrived;

        public override void OnMessage(object message)
        {
            if (message is not CapsuleFrame next) return;
            frame = next;
            arrived = CompositionNow;
            Invalidate();
            if (!next.Done(next.Start)) RegisterForNextAnimationFrameUpdate();
        }

        public override void OnAnimationFrameUpdate()
        {
            if (frame is not { } current) return;
            Invalidate();
            if (!current.Done(Time(current))) RegisterForNextAnimationFrameUpdate();
        }

        private double Time(CapsuleFrame current) => current.Start + (CompositionNow - arrived).TotalSeconds;

        public override Rect GetRenderBounds() => new(0, 0, EffectiveSize.X, EffectiveSize.Y);

        public override void OnRender(ImmediateDrawingContext context)
        {
            if (frame is not { } current || context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var size = EffectiveSize;
            var inset = (float)Math.Clamp(current.At(Time(current)), 0, size.X);
            var h = (float)size.Y;
            var rect = new SKRoundRect(new SKRect(inset, 0, (float)size.X, h), h / 2);
            using var fill = new SKPaint { IsAntialias = true, Color = new SKColor(current.Fill.R, current.Fill.G, current.Fill.B, current.Fill.A) };
            lease.SkCanvas.DrawRoundRect(rect, fill);
            if (current.Ring is { } ring)
            {
                using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = new SKColor(ring.R, ring.G, ring.B, ring.A) };
                var inner = new SKRoundRect(new SKRect(inset + 0.5f, 0.5f, (float)size.X - 0.5f, h - 0.5f), (h - 1) / 2);
                lease.SkCanvas.DrawRoundRect(inner, stroke);
            }
        }
    }
}

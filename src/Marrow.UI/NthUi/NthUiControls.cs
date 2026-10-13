// PORT-ONLY: web surfaces W16a (DESIGN.md 7.5 "pill chips ... monochrome line art with the accent only for the current value
// and focus, tabular numerals"; 10 "nth-ui components are native Avalonia controls with roles, names and values"; canvas
// index.html .ubtn, .fchips, .seg; ED-NEW-web-w16a-1). No Swift counterpart. The small parts every component is made of:
// text in the thread's type ramp, Lucide line icons, and pressable faces on real Avalonia buttons and toggle buttons (so
// each has its automation role, keyboard activation and focus), drawn as tinted fills inside the card, never glass.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.UI.Port;

namespace Marrow.UI.NthUi;

/// <summary>Text and icons in the live view's look.</summary>
public static class NthUiText
{
    /// <summary>A wrapping text block in <paramref name="font"/> and <paramref name="color"/>.</summary>
    public static TextBlock Block(string text, SwiftFont font, SwiftColor? color, bool wrap = true)
    {
        var block = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
        font.Apply(block);
        if (color is not null) block.Foreground = color.Brush(block);
        if (!wrap) block.TextTrimming = TextTrimming.CharacterEllipsis;
        return block;
    }

    /// <summary>A Lucide icon by name (lower-case, dashes), or null when nth has no such icon.</summary>
    public static NthUiIcon? Icon(string? name, double size = 16, SwiftColor? color = null)
    {
        var icon = NthUiIcon.Find(name);
        if (icon is null) return null;
        var control = new NthUiIcon(icon) { Width = size, Height = size };
        if (color is not null) control.Foreground = color.Brush(control);
        return control;
    }
}

/// <summary>A Lucide line icon drawn in the inherited text colour (canvas .ic: stroke 1.6 at 16 DIP).</summary>
public sealed class NthUiIcon : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<NthUiIcon>();

    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.Ordinal)
    {
        ["chart"] = ["chart-line", "chart-no-axes-column", "activity"],
        ["rocket"] = ["send", "arrow-up-right"],
        ["download"] = ["arrow-down-to-line", "arrow-down", "save"],
        ["external"] = ["square-arrow-out-up-right", "arrow-up-right"],
        ["warning"] = ["triangle-alert"],
        ["danger"] = ["octagon-alert", "circle-alert"],
        ["success"] = ["circle-check", "check"],
        ["error"] = ["circle-alert"],
    };

    private readonly SvgIcon icon;

    public NthUiIcon(SvgIcon icon)
    {
        this.icon = icon;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    static NthUiIcon()
    {
        AffectsRender<NthUiIcon>(ForegroundProperty);
    }

    /// <summary>The parsed icon for an nth-ui icon name: the Lucide icon of that name, else a close alias; null when none.</summary>
    public static SvgIcon? Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (SymbolCatalogue.Icon(name) is { } exact) return exact;
        if (Aliases.TryGetValue(name, out var others))
        {
            foreach (var other in others)
            {
                if (SymbolCatalogue.Icon(other) is { } found) return found;
            }
        }
        return null;
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsNaN(Width) ? 16 : Width, double.IsNaN(Height) ? 16 : Height);

    public override void Render(DrawingContext context)
    {
        var brush = Foreground;
        if (brush is null || Bounds.Width <= 0) return;
        var box = icon.ViewBox;
        var scale = Math.Min(Bounds.Width / box.Width, Bounds.Height / box.Height);
        var pen = new ImmutablePen(brush.ToImmutable(), 1.6 / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateTranslation(-box.X, -box.Y) * Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation((Bounds.Width - box.Width * scale) / 2, (Bounds.Height - box.Height * scale) / 2)))
        {
            foreach (var shape in icon.Shapes)
            {
                var fill = shape.Fill.Kind == SvgPaintKind.CurrentColor ? brush : null;
                var stroke = shape.Stroke.Kind == SvgPaintKind.None ? null : pen;
                context.DrawGeometry(fill, stroke, shape.Geometry);
            }
        }
    }
}

/// <summary>The looks a pressable face takes.</summary>
public enum NthUiFaceStyle
{
    /// <summary>An action button (canvas .ubtn): tinted fill, pill.</summary>
    Plain,

    /// <summary>The one strong button of a form or a primary action: monochrome solid.</summary>
    Primary,

    /// <summary>A follow-up chip (canvas .fchips): faint fill and rim.</summary>
    Chip,

    /// <summary>A filter chip: rim when off, chosen fill when on.</summary>
    Filter,

    /// <summary>A segment or tab: clear when off (the track draws the chosen pill), primary text when on.</summary>
    Segment,

    /// <summary>A small icon button (a stepper's minus and plus, the steps arrows): clear until hovered.</summary>
    Ghost,
}

/// <summary>The visible face of a button: its fill, rim and text colour follow the owner's hover, press, check and focus.</summary>
public sealed class NthUiFace : Border
{
    private readonly TemplatedControl owner;
    private readonly NthUiFaceStyle style;
    private readonly IBrush rest;
    private readonly IBrush hover;
    private readonly IBrush on;
    private readonly IBrush text;
    private readonly IBrush textOn;
    private readonly IBrush? rim;
    private readonly IBrush focus;
    private bool over;

    public NthUiFace(TemplatedControl owner, NthUiFaceStyle style, double radius)
    {
        this.owner = owner;
        this.style = style;
        CornerRadius = new CornerRadius(radius);
        (SwiftColor Idle, SwiftColor Hover, SwiftColor On, SwiftColor Text, SwiftColor TextOn, SwiftColor? Rim) look = style switch
        {
            NthUiFaceStyle.Primary => (NthUiTokens.Strong, NthUiTokens.Strong.Opacity(.88), NthUiTokens.Strong, NthUiTokens.OnStrong, NthUiTokens.OnStrong, null),
            NthUiFaceStyle.Chip => (NthUiTokens.ControlFill.Opacity(.5), NthUiTokens.ControlHover, NthUiTokens.ControlHover, NthUiTokens.Text1, NthUiTokens.Text1, NthUiTokens.ControlRim),
            NthUiFaceStyle.Filter => (SwiftColor.Clear, NthUiTokens.ControlFill, NthUiTokens.Chosen, NthUiTokens.Text2, NthUiTokens.Text1, NthUiTokens.ControlRim),
            NthUiFaceStyle.Segment => (SwiftColor.Clear, NthUiTokens.ControlFill, SwiftColor.Clear, NthUiTokens.Text2, NthUiTokens.Text1, null),
            NthUiFaceStyle.Ghost => (SwiftColor.Clear, NthUiTokens.ControlHover, NthUiTokens.ControlHover, NthUiTokens.Text1, NthUiTokens.Text1, null),
            _ => (NthUiTokens.ControlFill, NthUiTokens.ControlHover, NthUiTokens.ControlHover, NthUiTokens.Text1, NthUiTokens.Text1, null),
        };
        rest = look.Idle.Brush(this);
        hover = look.Hover.Brush(this);
        on = look.On.Brush(this);
        text = look.Text.Brush(this);
        textOn = look.TextOn.Brush(this);
        rim = look.Rim?.Brush(this);
        focus = NthUiTokens.Accent.Brush(this);
        BorderThickness = rim is null ? default : new Thickness(1);
        owner.PointerEntered += (_, _) => { over = true; Update(); };
        owner.PointerExited += (_, _) => { over = false; Update(); };
        owner.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty || e.Property == Button.IsPressedProperty || e.Property == InputElement.IsEffectivelyEnabledProperty) Update();
        };
        owner.Classes.CollectionChanged += (_, _) => Update();
        Update();
    }

    /// <summary>Whether the face shows its on state (a checked toggle).</summary>
    public bool IsOn => owner is ToggleButton { IsChecked: true };

    internal void Update()
    {
        var pressed = owner is Button { IsPressed: true };
        Background = IsOn ? on : pressed || over ? hover : rest;
        if (style == NthUiFaceStyle.Primary && pressed) Background = rest;
        BorderBrush = IsOn && style == NthUiFaceStyle.Filter ? Brushes.Transparent : rim;
        SetValue(TextElement.ForegroundProperty, IsOn ? textOn : text);
        Opacity = owner.IsEffectivelyEnabled ? 1 : .45;
        var focused = owner.Classes.Contains(":focus-visible");
        BoxShadow = focused
            ? new BoxShadows(new BoxShadow { Spread = 2, Color = (focus as ISolidColorBrush)?.Color ?? Colors.DodgerBlue })
            : default;
    }
}

/// <summary>Real buttons with a face for content: role, name, keyboard and focus come from Avalonia.</summary>
public static class NthUiPress
{
    /// <summary>A template that only presents the content (the app carries no theme package for buttons).</summary>
    public static T Plain<T>(T control) where T : ContentControl
    {
        control.Template = new FuncControlTemplate<T>((owner, scope) => new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty],
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        }.RegisterInNameScope(scope));
        control.Padding = default;
        control.Background = Brushes.Transparent;
        control.Cursor = new Cursor(StandardCursorType.Hand);
        return control;
    }

    /// <summary>A button labelled with text and an optional leading icon.</summary>
    public static Button Button(string label, NthUiFaceStyle style, string? icon, Action pressed, double height = NthUiTokens.ButtonHeight)
    {
        var button = Plain(new Button());
        button.Content = Face(button, style, label, icon, height, out _);
        button.Click += (_, _) => pressed();
        AutomationProperties.SetName(button, label);
        return button;
    }

    /// <summary>A toggle button labelled with text (a filter chip, a segment, a tab).</summary>
    public static ToggleButton Toggle(string label, NthUiFaceStyle style, double height, Action pressed, out NthUiFace face, out TextBlock text)
    {
        var toggle = Plain(new ToggleButton());
        toggle.Content = face = Face(toggle, style, label, null, height, out text);
        toggle.IsCheckedChanged += (_, _) => toggle.Classes.Set("on", toggle.IsChecked == true);
        toggle.Click += (_, _) => pressed();
        AutomationProperties.SetName(toggle, label);
        return toggle;
    }

    /// <summary>A face holding a label and an icon, padded as a pill of <paramref name="height"/>.</summary>
    public static NthUiFace Face(TemplatedControl owner, NthUiFaceStyle style, string label, string? icon, double height, out TextBlock text)
    {
        var font = style is NthUiFaceStyle.Chip or NthUiFaceStyle.Filter ? NthUiTokens.Small : NthUiTokens.Body;
        text = NthUiText.Block(label, font, null, wrap: false);
        text.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (NthUiText.Icon(icon, 13) is { } glyph)
        {
            glyph.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(glyph);
        }
        row.Children.Add(text);
        var face = new NthUiFace(owner, style, height / 2)
        {
            Height = height,
            Padding = new Thickness(style == NthUiFaceStyle.Segment ? 10 : 12, 0),
            Child = row,
        };
        face.Transitions = [new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromSeconds(.15) }];
        return face;
    }
}

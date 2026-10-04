// OD-0099 Look Picks, Q-CZ-3 B (owner pick): Marrow's own glass colour picker for the Uiverse light's Custom colour wells, drawn after
// lp-pickers.js variant B (pickerB): a saturation-brightness square, a hue strip, an opacity strip, a hex field with the opacity in
// percent and a dot of the colour, and the recent colours. It is the content of a PopoverCard (OD-0095): the host's card carries the
// arrow tip; the popover is the picker's width plus 12. The colour is held as the design holds it (hue, saturation, value, alpha) and
// handed out as "#RRGGBBAA", the form the Uiverse custom colours are stored in; a "#RRGGBB" given to it reads as opaque.
// Sizes, gaps and radii are lp-page.css's for variant B (.mg: 260 px, 6 px padding, 10 px gap; see PickerSurface for the drawn parts);
// what that stylesheet names but does not define, and the keyboard, are ENGINEERING DECISIONS under ED-NEW-f-cz-2 in
// porting/engineering_decisions.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.UI.Port.Settings.ColorPicker;
using static Marrow.UI.Port.SwiftFont;

namespace Marrow.UI.Port.Settings;

/// <summary>The glass colour picker (see the file note).</summary>
public sealed class GlassColorPicker : Panel
{
    /// <summary>The picker's width (.mg: width 260); the popover around it is this plus 12 (the card's 6 px on each side, .cz-pk: left 6 top 6).</summary>
    public const double PickerWidth = 260;

    /// <summary>.mg's padding.</summary>
    internal const double Padding = 6;

    /// <summary>.mg's flex gap.</summary>
    internal const double Gap = 10;

    /// <summary>.mg-hexrow's gap, and .mg-hex's height (28), corner (14), side padding (10) and inner gap (2).</summary>
    internal const double HexRowGap = 8, HexHeight = 28, HexRadius = 14, HexPadding = 10, HexInnerGap = 2;

    /// <summary>.mg-pct's min-width and .mg-dot's size.</summary>
    internal const double PercentWidth = 42, DotSize = 22;

    /// <summary>.mg-cap: 11 px at line-height 1.3, margin-bottom -4 px (so 6 px above the swatches).</summary>
    internal const double CaptionHeight = 11 * 1.3, CaptionMargin = -4;

    /// <summary>The room the popover's card keeps round the picker inside its 1 pt edge: 6 less that edge (OD-0095's card draws the edge itself).</summary>
    public static Thickness CardInset => new(5);

    private PickerColor colour = PickerMath.FromHex("#8E7CFF");
    private PickerRecent recent = new();

    internal PickerSurface Square { get; } = new(PickerSurfaceKind.Square);
    internal PickerSurface Hue { get; } = new(PickerSurfaceKind.Hue);
    internal PickerSurface OpacityStrip { get; } = new(PickerSurfaceKind.Opacity);
    internal PickerSurface RecentRow { get; } = new(PickerSurfaceKind.Recent);
    internal TextBox HexBox { get; }
    internal TextBlock Percent { get; }
    internal Border Dot { get; }
    internal Border Pill { get; }
    private readonly TextBlock hash, caption;

    public GlassColorPicker()
    {
        Width = PickerWidth;
        TextBlock Mono(double size, Weight weight) { var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center }; SwiftFont.System(size, weight, SwiftFont.Design.Monospaced).Apply(t); return t; }
        hash = Mono(12, Weight.Medium); hash.Text = "#";
        HexBox = new TextBox
        {
            MaxLength = 9, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            MinHeight = 0, Height = HexHeight, VerticalContentAlignment = VerticalAlignment.Center,
        };
        SwiftFont.System(12.5, Weight.Medium, SwiftFont.Design.Monospaced).Apply(HexBox);
        AutomationProperties.SetName(HexBox, "Hex colour");
        // the template's own focus and hover fills are the design's job to remove: the pill carries them (.mg-hex:focus-within)
        HexBox.Styles.Add(new Style(x => x.OfType<TextBox>().Template().OfType<Border>().Name("PartBorderElement")) { Setters = { new Setter(BackgroundProperty, Brushes.Transparent), new Setter(Border.BorderThicknessProperty, new Thickness(0)) } });
        Percent = Mono(11.5, Weight.Medium); Percent.Width = PercentWidth; Percent.TextAlignment = TextAlignment.Right;
        Dot = new Border { Width = DotSize, Height = DotSize, CornerRadius = new CornerRadius(DotSize / 2), BorderThickness = new Thickness(PickerSurface.RimWidth) };
        caption = new TextBlock { Text = "Recent", VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(11, Weight.Semibold).Apply(caption);
        var inner = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(hash, Dock.Left);
        inner.Children.Add(hash); inner.Children.Add(HexBox);
        inner.Margin = new Thickness(0);
        hash.Margin = new Thickness(0, 0, HexInnerGap, 0);
        Pill = new Border { Child = inner, Height = HexHeight, CornerRadius = new CornerRadius(HexRadius), Padding = new Thickness(HexPadding, 0), BorderThickness = new Thickness(PickerSurface.RimWidth) };
        foreach (var surface in new[] { Square, Hue, OpacityStrip, RecentRow }) surface.Edit = Apply;
        HexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { EnterHex(); e.Handled = true; } };
        HexBox.LostFocus += (_, _) => { if (HexBox.Text != Canonical) EnterHex(); ApplyTheme(); };
        HexBox.GotFocus += (_, _) => ApplyTheme();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        Children.AddRange([Square, Hue, OpacityStrip, Pill, Percent, Dot, caption, RecentRow]);
        Sync();
    }

    /// <summary>The colour changed by the user: the stored "#RRGGBBAA", and whether it is committed (a release, a hex entry, a recent click).</summary>
    public event Action<string, bool>? Changed;

    /// <summary>The colour as stored, "#RRGGBBAA". Setting it (a "#RRGGBB" reads as opaque; anything else is ignored) moves the picker without raising <see cref="Changed"/>.</summary>
    public string Value
    {
        get => PickerMath.Stored(colour);
        set
        {
            if (PickerMath.FromStored(value) is not { } next) return;
            colour = next;
            Sync();
        }
    }

    /// <summary>The recent colours ("#RRGGBB", newest first); a colour joins on a commit.</summary>
    public PickerRecent Recent
    {
        get => recent;
        set { recent = value; Sync(); }
    }

    private string Canonical => PickerMath.HsvHex(colour)[1..];

    /// <summary>set(c, commit): the design's one way a colour changes.</summary>
    private void Apply(PickerColor next, bool commit)
    {
        colour = next;
        if (commit) recent.Commit(PickerMath.HsvHex(colour));
        Sync();
        Changed?.Invoke(PickerMath.Stored(colour), commit);
    }

    /// <summary>The hex field's change: a good entry sets the colour (a six-digit one keeps the alpha), a bad one shows the colour again.</summary>
    internal void EnterHex()
    {
        if (PickerMath.ParseHexEntry(HexBox.Text) is { } entry) Apply(PickerMath.FromHex(entry.Rgb, entry.Alpha ?? colour.A), true);
        else HexBox.Text = Canonical;
    }

    private void Sync()
    {
        foreach (var surface in new[] { Square, Hue, OpacityStrip, RecentRow }) surface.Colour = colour;
        RecentRow.Recent = recent.Items;
        HexBox.Text = Canonical;
        Percent.Text = PickerMath.JsRound(colour.A * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + " %";
        var rgb = PickerMath.HsvRgb(colour.H, colour.S, colour.V);
        byte B(double v) => (byte)PickerMath.Clamp(PickerMath.JsRound(v), 0, 255);
        Dot.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(colour.A * 255), B(rgb[0]), B(rgb[1]), B(rgb[2])));
        ApplyTheme();
    }

    /// <summary>The theme-dependent fills: the pill (.mg-hex, and :focus-within), the dot's edge, the secondary text.</summary>
    private void ApplyTheme()
    {
        var focused = HexBox.IsKeyboardFocusWithin || HexBox.IsFocused;
        var light = PickerTheme.Light(this);
        // .mg-hex:focus-within: --caph and a 1 px --accent edge (the platform accent stands in for the page's --accent)
        Pill.Background = new SolidColorBrush(focused ? (light ? Color.FromArgb(0x1A, 0, 0, 0) : Color.FromArgb(0x52, 0, 0, 0)) : PickerTheme.Cap(this));
        Pill.BorderBrush = new SolidColorBrush(focused ? PlatformAccent() : PickerTheme.Rim(this));
        Pill.BorderThickness = new Thickness(focused ? 1 : PickerSurface.RimWidth);
        Dot.BorderBrush = new SolidColorBrush(PickerTheme.Rim(this));
        hash.Opacity = Percent.Opacity = caption.Opacity = PickerTheme.Secondary(this);
    }

    private static Color PlatformAccent() => Application.Current?.PlatformSettings?.GetColorValues().AccentColor1 ?? Color.FromRgb(0x00, 0x78, 0xD7);

    /// <summary>The height of the whole picker: .mg's padding, each part and the flex gap after it, the caption's -4 px margin.</summary>
    internal static double FullHeight => Padding + PickerSurface.SquareHeight + Gap + PickerSurface.StripHeight + Gap + PickerSurface.StripHeight + Gap + HexHeight + Gap
        + CaptionHeight + (Gap + CaptionMargin) + PickerSurface.SwatchBox + Padding;

    private static double InnerWidth => PickerWidth - 2 * Padding;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(new Size(InnerWidth, double.PositiveInfinity));
        return new Size(PickerWidth, FullHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var w = InnerWidth;
        var y = Padding;
        Square.Arrange(new Rect(Padding, y, w, PickerSurface.SquareHeight)); y += PickerSurface.SquareHeight + Gap;
        Hue.Arrange(new Rect(Padding, y, w, PickerSurface.StripHeight)); y += PickerSurface.StripHeight + Gap;
        OpacityStrip.Arrange(new Rect(Padding, y, w, PickerSurface.StripHeight)); y += PickerSurface.StripHeight + Gap;
        // .mg-hexrow: the pill takes what is left (flex: 1), then the percent (min-width 42), then the 22 px dot, 8 px apart
        var pill = w - PercentWidth - DotSize - 2 * HexRowGap;
        Pill.Arrange(new Rect(Padding, y, pill, HexHeight));
        Percent.Arrange(new Rect(Padding + pill + HexRowGap, y, PercentWidth, HexHeight));
        Dot.Arrange(new Rect(Padding + pill + 2 * HexRowGap + PercentWidth, y + (HexHeight - DotSize) / 2, DotSize, DotSize)); y += HexHeight + Gap;
        caption.Arrange(new Rect(Padding, y, w, CaptionHeight)); y += CaptionHeight + Gap + CaptionMargin;
        RecentRow.Arrange(new Rect(Padding, y, w, PickerSurface.SwatchBox));
        return new Size(PickerWidth, FullHeight);
    }
}

// OD-0099 Look Picks, Q-CZ-3 B (owner pick): Marrow's own glass colour picker for the Uiverse light's Custom colour wells, drawn after
// lp-pickers.js variant B (pickerB): a saturation-brightness square, a hue strip, an opacity strip, a hex field with the opacity in
// percent and a dot of the colour, and the recent colours. It is the content of a PopoverCard (OD-0095): the host's card carries the
// arrow tip; the popover is the picker's width plus 12. The colour is held as the design holds it (hue, saturation, value, alpha) and
// handed out as "#RRGGBBAA", the form the Uiverse custom colours are stored in; a "#RRGGBB" given to it reads as opaque.
// Layout numbers the design's script does not fix (its stylesheet is not in the repository) are ENGINEERING DECISIONS, listed under
// ED-NEW-f-cz-2 in porting/engineering_decisions.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port.Settings.ColorPicker;

namespace Marrow.UI.Port.Settings;

/// <summary>The glass colour picker (see the file note).</summary>
public sealed class GlassColorPicker : Panel
{
    /// <summary>The picker's width (pickerB: w 260); the popover around it is this plus 12.</summary>
    public const double PickerWidth = 260;

    internal const double Inset = 12;
    internal const double Gap = 12;
    internal const double StripGap = 8;
    internal const double HexRowHeight = 28;
    internal const double CaptionHeight = 14;
    internal const double CaptionGap = 6;
    internal const double DotSize = 16;

    private PickerColor colour = PickerMath.FromHex("#8E7CFF");
    private PickerRecent recent = new();

    internal PickerSurface Square { get; } = new(PickerSurfaceKind.Square);
    internal PickerSurface Hue { get; } = new(PickerSurfaceKind.Hue);
    internal PickerSurface OpacityStrip { get; } = new(PickerSurfaceKind.Opacity);
    internal PickerSurface RecentRow { get; } = new(PickerSurfaceKind.Recent);
    internal TextBox HexBox { get; }
    internal TextBlock Percent { get; }
    internal Border Dot { get; }
    private readonly TextBlock hash, caption;

    public GlassColorPicker()
    {
        Width = PickerWidth;
        hash = new TextBlock { Text = "#", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        HexBox = new TextBox { MaxLength = 9, FontSize = 12, Height = HexRowHeight, MinHeight = HexRowHeight, Padding = new Thickness(6, 0), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(HexBox, "Hex colour");
        Percent = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        Dot = new Border { Width = DotSize, Height = DotSize, CornerRadius = new CornerRadius(DotSize / 2) };
        caption = new TextBlock { Text = "Recent", FontSize = 11, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var surface in new[] { Square, Hue, OpacityStrip, RecentRow }) surface.Edit = Apply;
        HexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { EnterHex(); e.Handled = true; } };
        HexBox.LostFocus += (_, _) => { if (HexBox.Text != Canonical) EnterHex(); };
        Children.AddRange([Square, Hue, OpacityStrip, hash, HexBox, Percent, Dot, caption, RecentRow]);
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
    }

    /// <summary>The height of the whole picker: its inset, then each part and the gap under it.</summary>
    internal static double FullHeight => Inset + PickerSurface.SquareHeight + Gap + PickerSurface.StripHeight + StripGap + PickerSurface.StripHeight + Gap + HexRowHeight + Gap
        + CaptionHeight + CaptionGap + PickerSurface.SwatchSize + Inset;

    private double InnerWidth => PickerWidth - 2 * Inset;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(new Size(InnerWidth, double.PositiveInfinity));
        return new Size(PickerWidth, FullHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var w = InnerWidth;
        var y = Inset;
        Square.Arrange(new Rect(Inset, y, w, PickerSurface.SquareHeight)); y += PickerSurface.SquareHeight + Gap;
        Hue.Arrange(new Rect(Inset, y, w, PickerSurface.StripHeight)); y += PickerSurface.StripHeight + StripGap;
        OpacityStrip.Arrange(new Rect(Inset, y, w, PickerSurface.StripHeight)); y += PickerSurface.StripHeight + Gap;
        // the hex row: "#", the field, the opacity in percent, the dot of the colour at the right
        const double hashWidth = 12, boxWidth = 100, pctGap = 12;
        hash.Arrange(new Rect(Inset, y, hashWidth, HexRowHeight));
        HexBox.Arrange(new Rect(Inset + hashWidth, y, boxWidth, HexRowHeight));
        Percent.Arrange(new Rect(Inset + hashWidth + boxWidth + pctGap, y, Math.Max(0, w - hashWidth - boxWidth - pctGap - DotSize - pctGap), HexRowHeight));
        Dot.Arrange(new Rect(Inset + w - DotSize, y + (HexRowHeight - DotSize) / 2, DotSize, DotSize)); y += HexRowHeight + Gap;
        caption.Arrange(new Rect(Inset, y, w, CaptionHeight)); y += CaptionHeight + CaptionGap;
        RecentRow.Arrange(new Rect(Inset, y, w, PickerSurface.SwatchSize));
        return new Size(PickerWidth, FullHeight);
    }
}

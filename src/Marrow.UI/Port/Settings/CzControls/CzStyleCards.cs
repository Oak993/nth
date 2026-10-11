// PORT-ONLY CZ-1: the Light radio cards (CZ-5), exact locked titles/descriptions and animated glyphs. OD-0139: Border beam, the Uiverse light,
// the shader lights of the catalogue (Composer Light v2) and Off, three to a row.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port.Settings;
namespace Marrow.UI.Port.Settings;

public sealed class CzStyleCards : CzRadioGroup
{
    public IReadOnlyList<CzStyleGlyph> Glyphs { get; }
    private readonly List<Border> shells = [];
    private readonly List<CzNumber> rings = [];
    private readonly List<CzColor> fills = [];
    public CzStyleCards(string selected) : base("Light", CzText.StyleTitles.Select(p => (p.Key, p.Value)).ToArray(), selected)
    {
        Spacing = 8; HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        Glyphs = Buttons.Select(b => new CzStyleGlyph(b.Id)).ToArray();
        for (var i = 0; i < Buttons.Count; i++)
        {
            var button = Buttons[i]; button.Height = double.NaN; button.Padding = new Thickness(0); button.PressScale = .98;
            button.CornerRadius = new CornerRadius(12); button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            var stack = new StackPanel { Spacing = 3 };
            stack.Children.Add(Glyphs[i]);
            stack.Children.Add(new TextBlock { Text = CzText.StyleTitles[button.Id], FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 13, FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(4, 5, 4, 0), Foreground = CzTheme.Text(this) });
            stack.Children.Add(new TextBlock { Text = CzText.StyleDescriptions[button.Id], FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 11.5, LineHeight = 15,
                Margin = new Thickness(4, 0), Foreground = CzTheme.Text(this, true), TextWrapping = TextWrapping.Wrap });
            var shell = new Border { Padding = new Thickness(8, 8, 8, 11), CornerRadius = new CornerRadius(12), Child = stack };
            button.Child = shell; shells.Add(shell);
            AutomationProperties.SetHelpText(button, CzText.StyleDescriptions[button.Id]);
            var ring = new CzNumber(button.Motion, button.Picked ? 2 : 0, v => { shell.BorderThickness = new Thickness(v); shell.Padding = new Thickness(8 - v, 8 - v, 8 - v, 11 - v); });
            rings.Add(ring); ring.Jump(button.Picked ? 2 : 0);
            var color = CzTheme.Token(this, button.Picked ? "tilesel" : "cardfill");
            shell.Background = CzTheme.Brush(color);
            fills.Add(new CzColor(button.Motion, color, c => shell.Background = CzTheme.Brush(c)));
            button.PointerEntered += (_, _) => RefreshCards();
            button.PointerExited += (_, _) => RefreshCards();
        }
        SelectionChanged += _ => RefreshCards();
        PropertyChanged += (_, e) => { if (e.Property.Name == "ActualThemeVariant") RefreshCards(); };
        RefreshCards();
    }
    private void RefreshCards()
    {
        for (var i = 0; i < Buttons.Count; i++)
        {
            var b = Buttons[i]; var shell = shells[i];
            var text = ((StackPanel)shell.Child!).Children.OfType<TextBlock>().ToArray();
            text[0].Foreground = CzTheme.Text(this); text[1].Foreground = CzTheme.Text(this, true);
            fills[i].To(CzTheme.Token(this, b.Picked ? "tilesel" : b.IsPointerOver ? "tile" : "cardfill"), CzMotion.Ease(.2));
            shell.BorderBrush = CzTheme.Brush(CzTheme.Token(this, "sel")); rings[i].To(b.Picked ? 2 : 0, CzMotion.Ease(.2));
        }
    }
    /// <summary>Cards in a row (OD-0139: the rows wrap, each as tall as its tallest card).</summary>
    public const int PerRow = 3;
    private const double Gap = 8;
    private double RowHeight(int row) => Buttons.Skip(row * PerRow).Take(PerRow).Max(b => b.DesiredSize.Height);
    private int Rows => (Buttons.Count + PerRow - 1) / PerRow;
    protected override Size MeasureOverride(Size availableSize)
    {
        var column = double.IsFinite(availableSize.Width) ? Math.Max(0, (availableSize.Width - Gap * (PerRow - 1)) / PerRow) : 160;
        foreach (var b in Buttons) b.Measure(new Size(column, double.PositiveInfinity));
        var height = Enumerable.Range(0, Rows).Sum(RowHeight) + Gap * (Rows - 1);
        return new Size(column * PerRow + Gap * (PerRow - 1), height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var column = Math.Max(0, (finalSize.Width - Gap * (PerRow - 1)) / PerRow);
        var y = 0.0;
        for (var row = 0; row < Rows; row++)
        {
            var h = RowHeight(row);
            for (var k = 0; k < PerRow && row * PerRow + k < Buttons.Count; k++) Buttons[row * PerRow + k].Arrange(new Rect(k * (column + Gap), y, column, h));
            y += h + Gap;
        }
        return finalSize;
    }
}

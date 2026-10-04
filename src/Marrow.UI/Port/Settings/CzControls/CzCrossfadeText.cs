// PORT-ONLY CZ-1: overlapping text in one cell, outgoing .1s and incoming .2s after .08s (CZ-12.9).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public sealed class CzCrossfadeText : Panel
{
    private readonly CzMotion motion;
    private readonly Dictionary<TextBlock, CzNumber> fades = [];
    private TextBlock current;
    private TextBlock? previous;
    private readonly CzNumber height;
    private bool measured;
    public bool Wrap { get; set; }
    public double FontSize { get; set; } = 12;
    public FontWeight FontWeight { get; set; } = FontWeight.Normal;
    public bool Secondary { get; set; }
    public string Text => current.Text!;
    public double IncomingOpacity => current.Opacity;
    public double OutgoingOpacity => previous?.Opacity ?? 0;
    public CzCrossfadeText(string text)
    {
        ActualThemeVariantChanged += (_, _) => InvalidateMeasure();
        motion = new CzMotion(this); current = Make(text); current.Opacity = 1;
        height = new CzNumber(motion, 0, _ => InvalidateMeasure()); ClipToBounds = true;
    }
    private TextBlock Make(string text)
    {
        var block = new TextBlock { Text = text, FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = FontSize,
            FontWeight = FontWeight, Foreground = CzTheme.Text(this, Secondary), TextWrapping = Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
        Children.Add(block); fades.Add(block, new CzNumber(motion, 0, v => block.Opacity = v)); return block;
    }
    public void SetText(string text)
    {
        if (Text == text) return;
        previous = current;
        foreach (var block in Children.OfType<TextBlock>().ToArray())
        {
            if (block.Opacity == 0) { Children.Remove(block); fades.Remove(block); }
            else { fades[block].Jump(block.Opacity); fades[block].To(0, CzMotion.Ease(.1)); }
        }
        current = Make(text); current.Opacity = 0; fades[current].To(1, CzMotion.Ease(.2), .08); InvalidateMeasure();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var block in Children.OfType<TextBlock>())
        {
            block.FontSize = FontSize; block.FontWeight = FontWeight; block.Foreground = CzTheme.Text(this, Secondary);
            block.TextWrapping = Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap; block.Measure(availableSize);
        }
        var natural = current.DesiredSize;
        if (!measured) { height.Jump(natural.Height); measured = true; } else height.To(natural.Height, CzMotion.Ease(.34));
        return new Size(natural.Width, height.Value);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var block in Children.OfType<TextBlock>()) block.Arrange(new Rect(0, 0, Math.Max(finalSize.Width, block.DesiredSize.Width), block.DesiredSize.Height));
        return finalSize;
    }
}

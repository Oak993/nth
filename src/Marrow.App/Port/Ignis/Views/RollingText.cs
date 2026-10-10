// PORT-ONLY: Ignis U4 (OD-0134, ED-NEW-ignis-views). The Kindle's countdown (".k-roll", kRoll): digits roll like an odometer,
// only the characters that changed move; the old one slides down 80 % and fades, the new one comes from 80 % above, 0.42 s on
// the design's smooth curve, inside a mask that fades the top and bottom fifth. A change of length (or Reduce Motion) swaps the
// text at once. Tabular figures keep the width steady.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port;

namespace Marrow.App.Port.Ignis.Views;

internal sealed class RollingText : StackPanel
{
    public const double LineHeight = 18;
    private readonly Func<TextBlock> make;
    private string text = "";

    public RollingText(Func<TextBlock> make)
    {
        this.make = make;
        Orientation = Orientation.Horizontal;
        Height = LineHeight;
        MinWidth = 24;
        HorizontalAlignment = HorizontalAlignment.Right;
        OpacityMask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, .22), new GradientStop(Colors.Black, .78), new GradientStop(Colors.Transparent, 1)],
        };
    }

    /// <summary>The text on show (after the roll).</summary>
    public string Text => text;

    /// <summary>Whether a character is rolling now (tests).</summary>
    public bool IsRolling => Children.OfType<Panel>().Any(cell => cell.Children.Count > 1);

    public void Show(string next, bool animate)
    {
        if (next == text) return;
        var previous = text;
        text = next;
        var roll = animate && previous.Length == next.Length && previous.Length > 0 && !KindleMotion.Reduce(this);
        if (!roll)
        {
            Children.Clear();
            foreach (var c in next) Children.Add(Cell(c));
            return;
        }
        for (var i = 0; i < next.Length; i++)
        {
            if (previous[i] == next[i] || Children[i] is not Panel cell) continue;
            foreach (var stale in cell.Children.Skip(1).ToList()) cell.Children.Remove(stale);
            var old = cell.Children.Count > 0 ? (TextBlock)cell.Children[0] : null;
            var arriving = Glyph(next[i]);
            cell.Children.Add(arriving);
            var motion = new AnimatedScalar(arriving, 0, p =>
            {
                arriving.RenderTransform = new TranslateTransform(0, -.8 * LineHeight * (1 - p));
                arriving.Opacity = p;
                if (old is not null)
                {
                    old.RenderTransform = new TranslateTransform(0, .8 * LineHeight * p);
                    old.Opacity = 1 - p;
                    if (p >= 1) cell.Children.Remove(old);
                }
            });
            motion.Set(1, KindleMotion.Smooth(.42));
        }
    }

    private Panel Cell(char c) => new() { ClipToBounds = true, Height = LineHeight, Children = { Glyph(c) } };

    private TextBlock Glyph(char c)
    {
        var glyph = make();
        glyph.Text = c.ToString();
        glyph.LineHeight = LineHeight;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        return glyph;
    }
}

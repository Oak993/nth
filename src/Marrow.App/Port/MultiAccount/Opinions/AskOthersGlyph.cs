// PORT-ONLY: locked page :4795, two overlapping circles, 16px viewbox, stroke 1.3.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class AskOthersGlyph : Control
{
    internal AskOthersGlyph() { Width = Height = 16; IsHitTestVisible = false; }
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(ChromeTokens.SecondaryText.Brush(this), 1.3);
        context.DrawEllipse(null, pen, new Point(6, 8), 3.6, 3.6);
        context.DrawEllipse(null, pen, new Point(10.5, 8), 3.6, 3.6);
    }
}

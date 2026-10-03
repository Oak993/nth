using Avalonia;
using Avalonia.Media;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

// A MorphingTextBlock that counts how often its text is drawn (MorphingTextBlockCostTests: the drift draws it once).
public sealed class CountedMorphingTextBlock : MorphingTextBlock
{
    public int Renders { get; private set; }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        Renders++;
        base.RenderTextLayout(context, origin);
    }
}

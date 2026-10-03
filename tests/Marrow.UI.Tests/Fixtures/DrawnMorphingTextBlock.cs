using Avalonia;
using Avalonia.Media;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

// A MorphingTextBlock that can be asked what it draws now into a drawing context of the test's own (MorphingTextBlockLayersTests:
// a DrawingGroup records the opacity layers the label pushes round each letter).
public sealed class DrawnMorphingTextBlock : MorphingTextBlock
{
    public void DrawInto(DrawingContext context) => RenderTextLayout(context, new Point(Padding.Left, Padding.Top));
}

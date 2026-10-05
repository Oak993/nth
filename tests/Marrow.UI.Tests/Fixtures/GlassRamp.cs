// ED-NEW-glass-g1: the grey ramp GlassMaterialLiquidLookTests reads the bend and the fringe from.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.UI.Tests.Fixtures;

// A ramp of greys, level = x - offset (clamped), or its reverse: an affine backdrop the blur keeps, so a pixel's level says where it was read.
internal sealed class GlassRamp(bool reversed, int offset) : Control
{
    public override void Render(DrawingContext context)
    {
        for (var x = 0; x < (int)Bounds.Width; x++)
        {
            var level = (byte)Math.Clamp(reversed ? 255 - (x - offset) : x - offset, 0, 255);
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(level, level, level)), null, new Rect(x, 0, 1, Bounds.Height));
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>A flat colour drawn by its own Render, the kind of backdrop the headless glass material samples (as GlassBackdropPattern).</summary>
internal sealed class SolidBackdrop(Color color) : Control
{
    public override void Render(DrawingContext context) => context.DrawRectangle(new SolidColorBrush(color), null, new Rect(Bounds.Size));
}

using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;

namespace Marrow.UI.Tests.Fixtures;

internal sealed class BootHandlerDrawOperation(Action<ImmediateDrawingContext> read) : ICustomDrawOperation
{
    public Rect Bounds => new(0, 0, 64, 64);
    public bool HitTest(Point point) => false;
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose() { }
    public void Render(ImmediateDrawingContext context) => read(context);
}

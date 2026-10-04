using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;

namespace Marrow.App.Tests.Fixtures;

/// <summary>GR-1 tests: a custom draw operation that hands the drawing context to the test while it renders.</summary>
public sealed class ReadingDrawOperation(Action<ImmediateDrawingContext> read) : ICustomDrawOperation
{
    public Rect Bounds => new(0, 0, 1, 1);
    public bool HitTest(Point p) => false;
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose() { }
    public void Render(ImmediateDrawingContext context) => read(context); // an exception here is logged, not thrown: the test reads what it saw
}

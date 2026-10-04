using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;

namespace Marrow.App.Tests.Fixtures;

/// <summary>GR-1 tests: a custom draw operation that draws nothing and throws nothing.</summary>
public sealed class QuietDrawOperation : ICustomDrawOperation
{
    public Rect Bounds => new(0, 0, 1, 1);
    public bool HitTest(Point p) => false;
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose() { }
    public void Render(ImmediateDrawingContext context) { }
}

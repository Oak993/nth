namespace Marrow.UI.Tests.Fixtures;

internal sealed class ToolTipBrushLifetime : IDisposable
{
    internal int Disposals { get; private set; }
    public void Dispose() => Disposals++;
}

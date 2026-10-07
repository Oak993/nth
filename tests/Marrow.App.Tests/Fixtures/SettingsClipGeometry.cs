using System.Numerics;

namespace Marrow.App.Tests.Fixtures;

public sealed class SettingsClipGeometry : IDisposable
{
    public Vector2 Offset { get; private set; }
    public Vector2 Size { get; private set; }
    public Vector2 Radius { get; private set; }
    public int Disposals { get; private set; }
    public bool RejectOffset { get; init; }
    public void SetOffset(Vector2 value) { if (RejectOffset) throw new InvalidOperationException("clip rejected"); Offset = value; }
    public void SetSize(Vector2 value) => Size = value;
    public void SetCornerRadius(Vector2 value) => Radius = value;
    public void Dispose() => Disposals++;
}

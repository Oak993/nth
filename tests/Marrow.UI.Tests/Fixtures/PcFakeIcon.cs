// PORT-ONLY: a stand-in app icon for the Tactus icon checks (ED-NEW-pcctl-46): an image of a fixed size that draws nothing.
using Avalonia;
using Avalonia.Media;

namespace Marrow.UI.Tests.Fixtures;

public sealed class PcFakeIcon(string name) : IImage
{
    public string Name { get; } = name;

    public Size Size { get; } = new(96, 96);

    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
    {
    }

    public override string ToString() => Name;
}

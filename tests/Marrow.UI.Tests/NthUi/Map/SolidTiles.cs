using Avalonia;
using Avalonia.Media;
using Marrow.UI.NthUi.Map;

namespace Marrow.UI.Tests.NthUi.Map;

/// <summary>ED-NEW-web-w16c-1: a tile source whose every tile is one colour, for the tile seam's tests.</summary>
internal sealed class SolidTiles(Color color, int maxZoom) : INthUiMapTiles
{
    private readonly Avalonia.Media.Imaging.RenderTargetBitmap tile = Make(color);

    public int Asked { get; private set; }

    public int MaxZoom { get; } = maxZoom;

    public string Attribution => "Test tiles";

    public IImage? Tile(int zoom, int x, int y)
    {
        Asked++;
        return tile;
    }

    public event Action? TilesChanged
    {
        add { }
        remove { }
    }

    private static Avalonia.Media.Imaging.RenderTargetBitmap Make(Color color)
    {
        var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(256, 256));
        using (var context = bitmap.CreateDrawingContext(true)) context.FillRectangle(new SolidColorBrush(color), new Rect(0, 0, 256, 256));
        return bitmap;
    }
}

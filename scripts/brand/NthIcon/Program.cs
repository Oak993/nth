// OD-0136: renders the locked nth icon B ("Exponent") with the app's own NthMark and writes the shipped icon files.
//   icon.ico   16, 20, 24 (mkBs, the thicker cut) and 32, 40, 48, 64, 96, 128, 256 (mkB), each a PNG entry
//   icon.png   512 (mkB)                                  src/Marrow.App/Assets
//   marrow-icon.png 256 and marrow-icon@2x.png 512 (mkB)  src/Marrow.UI/Assets/Images ("droppy-logo")
// Every size is rendered at its own pixel size (never scaled down from a larger one). Where the design's 8/256 margin is
// not a whole number of pixels (16, 20, 24, 40, 48) the tile is fitted to whole pixels, a margin of round(N/32) and at
// least 1, so its edges are sharp. Up to 48 px the n is fitted the way a font is hinted: its stems keep a whole number
// of pixels' width (the cut's width rounded, at least 2) and start on a pixel edge, the arc spans the fitted stems and its
// top lands on a pixel edge, the foot ends on one; the bead keeps the symbol's centre and size. 64 px and up are unfitted.
// Usage: NthIcon <repository root> [review folder for one PNG per size]
using SkiaSharp;
using Marrow.UI.Port;

if (args.Length is < 1 or > 2) { Console.Error.WriteLine("usage: NthIcon <repository root> [review folder]"); return 2; }
var root = Path.GetFullPath(args[0]);
var review = args.Length == 2 ? Directory.CreateDirectory(args[1]).FullName : null;
var app = Path.Combine(root, "src", "Marrow.App", "Assets");
var images = Path.Combine(root, "src", "Marrow.UI", "Assets", "Images");
if (!Directory.Exists(app) || !Directory.Exists(images)) { Console.Error.WriteLine($"not the repository root: {root}"); return 2; }

using var mark = new NthMark();
int[] icoSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
var entries = icoSizes.Select(size => (size, png: Render(mark, size))).ToList();
WriteIco(Path.Combine(app, "icon.ico"), entries);
File.WriteAllBytes(Path.Combine(app, "icon.png"), Render(mark, 512));
File.WriteAllBytes(Path.Combine(images, "marrow-icon.png"), Render(mark, 256));
File.WriteAllBytes(Path.Combine(images, "marrow-icon@2x.png"), Render(mark, 512));
if (review is not null)
{
    foreach (var (size, png) in entries) File.WriteAllBytes(Path.Combine(review, $"nth-{size}.png"), png);
    File.WriteAllBytes(Path.Combine(review, "nth-512.png"), Render(mark, 512));
}
Console.WriteLine($"wrote icon.ico ({string.Join(", ", icoSizes)}), icon.png 512, marrow-icon.png 256, marrow-icon@2x.png 512");
return 0;

static byte[] Render(NthMark mark, int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul))
        ?? throw new InvalidOperationException("no surface");
    surface.Canvas.Clear(SKColors.Transparent);
    var box = Box(size);
    var cut = size <= 24 ? NthGlyph.Small : NthGlyph.Regular;
    mark.Draw(surface.Canvas, box, NthTileStyle.Icon, size <= 48 ? Fitted(box, cut) : cut, NthPose.Still);
    surface.Canvas.Flush();
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// The 256-unit symbol box for an N px icon: exact when N is a multiple of 32, otherwise the 240-unit tile fitted to whole pixels.
static SKRect Box(int size)
{
    if (size % 32 == 0) return new SKRect(0, 0, size, size);
    var margin = Math.Max(1, (int)Math.Floor(size / 32.0 + .5));
    var unit = (size - 2.0 * margin) / NthMark.TileSide;
    var left = margin - NthMark.TileInset * unit;
    return new SKRect((float)left, (float)left, (float)(left + NthMark.Box * unit), (float)(left + NthMark.Box * unit));
}

// A cut fitted to the pixel grid of its box (see the file note); returned in symbol units.
static NthGlyph Fitted(SKRect box, NthGlyph g)
{
    var unit = box.Width / NthMark.Box;
    double Px(double units) => box.Left + units * unit;
    float Units(double px) => (float)((px - box.Left) / unit);
    var width = Math.Max(2, Math.Round(g.Width * unit));
    var left = Math.Round(Px(g.Left) - width / 2) + width / 2;
    var right = Math.Round(Px(g.Right) - width / 2) + width / 2;
    var radius = (right - left) / 2;
    var top = Math.Round(Px(g.Top) - radius - width / 2) + radius + width / 2;
    var bottom = Math.Round(Px(g.Bottom));
    return g with { Left = Units(left), Right = Units(right), Top = Units(top), Bottom = Units(bottom), Width = (float)(width / unit) };
}

static void WriteIco(string path, List<(int Size, byte[] Png)> entries)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)entries.Count); // ICONDIR: reserved, type 1 (icon), count
    var offset = 6 + 16 * entries.Count;
    foreach (var (size, png) in entries)
    {
        writer.Write((byte)(size >= 256 ? 0 : size)); writer.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
        writer.Write((byte)0); writer.Write((byte)0); // no palette, reserved
        writer.Write((ushort)1); writer.Write((ushort)32); // planes, bits per pixel
        writer.Write(png.Length); writer.Write(offset);
        offset += png.Length;
    }
    foreach (var (_, png) in entries) writer.Write(png);
}

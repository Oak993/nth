using Avalonia;
using Avalonia.Media;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A PNG written from a pixel function, for the tests that mount a picture (the tool call's image preview, the attachment plates).</summary>
internal static class TestPng
{
    public static void Write(string path, int width, int height, Func<int, int, Color> pixel)
    {
        var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        using (var buffer = bitmap.Lock())
        {
            var row = new byte[width * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var c = pixel(x, y);
                    row[x * 4] = c.B; row[x * 4 + 1] = c.G; row[x * 4 + 2] = c.R; row[x * 4 + 3] = 255;
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
            }
        }
        using (var stream = File.Create(path)) bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Marrow.App.Tests.Fixtures;

/// <summary>One captured frame of a glide test, copied out so it can be read after the next step.</summary>
public sealed class RowGlideSnapshot
{
    private readonly byte[] red;
    private readonly byte[] green;
    private readonly byte[] blue;

    public RowGlideSnapshot(WriteableBitmap frame)
    {
        Width = frame.PixelSize.Width;
        Height = frame.PixelSize.Height;
        red = new byte[Width * Height];
        green = new byte[Width * Height];
        blue = new byte[Width * Height];
        using var buffer = frame.Lock();
        var row = new byte[buffer.RowBytes];
        for (var y = 0; y < Height; y++)
        {
            Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, buffer.RowBytes);
            for (var x = 0; x < Width; x++)
            {
                var swap = buffer.Format == PixelFormat.Bgra8888;
                red[y * Width + x] = row[x * 4 + (swap ? 2 : 0)];
                green[y * Width + x] = row[x * 4 + 1];
                blue[y * Width + x] = row[x * 4 + (swap ? 0 : 2)];
            }
        }
    }

    public int Width { get; }

    public int Height { get; }

    public (int R, int G, int B) At(int x, int y) => (red[y * Width + x], green[y * Width + x], blue[y * Width + x]);

    /// <summary>The smallest rectangle holding every pixel the predicate accepts; null when none does.</summary>
    public Rect? BoundsOf(Func<(int R, int G, int B), bool> accepts)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (!accepts(At(x, y))) continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }
        return maxX < 0 ? null : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>The mean position of the pixels the predicate accepts within <paramref name="area"/>; null when none does.</summary>
    public Point? CentreOf(Rect area, Func<(int R, int G, int B), bool> accepts)
    {
        double sumX = 0, sumY = 0;
        var count = 0;
        for (var y = (int)area.Top; y < (int)area.Bottom; y++)
        {
            for (var x = (int)area.Left; x < (int)area.Right; x++)
            {
                if (!accepts(At(x, y))) continue;
                sumX += x + 0.5;
                sumY += y + 0.5;
                count++;
            }
        }
        return count == 0 ? null : new Point(sumX / count, sumY / count);
    }
}

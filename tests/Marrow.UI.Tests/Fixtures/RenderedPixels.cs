using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>A rendered headless frame as rows of (red, green, blue) bytes.</summary>
public sealed class RenderedPixels
{
    private readonly byte[] bytes;
    private readonly int rowBytes;

    public int Width { get; }
    public int Height { get; }

    private RenderedPixels(byte[] bytes, int rowBytes, int width, int height)
    {
        this.bytes = bytes;
        this.rowBytes = rowBytes;
        Width = width;
        Height = height;
    }

    public static RenderedPixels Capture(TopLevel window)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var data = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, data, 0, data.Length);
        return new RenderedPixels(data, buffer.RowBytes, buffer.Size.Width, buffer.Size.Height);
    }

    /// <summary>A rectangle of the frame.</summary>
    public RenderedPixels Crop(int x, int y, int width, int height)
    {
        var data = new byte[width * 4 * height];
        for (var row = 0; row < height; row++)
            Array.Copy(bytes, (y + row) * rowBytes + x * 4, data, row * width * 4, width * 4);
        return new RenderedPixels(data, width * 4, width, height);
    }

    /// <summary>(red, green, blue) at a pixel (Rgba8888 on the headless surface).</summary>
    public (int Red, int Green, int Blue) At(int x, int y)
    {
        var i = y * rowBytes + x * 4;
        return (bytes[i], bytes[i + 1], bytes[i + 2]);
    }

    /// <summary>The strongest channel at a pixel: how much is drawn there over black.</summary>
    public int Level(int x, int y)
    {
        var (r, g, b) = At(x, y);
        return Math.Max(r, Math.Max(g, b));
    }

    /// <summary>The first and last rows holding a pixel above <paramref name="threshold"/>, or null when none does.</summary>
    public (int Top, int Bottom)? InkRows(int threshold = 24)
    {
        int? top = null, bottom = null;
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                if (Level(x, y) > threshold) { top ??= y; bottom = y; break; }
        return top is { } t && bottom is { } b ? (t, b) : null;
    }
}

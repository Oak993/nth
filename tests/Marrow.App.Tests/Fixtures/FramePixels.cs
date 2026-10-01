using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Reads pixels of a rendered headless frame.</summary>
public static class FramePixels
{
    /// <summary>The colour at each point as (red, green, blue), divided by its strongest channel: what is drawn, whatever its opacity over black.</summary>
    public static (double Red, double Green, double Blue)[] Hues(TopLevel window, params (int X, int Y)[] points)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var row = new byte[buffer.RowBytes];
        var hues = new List<(double, double, double)>();
        foreach (var (x, y) in points)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, buffer.RowBytes);
            // Rgba8888 on this headless surface
            double r = row[x * 4], g = row[x * 4 + 1], b = row[x * 4 + 2];
            var strongest = Math.Max(1, Math.Max(r, Math.Max(g, b)));
            hues.Add((r / strongest, g / strongest, b / strongest));
        }
        return hues.ToArray();
    }

    /// <summary>The strongest channel at each point, as a fraction of 255: how bright what is drawn is over black.</summary>
    public static double[] Levels(TopLevel window, params (int X, int Y)[] points)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var row = new byte[buffer.RowBytes];
        var levels = new List<double>();
        foreach (var (x, y) in points)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, buffer.RowBytes);
            levels.Add(Math.Max(row[x * 4], Math.Max(row[x * 4 + 1], row[x * 4 + 2])) / 255.0);
        }
        return levels.ToArray();
    }
}

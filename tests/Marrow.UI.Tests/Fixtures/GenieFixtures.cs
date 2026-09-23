using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// The three AppKit reads UI/Common/Genie.swift makes, scripted: NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
/// (:64, :84), NSScreen.main?.backingScaleFactor (:87) and ImageRenderer's nsImage (:85-88). Every call is logged so
/// a test sees whether the guard ran before the render and which scale reached it.
/// </summary>
public sealed class FakeGeniePlatform : IGeniePlatform
{
    private readonly Func<Control, double, IImage?> render;

    public FakeGeniePlatform(bool reduceMotion = false, double? scale = null, Func<Control, double, IImage?>? render = null)
    {
        ReduceMotion = reduceMotion;
        Scale = scale;
        this.render = render ?? ((_, _) => new StandInImage());
    }

    public bool ReduceMotion { get; set; }

    public double? Scale { get; set; }

    public List<(Control Content, double Scale)> Renders { get; } = [];

    public int ReduceMotionReads { get; private set; }

    public bool AccessibilityDisplayShouldReduceMotion
    {
        get
        {
            ReduceMotionReads++;
            return ReduceMotion;
        }
    }

    public double? MainScreenBackingScaleFactor => Scale;

    public IImage? RenderImage(Control content, double scale)
    {
        Renders.Add((content, scale));
        return render(content, scale);
    }
}

/// <summary>An image with a size and nothing to draw, for tests that only follow the records.</summary>
public sealed class StandInImage : IImage
{
    public Size Size => new(10, 10);

    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) { }
}

/// <summary>
/// UI/Common/Genie.metal:16-54, the `genie` stitchable function, transcribed line for line in single precision so a
/// test can say which source pixel each destination pixel of a flight shows. Null is the shader's `nothing`
/// (-100000, -100000): a position that samples nothing.
/// </summary>
public static class GenieMetal
{
    public static (float X, float Y)? Genie(float px, float py, float progress, float sx, float sy, float sw, float sh, float tx, float ty)
    {
        var t = Math.Clamp(progress, 0f, 1f);                              // :18
        var pinch = SmoothStep(0f, 0.45f, t);                              // :19
        var slide = SmoothStep(0.2f, 1f, t);                               // :20
        var width = sw;                                                    // :22
        var height = sh;                                                   // :23
        var flip = ty >= sy + height * 0.5f ? 1f : -1f;                    // :26
        var x0 = sx;                                                       // :27
        var y0 = flip > 0f ? sy : -(sy + height);                          // :28
        var targetY = flip * ty;                                           // :29
        var positionY = flip * py;                                         // :30
        var travel = Math.Max(targetY - y0, height);                       // :31
        if (positionY > targetY) return null;                              // :33-35
        var depth = Math.Clamp((positionY - y0) / travel, 0f, 1f);         // :38
        var bend = pinch * SmoothStep(0f, 1f, depth);                      // :39
        var neck = Math.Min(24f, width);                                   // :40
        var left = Mix(x0, tx - neck * 0.5f, bend);                        // :41
        var right = Mix(x0 + width, tx + neck * 0.5f, bend);               // :42
        var u = (px - left) / Math.Max(right - left, 0.0001f);             // :43
        if (u < 0f || u > 1f) return null;                                 // :44-46
        var sourceY = positionY - slide * (targetY - y0);                  // :49
        if (sourceY < y0 || sourceY > y0 + height) return null;            // :50-52
        return (x0 + u * width, flip * sourceY);                           // :53
    }

    // Metal smoothstep(e0, e1, x): t = clamp((x - e0) / (e1 - e0), 0, 1); t * t * (3 - 2 * t).
    private static float SmoothStep(float e0, float e1, float x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // Metal mix(a, b, t) = a + (b - a) * t.
    private static float Mix(float a, float b, float t) => a + (b - a) * t;
}

/// <summary>Bitmaps for the genie tests: a two-colour row face and pixel reads in premultiplied BGRA.</summary>
public static class GenieBitmaps
{
    /// <summary>A row face: left half opaque red, right half opaque blue, laid out at <paramref name="size"/>.</summary>
    public static Control RedBlueRow(Size size)
    {
        var grid = new Grid { Width = size.Width, Height = size.Height, ColumnDefinitions = new ColumnDefinitions("*,*") };
        grid.Children.Add(new Border { Background = Brushes.Red });
        var right = new Border { Background = Brushes.Blue };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    public static byte[] Pixels(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var buffer = new byte[stride * size.Height];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), buffer.Length, stride); }
        finally { handle.Free(); }
        return buffer;
    }

    /// <summary>The (B, G, R, A) bytes at one pixel of a buffer from <see cref="Pixels"/>.</summary>
    public static (byte B, byte G, byte R, byte A) At(byte[] pixels, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
    }
}

/// <summary>
/// Windows' "Animation effects" switch read directly (SPI_GETCLIENTAREAANIMATION), for the test that the genie's
/// reduce-motion query follows it live (ED-084, port_impacts.md X4). Reading only; nothing is ever set.
/// </summary>
public static class SystemAnimation
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out bool enabled, uint update);

    public static bool ClientAreaAnimation()
    {
        Assert.True(SystemParametersInfo(0x1042, 0, out var enabled, 0));
        return enabled;
    }
}

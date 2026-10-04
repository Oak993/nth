using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Views.Settings;
using Marrow.App.Views;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views;

// Shared pixel check for the logo places (OD-0015): samples only the Marrow icon artwork satisfies.
internal static class MarrowLogoCheck
{
    public static void AssertIsTheMarrowIcon(SwiftImage logo)
    {
        Assert.NotNull(logo.Bitmap); Assert.NotNull(logo.Bitmap2x);
        Check(logo.Bitmap2x!, 6); Check(logo.Bitmap!, 10);
    }

    private static void Check(Bitmap bitmap, int tolerance)
    {
        var size = bitmap.PixelSize;
        var bytes = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
        finally { handle.Free(); }
        var bgra = bitmap.Format == PixelFormat.Bgra8888;
        (int R, int G, int B, int A) At(double fx, double fy)
        {
            var x = Math.Clamp((int)Math.Round(fx * size.Width), 0, size.Width - 1);
            var y = Math.Clamp((int)Math.Round(fy * size.Height), 0, size.Height - 1);
            var i = (y * size.Width + x) * 4;
            return bgra ? (bytes[i + 2], bytes[i + 1], bytes[i], bytes[i + 3]) : (bytes[i], bytes[i + 1], bytes[i + 2], bytes[i + 3]);
        }
        static double U(double v) => (v - 72) / 880; // icon.svg: the tile is x,y 72..952 of a 1024 box
        void Near(string what, (int R, int G, int B, int A) p, int r, int g, int b) =>
            Assert.True(p.A >= 250 && Math.Abs(p.R - r) <= tolerance && Math.Abs(p.G - g) <= tolerance && Math.Abs(p.B - b) <= tolerance,
                $"{what}: got ({p.R},{p.G},{p.B},{p.A}), expected about ({r},{g},{b})");
        Assert.Equal(0, At(0.0, 0.0).A);
        var core = At(0.5, 0.5);
        Assert.True(core.A >= 250 && core.R >= 214 && core.G >= 220 && core.B >= 240, $"core: got ({core.R},{core.G},{core.B})");
        Near("blue arm", At(U(392.88), U(217.16)), 0x9C, 0xBB, 0xFF);
        Near("violet arm", At(U(826.91), U(556.26)), 0xCB, 0xA6, 0xFF);
        Near("mint arm", At(U(316.22), U(762.59)), 0x74, 0xF2, 0xD0);
        var navy = At(U(828), U(828));
        Assert.True(navy.A >= 250 && navy.R < 40 && navy.G < 40 && navy.B < 64, $"navy tile: got ({navy.R},{navy.G},{navy.B})");
    }
}

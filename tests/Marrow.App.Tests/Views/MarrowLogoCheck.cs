using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Security.Cryptography;
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

// OD-0112, ED-402: locked generator outputs replace only OD-0015 artwork expectations.
// Samples and literal hashes are checked in; tests never regenerate. All comparisons and bounds are unchanged.
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
        static double U(double v) => (v - 72) / 880; // Fixed fractional sampling grid; OD-0112 samples are mapped into it.
        void Near(string what, (int R, int G, int B, int A) p, int r, int g, int b) =>
            Assert.True(p.A >= 250 && Math.Abs(p.R - r) <= tolerance && Math.Abs(p.G - g) <= tolerance && Math.Abs(p.B - b) <= tolerance,
                $"{what}: got ({p.R},{p.G},{p.B},{p.A}), expected about ({r},{g},{b})");
        Assert.Equal(0, At(0.0, 0.0).A);
        var core = At(0.88, 0.67); // OD-0112: Aurora curtain glint; brightness bounds unchanged.
        Assert.True(core.A >= 250 && core.R >= 214 && core.G >= 220 && core.B >= 240, $"core: got ({core.R},{core.G},{core.B})");
        Near("night glass", At(U(688), U(248)), 0x0B, 0x10, 0x27);
        Near("teal curtain", At(U(336), U(486.48)), 0x55, 0xE3, 0xC4);
        Near("emerald foot", At(U(293.76), U(441.6)), 0x52, 0xFF, 0xDB);
        var navy = At(U(828), U(828));
        Assert.True(navy.A >= 250 && navy.R < 40 && navy.G < 40 && navy.B < 64, $"navy tile: got ({navy.R},{navy.G},{navy.B})");
    }
    /// <summary>NSApp.applicationIconImage sites (RootView.swift:307, NewThreadPrompt.swift:17, AboutUpdateViews.swift:554, SettingsView.swift:971):
    /// a plain Image whose Source is the packaged app icon bitmap, Assets/icon.png with its margin (OD-0015 / OD-0033), the
    /// same source the About identity pill draws.</summary>
    public static void AssertIsTheAppIconImage(Control control, double size)
    {
        var image = Assert.IsType<Image>(control);
        Assert.Equal(size, image.Width); Assert.Equal(size, image.Height);
        Assert.Equal(Stretch.Uniform, image.Stretch);
        Assert.Equal(BitmapInterpolationMode.HighQuality, RenderOptions.GetBitmapInterpolationMode(image));
        var source = Assert.IsAssignableFrom<Bitmap>(image.Source);
        using var resource = AssetLoader.Open(new Uri("avares://Marrow.App/Assets/icon.png"));
        using var copy = new MemoryStream(); resource.CopyTo(copy);
        Assert.Equal("2833C700A1BEC5DB954FD0EDC3045708BC1A821E0F8D4CB47958036408097008", Convert.ToHexString(SHA256.HashData(copy.ToArray())));
        copy.Position = 0;
        using var approved = new Bitmap(copy);
        Assert.Equal(approved.PixelSize, source.PixelSize);
        Assert.Equal(Pixels(approved), Pixels(source));
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var size = bitmap.PixelSize; var bytes = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
        finally { handle.Free(); }
        return bytes;
    }
}

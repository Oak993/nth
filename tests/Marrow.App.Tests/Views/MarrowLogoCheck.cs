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
// OD-0136: the artwork is the nth icon B (assets-src/brand/nth-icon-b.svg, rendered by scripts/brand/NthIcon). Each sample's
// expected colour is worked out from the master's own gradients at that point (not read back from the bitmap): see each line.
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
        static double U(double v) => v / 256; // the master's 256-unit symbol box
        void Near(string what, (int R, int G, int B, int A) p, int r, int g, int b) =>
            Assert.True(p.A >= 250 && Math.Abs(p.R - r) <= tolerance && Math.Abs(p.G - g) <= tolerance && Math.Abs(p.B - b) <= tolerance,
                $"{what}: got ({p.R},{p.G},{p.B},{p.A}), expected about ({r},{g},{b})");
        Assert.Equal(0, At(0.0, 0.0).A); // outside the tile's rounded corner (rect 8,8 240 rx 52)
        // the n's left stem at 70,130: gGlyph over y 80..196 is .43, white to --s100 #e8ebf0 (.55) at .78
        Near("n stem", At(U(70), U(130)), 237, 239, 243);
        // the bead's centre 186,74: gBead centred 182.08,68.4 r 22.4 is .31 out, white to --s200 #d6dae2 (.5) at .61
        Near("bead", At(U(186), U(74)), 230, 232, 237);
        // 40,40: gTile .13 (--g800 #14161c to --g600 #3b414f at .24) under gHi's white .46 (.55 to .08 at .2 of .42)
        Near("glass light", At(U(40), U(40)), 133, 134, 138);
        // 228,228: gTile .92 (--g600 to --s400 #9aa3b5 at .82) under gPool's --pool rgba(214,218,226) .46 (.17 of r 144)
        Near("silver pool", At(U(228), U(228)), 172, 178, 191);
        var graphite = At(U(114), U(150)); // inside the n's arch: dark graphite tile
        Assert.True(graphite.A >= 250 && graphite.R < 90 && graphite.G < 96 && graphite.B < 110, $"graphite tile: got ({graphite.R},{graphite.G},{graphite.B})");
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
        Assert.Equal("37013DCFD529A2565D715CE1E19D836C587B9C044A13819FB97EECFF9FF9C613", Convert.ToHexString(SHA256.HashData(copy.ToArray()))); // OD-0136: scripts/brand/NthIcon's icon.png
        copy.Position = 0;
        using var approved = new Bitmap(copy);
        Assert.Equal(approved.PixelSize, source.PixelSize);
        Assert.Equal(Pixels(approved), Pixels(source));
    }

    /// <summary>OD-0136: NewThreadPrompt.swift:17's icon is the 44 point nth tile B (nth-brand.html section 3, .hero .mk): the
    /// icon symbol drawn as vectors (NthTile; its drawing and sweep are NthTileTests'), hidden from assistive technology.</summary>
    public static void AssertIsTheNthTile(Control control)
    {
        var tile = Assert.IsType<NthTile>(control);
        Assert.Equal(NthTile.Side, tile.Width); Assert.Equal(NthTile.Side, tile.Height);
        Assert.Equal(44, NthTile.Side);
        Assert.Equal(Avalonia.Automation.AccessibilityView.Raw, Avalonia.Automation.AutomationProperties.GetAccessibilityView(tile));
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

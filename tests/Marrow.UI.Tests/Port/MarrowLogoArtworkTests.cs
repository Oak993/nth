using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// OD-0015 (and OD-0002, OD-0033): every place upstream shows its own app logo shows the Marrow icon. The upstream
// Image("droppy-logo") lookup key stays (the view code keeps the Swift name), but what it loads is the Marrow icon
// (src/Marrow.App/Assets/icon.svg, artwork cropped to the tile so it fills the frame as the full-bleed Droppy file
// did), never DroppyCode/Resources/Assets.xcassets/droppy-logo.imageset/*.png.
[Collection("Headless")]
public sealed class MarrowLogoArtworkTests(HeadlessSession headless)
{
    // SHA-256 of the two upstream PNGs (B3 52bc792). Nothing shipped may carry either.
    private static readonly string[] DroppyHashes =
    [
        "307aca65b532fe56c1faf38423e40812fcdd4969e105bd1e936224cb19e4de95",
        "467897a05e5b29238ee4df44e38eb130fb7bdf93a9d0acec38f342afdf1c242c",
    ];

    /// <summary>Pixel samples that only the Marrow icon artwork satisfies, at fractions of the image (the icon.svg tile is
    /// x,y 72..952 in a 1024 box; the sample points are the gradient start of each arm and the white core).</summary>
    private static void AssertIsTheMarrowIcon(Bitmap bitmap, int tolerance)
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
        static double U(double v) => (v - 72) / 880;
        void Near(string what, (int R, int G, int B, int A) p, int r, int g, int b)
        {
            Assert.True(p.A >= 250 && Math.Abs(p.R - r) <= tolerance && Math.Abs(p.G - g) <= tolerance && Math.Abs(p.B - b) <= tolerance,
                $"{what}: got ({p.R},{p.G},{p.B},{p.A}), expected about ({r},{g},{b}) in a {size.Width}px image");
        }

        // Round-tile corners are transparent and the tile reaches the frame edge (the Droppy file is full-bleed too).
        Assert.Equal(0, At(0.0, 0.0).A);
        Assert.True(At(0.5, 0.015).A >= 250);
        Assert.True(At(0.015, 0.5).A >= 250);
        // The white core at the centre: icon.svg circle (512,512) r 82, radial #FFFFFF -> #D6DEFB.
        var core = At(0.5, 0.5);
        Assert.True(core.A >= 250 && core.R >= 214 && core.G >= 220 && core.B >= 240, $"core: got ({core.R},{core.G},{core.B})");
        // The three arms, at the centre of each one's start cap: #9CBBFF blue, #CBA6FF violet, #74F2D0 mint.
        Near("blue arm", At(U(392.88), U(217.16)), 0x9C, 0xBB, 0xFF);
        Near("violet arm", At(U(826.91), U(556.26)), 0xCB, 0xA6, 0xFF);
        Near("mint arm", At(U(316.22), U(762.59)), 0x74, 0xF2, 0xD0);
        // The dark navy tile near its bottom-right (#0A0C18 end of the gradient); the Droppy logo is blue-violet there.
        var navy = At(U(828), U(828));
        Assert.True(navy.A >= 250 && navy.R < 40 && navy.G < 40 && navy.B < 64, $"navy tile: got ({navy.R},{navy.G},{navy.B})");
    }

    [Fact]
    [Trait("Decision", "OD-0015")]
    public void The_logo_the_views_ask_for_by_its_Swift_name_is_the_Marrow_icon_at_1x_and_2x() => headless.OnUiThread(() =>
    {
        var logo = SwiftImage.Named("droppy-logo");
        Assert.False(logo.IsEmpty);
        Assert.Null(logo.Icon);
        Assert.Equal(new PixelSize(256, 256), logo.Bitmap!.PixelSize);
        Assert.Equal(new PixelSize(512, 512), logo.Bitmap2x!.PixelSize);
        AssertIsTheMarrowIcon(logo.Bitmap2x, 6);
        AssertIsTheMarrowIcon(logo.Bitmap, 10);
    });

    [Fact]
    [Trait("Decision", "OD-0015")]
    public void No_Droppy_logo_file_is_shipped_and_the_shipped_logo_files_are_not_the_Droppy_bytes() => headless.OnUiThread(() =>
    {
        foreach (var name in new[] { "droppy-logo.png", "droppy-logo@2x.png" })
            Assert.False(AssetLoader.Exists(new Uri("avares://Marrow.UI/Assets/Images/" + name)), name + " is the upstream Droppy Code logo and must not ship (OD-0015)");
        foreach (var name in new[] { "marrow-icon.png", "marrow-icon@2x.png" })
        {
            using var stream = AssetLoader.Open(new Uri("avares://Marrow.UI/Assets/Images/" + name));
            Assert.DoesNotContain(Convert.ToHexStringLower(SHA256.HashData(stream)), DroppyHashes);
        }
    });
}

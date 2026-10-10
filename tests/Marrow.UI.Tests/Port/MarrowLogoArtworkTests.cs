using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// OD-0112 (and OD-0002, OD-0033): every place upstream shows its own app logo shows the Marrow icon. The upstream
// Image("droppy-logo") lookup key stays (the view code keeps the Swift name), but what it loads is the Marrow icon
// (locked Aurora Canvas source, cropped to IconKit.box(size) by generate_app_icon_d.py; ED-402), as the full-bleed Droppy file
// did), never DroppyCode/Resources/Assets.xcassets/droppy-logo.imageset/*.png.
// OD-0136: the artwork is now the nth icon B (assets-src/brand/nth-icon-b.svg, rendered by scripts/brand/NthIcon at 256 and 512 px).
// Each sample's expected colour is worked out from the master's own gradients at that point, as MarrowLogoCheck's are.
[Collection("Headless")]
public sealed class MarrowLogoArtworkTests(HeadlessSession headless)
{
    // SHA-256 of the two upstream PNGs (B3 52bc792). Nothing shipped may carry either.
    private static readonly string[] DroppyHashes =
    [
        "307aca65b532fe56c1faf38423e40812fcdd4969e105bd1e936224cb19e4de95",
        "467897a05e5b29238ee4df44e38eb130fb7bdf93a9d0acec38f342afdf1c242c",
    ];

    /// <summary>OD-0136 samples of the nth icon B in the master's 256-unit symbol box: the n's stem, the bead, the glass light,
    /// the silver pool and the graphite inside the arch. The comparison methods and the 6/10 tolerances are unchanged.</summary>
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
        static double U(double v) => v / 256; // the master's 256-unit symbol box
        void Near(string what, (int R, int G, int B, int A) p, int r, int g, int b)
        {
            Assert.True(p.A >= 250 && Math.Abs(p.R - r) <= tolerance && Math.Abs(p.G - g) <= tolerance && Math.Abs(p.B - b) <= tolerance,
                $"{what}: got ({p.R},{p.G},{p.B},{p.A}), expected about ({r},{g},{b}) in a {size.Width}px image");
        }

        // The rounded tile (rect 8,8 240 rx 52) leaves the corners and the 8-unit margin clear and fills from its edge inward.
        Assert.Equal(0, At(0.0, 0.0).A);
        Assert.Equal(0, At(0.5, U(4)).A);
        Assert.True(At(0.5, U(10)).A >= 250);
        Assert.True(At(U(10), 0.5).A >= 250);
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

    [Fact]
    [Trait("Decision", "ED-215")] // OD-0015: the owner-ruled Marrow icon replaces the Droppy logo
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
    [Trait("Decision", "ED-215")] // OD-0015: the owner-ruled Marrow icon replaces the Droppy logo
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

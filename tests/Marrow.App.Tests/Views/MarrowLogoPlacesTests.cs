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

// OD-0015, OD-0002, OD-0033: every place upstream shows its own app logo (Image("droppy-logo")) draws the Marrow icon
// (src/Marrow.App/Assets/icon.svg artwork), at the source's size: the Welcome page (104 pt, RootView.swift:307-309),
// the new-thread prompt (72 pt, NewThreadPrompt.swift:17-21) and Settings > About "Made by" row (40 pt, SettingsView.swift:1090-1093).
// The Droppy Code logo must be in none of them: its tile is blue-violet; the Marrow tile is dark navy with a white core.
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

[Collection("Headless")]
public sealed class MarrowLogoPlacesTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "OD-0015")]
    public void The_Welcome_page_logo_is_the_Marrow_icon_at_104_points() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var host = RootViewRig.Show(model, new WelcomeView(), 900);
            try
            {
                var logo = Assert.Single(RootViewRig.All<SwiftImage>(host), image => image.AssetName == "droppy-logo");
                Assert.Equal(104, logo.Width); Assert.Equal(104, logo.Height);
                MarrowLogoCheck.AssertIsTheMarrowIcon(logo);
            }
            finally { RootViewRig.Close(host); }
        });
    });

    [Fact]
    [Trait("Decision", "OD-0015")]
    public void The_new_thread_prompt_logo_is_the_Marrow_icon_at_72_points() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var host = new Border();
            AppEnvironment.SetAppModel(host, model);
            host.Child = new NewThreadPrompt(System.Guid.NewGuid(), null);
            var window = new AvaloniaWindow { Content = host };
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            window.Show();
            var logo = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(host), image => image.AssetName == "droppy-logo");
            Assert.Equal(72, logo.Width); Assert.Equal(72, logo.Height);
            MarrowLogoCheck.AssertIsTheMarrowIcon(logo);
        });
    });
}

[Collection("Headless")]
public sealed class MarrowLogoSettingsRowTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    [Fact]
    [Trait("Decision", "OD-0015")]
    public void The_Settings_About_row_logo_is_the_Marrow_icon_at_40_points()
    {
        With(() => new MadeByDroppyRow(SwiftURL.Create("https://getdroppy.app")!), (view, _, _, _) =>
        {
            var logo = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(view), image => image.AssetName == "droppy-logo");
            Assert.Equal(40, logo.Width); Assert.Equal(40, logo.Height);
            MarrowLogoCheck.AssertIsTheMarrowIcon(logo);
        }, height: 220);
    }
}

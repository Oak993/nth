using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class HydraCardGlassCalibrationTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-HYDRA-229")] // HydraPanel.swift:99-121; Mac frame 18; ED-358 (1),(4)
    public void The_real_Hydra_card_matches_the_Mac_body_band_and_cast_shadow() => Measure(release: true);

    [Fact]
    [Trait("Decision", "OD-0147")] // the shipped canvas card: the canvas's popover glass, its body as continuous across the band (the same steps) and its cast shadow the Mac's (the card casts its own, so the material adds none)
    public void The_real_Hydra_card_on_the_canvas_glass_keeps_a_continuous_body_and_the_Macs_cast_shadow() => Measure(release: false);

    // OD-0147: the Mac frame's body and band levels are the release material's, so the release case draws the card with the release look; the
    // canvas case measures the same site with the shipped material and holds every measure that is not the release material's own levels.
    private void Measure(bool release) => ChatHeadlessFixtures.Run(headless, () =>
    {
        var previous = ThemeManager.Current;
        try
        {
            using var context = new HydraPanelContext(theme: ThemeVariant.Dark);
            ThemeManager.Current = AppTheme.Monokai;
            var (host, card, bitmap) = HydraCardCalibrationSite.Create(context.Model, context.Runtime);
            using (bitmap)
            {
                try
                {
                    context.Window.Content = host;
                    var glass = card.GetVisualDescendants().OfType<Control>().Prepend(card).Where(piece => SwiftGlass.GetEffect(piece) is not null).ToArray();
                    Assert.NotEmpty(glass);
                    if (release) foreach (var piece in glass) SwiftGlass.SetEffect(piece, SwiftGlass.GetEffect(piece)!.WithLook(GlassLook.Release));
                    else Assert.Equal(GlassKind.Popover, GlassLooks.Of(SwiftGlass.GetEffect(glass[0])!).Canvas!.Kind);
                    context.Window.Width = 520; context.Window.Height = 347;
                    context.Window.Background = Brushes.Black;
                    Func<int, int, double> Capture(string tag)
                    {
                        for (var i = 0; i < 4; i++) { context.Drain(); context.Window.CaptureRenderedFrame()?.Dispose(); }
                        using var frame = context.Window.CaptureRenderedFrame()!;
                        Directory.CreateDirectory(HydraCardCalibrationSite.Evidence);
                        frame.Save(Path.Combine(HydraCardCalibrationSite.Evidence, tag + (release ? "" : "-canvas") + ".png"), new PngBitmapEncoderOptions());
                        var bytes = new byte[frame.PixelSize.Width * frame.PixelSize.Height * 4];
                        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                        try { frame.CopyPixels(new PixelRect(frame.PixelSize), handle.AddrOfPinnedObject(), bytes.Length, frame.PixelSize.Width * 4); }
                        finally { handle.Free(); }
                        var stride = frame.PixelSize.Width * 4;
                        Assert.True(frame.Format == Avalonia.Platform.PixelFormat.Rgba8888 || frame.Format == Avalonia.Platform.PixelFormat.Bgra8888);
                        var red = frame.Format == Avalonia.Platform.PixelFormat.Rgba8888 ? 0 : 2;
                        var blue = 2 - red;
                        return (x, y) => .2126 * bytes[y * stride + x * 4 + red] + .7152 * bytes[y * stride + x * 4 + 1] + .0722 * bytes[y * stride + x * 4 + blue];
                    }
                    card.IsVisible = false;
                    _ = Capture("hydra18-headless-backdrop");
                    card.IsVisible = true;
                    var with = Capture("hydra18-headless-real-site");
                    static double Mean(Func<int,int,double> pixels, int fromY, int toY) =>
                        Enumerable.Range(fromY, toY-fromY).SelectMany(y => Enumerable.Range(270,170).Select(x => pixels(x,y))).Average();
                    var upper = Mean(with,106,156); var lower = Mean(with,246,286); var band = Mean(with,61,62);
                    // Mac frame 18 has a continuous blurred body. Scan clear strips through the band/body join,
                    // away from the popped-out icon, text and buttons; a one-pixel jump must stay within 3 luma levels.
                    var topStep = Enumerable.Range(90,25).SelectMany(y => Enumerable.Range(105,275).Select(x => Math.Abs(with(x,y+1)-with(x,y)))).Max();
                    var sideStep = Enumerable.Range(90,25).SelectMany(x => Enumerable.Range(105,185).Select(y => Math.Abs(with(x+1,y)-with(x,y)))).Max();
                    var cornerStep = Enumerable.Range(18,18).Select(d => Math.Abs(with(60+d-1,60+d+1)-with(60+d+1,60+d-1))).Max();
                    var lines = new List<string> { $"upper {upper:F3}; lower {lower:F3}; band {band:F3}; top step {topStep:F3}; side step {sideStep:F3}; corner step {cornerStep:F3}" };
                    (int D,double Above,double Beside)[] mac = [(2,.089,.146),(4,.084,.141),(8,.069,.123),(14,.048,.109),(20,.024,.084),(30,.009,.037),(46,-.002,.028)];
                    // Shadow coverage is independent of the backdrop. Read it over white, as SwiftShadowCastTests does:
                    // a one-byte dark-image rounding error would divide by only 20-60 levels and bias the inferred coverage.
                    // This still uses the production HydraPanel and its real Caster; the body/band above use the Mac backdrop.
                    host.Children.OfType<Image>().Single().IsVisible = false;
                    host.Background = Brushes.White;
                    card.IsVisible = false;
                    var whiteWithout = Capture("hydra18-headless-white-backdrop");
                    card.IsVisible = true;
                    var whiteWith = Capture("hydra18-headless-white-shadow");
                    var failures = new List<string>();
                    foreach (var (d, above, beside) in mac)
                    {
                        var top = Enumerable.Range(180,160).Select(x => 1-whiteWith(x,60-d)/Math.Max(1,whiteWithout(x,60-d))).Average();
                        var side = Enumerable.Range(120,120).Select(y => 1-whiteWith(460+d-1,y)/Math.Max(1,whiteWithout(460+d-1,y))).Average();
                        lines.Add($"distance {d}: above {top:F3} vs Mac {above:F3}; beside {side:F3} vs Mac {beside:F3}");
                        if (Math.Abs(top-above) > .025 || Math.Abs(side-beside) > .025) failures.Add(lines[^1]);
                    }
                    File.WriteAllLines(Path.Combine(HydraCardCalibrationSite.Evidence,release ? "hydra18-headless-real-site.txt" : "hydra18-headless-real-site-canvas.txt"),lines);
                    if (release)
                    {
                        Assert.InRange(upper,43.36-2.5,43.36+2.5);
                        Assert.InRange(lower,42.70-2.5,42.70+2.5);
                        Assert.InRange(band,43.99-3,43.99+3);
                        Assert.InRange(Math.Abs(upper-lower),0,2);
                    }
                    Assert.InRange(topStep,0,3);
                    Assert.InRange(sideStep,0,3);
                    Assert.InRange(cornerStep,0,4);
                    Assert.True(failures.Count==0,string.Join("; ",failures));
                }
                finally { context.Window.Content = null; } // detach before disposing the bitmap, even when an assertion fails
            }
        }
        finally { ThemeManager.Current = previous; }
    });
}

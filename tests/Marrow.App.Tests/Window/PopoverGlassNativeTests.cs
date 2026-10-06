using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for ED-357 (OD-0107 and its amendment, OD-0005): a real popover, presented through the app's own
// presenter (Presentation, PopoverHost, PopoverAppear) from an anchor in a real Win32 window, shows see-through glass in its
// own popup window: the screen behind it blurred, its corners round and its tip shaped, a dark hairline and a light rim, and its
// text kept legible over a white backdrop (Dark) and a black one (Light). The pixels are the composed screen (ScreenGrab: BitBlt
// with CAPTUREBLT), what the user sees. The backdrop is the anchor's own window, physical-pixel columns: white (0-450), stripes
// six pixels wide (450-600), black (600-900); the card, 600 x 220, hangs above an anchor at 525, so its left corners are over
// white, its right ones over black, and its tip and the body above it over the stripes. The timing test samples the stripes just
// above the tip from before the popover opens, so the first frame with glass on the screen is seen, and compares it with the
// grow-in (0.21 s) and the fade (0.06 s). Needs a free interactive desktop; the lead takes native.lock around the run.
[Collection("WindowManagerNative")]
public sealed class PopoverGlassNativeTests(WindowManagerNativeSession native)
{
    private const double Sharp = 42.5;  // StripeContrast of the bare stripes
    private const double Blurred = 3;   // below this the stripes are gone


    // The backdrop window with its anchor, and a popover on it, not yet presented.
    private PopoverGlassNativeScene Build(ThemeVariant appearance, PixelRect work)
    {
        var scale = 1.0;
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("450*,150*,300*") };
        columns.Children.Add(new Border { Background = Brushes.White });
        var stripes = new Stripes();
        Grid.SetColumn(stripes, 1);
        columns.Children.Add(stripes);
        var black = new Border { Background = Brushes.Black };
        Grid.SetColumn(black, 2);
        columns.Children.Add(black);
        var anchor = new Border { Width = 10, Height = 10, Background = Brushes.Red, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var root = new Panel { Children = { columns, anchor } };
        var window = new AvaloniaWindow
        {
            WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(work.X + 120, work.Y + 120),
            Content = new ThemeVariantScope { RequestedThemeVariant = appearance, Child = root },
            RequestedThemeVariant = appearance,
        };
        window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
        native.Register(window);
        window.Show();
        scale = window.RenderScaling;
        window.Width = 900 / scale;
        window.Height = 520 / scale;
        anchor.Margin = new Thickness(520 / scale, 470 / scale, 0, 0); // its middle at 525 px
        FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(window)); // the frame clock the app binds at start-up
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Top, IsLightDismissEnabled = false,
            Child = new Border { Width = 600 / scale, Height = 220 / scale },
        };
        return new PopoverGlassNativeScene(window, popup, anchor, scale);
    }

    private PixelRect Work() => native.OnUi(() =>
    {
        var probe = new AvaloniaWindow();
        var screen = probe.Screens.Primary ?? throw new InvalidOperationException("No native screen is available.");
        probe.Close();
        if (screen.WorkingArea.Width < 1100 || screen.WorkingArea.Height < 700)
            throw new InvalidOperationException("NATIVE_PRECONDITION: the popover glass tests need a working area of at least 1100 x 700 pixels.");
        return screen.WorkingArea;
    });

    private void Present(PopoverGlassNativeScene scene) => native.OnUi(() => Presentation.SetPresented(scene.Popup, true, scene.Anchor, () => Presentation.SetPresented(scene.Popup, false, scene.Anchor, () => { })));

    private void Dismiss(PopoverGlassNativeScene scene) => native.OnUi(() => { scene.Popup.IsOpen = false; scene.Window.Close(); });

    // The card's body (without its tip and its shadow room) and its tip's point, in screen pixels.
    private (PixelRect Body, PixelPoint Tip) Where(PopoverGlassNativeScene scene) => native.OnUi(() =>
    {
        var card = Assert.IsType<PopoverCard>(scene.Popup.Child);
        var topLeft = card.PointToScreen(new Point(0, 0));
        var bottomRight = card.PointToScreen(new Point(card.Bounds.Width, card.Bounds.Height - PopoverCard.TipHeight - PopoverCard.TipGap));
        return (new PixelRect(topLeft, bottomRight), card.PointToScreen(card.TipPoint));
    });

    private static ScreenGrab Grab(int x, int y, int width = 60, int height = 12) => ScreenGrab.Of(x, y, width, height);

    // Where the screen captures and the timing go: the gallery's folder when the parent opted in (MARROW_GALLERY=1,
    // MARROW_GALLERY_DIR); the child's environment is an allow-list, so the folder travels in its request file.
    private static string? Artifacts() => GalleryRun.OptionalOutputFolder();

    private static void Save(ScreenGrab grab, string name)
    {
        if (Artifacts() is { } folder) { Directory.CreateDirectory(folder); grab.SavePng(Path.Combine(folder, name)); }
    }

    [Fact]
    [Trait("Req", "PR-WIN-180")] // OD-0107, OD-0005
    public void A_real_dark_popover_shows_the_blurred_backdrop_through_round_cornered_glass_with_its_tip_a_hairline_and_a_rim() =>
        Shows("Dark", nameof(A_real_dark_popover_shows_the_blurred_backdrop_through_round_cornered_glass_with_its_tip_a_hairline_and_a_rim));

    [Fact]
    [Trait("Req", "PR-WIN-180")] // OD-0107, OD-0005
    public void A_real_light_popover_shows_the_blurred_backdrop_through_round_cornered_glass_with_its_tip_a_hairline_and_a_rim() =>
        Shows("Light", nameof(A_real_light_popover_shows_the_blurred_backdrop_through_round_cornered_glass_with_its_tip_a_hairline_and_a_rim));

    private void Shows(string name, string fact) => native.Run(subject =>
    {
        var dark = name == "Dark";
        var appearance = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        native.OnUi(() => { subject.Settings.Hide(); subject.Main.Hide(); });
        var work = Work();
        var scene = native.OnUi(() => Build(appearance, work));
        try
        {
            Thread.Sleep(600);
            var origin = native.OnUi(() => scene.Window.PointToScreen(new Point(0, 0)));
            var bare = Grab(origin.X + 500, origin.Y + 300);
            Assert.InRange(bare.StripeContrast(), Sharp - 6, Sharp + 3); // the instrument sees the stripes straight
            Present(scene);
            N.Until(() => native.OnUi(() => scene.Popup.IsOpen), "the popover never opened", 5000);
            Thread.Sleep(900); // the grow-in is over
            var (body, tip) = Where(scene);
            Assert.InRange(body.Width, 590, 610);
            var card = native.OnUi(() => (PopoverCard)scene.Popup.Child!);
            Assert.Equal(PopoverMaterial.Glass, native.OnUi(() => card.Material));
            Assert.True(native.OnUi(() => card.Glass?.Attached ?? false), "the popup window's glass never attached: " + native.OnUi(() => card.Glass?.Failure));
            Save(ScreenGrab.Of(body.X - 20, body.Y - 20, body.Width + 40, body.Height + 50), $"native-popover-{name}.png");

            // 1. Seen through: the stripes under the body are blurred away.
            var through = Grab(Math.Clamp(tip.X - 30, origin.X + 455, origin.X + 535), body.Y + 80);
            Assert.True(through.StripeContrast() < Blurred, $"{name}: the stripes under the glass read {through.StripeContrast():F2} (bare {bare.StripeContrast():F2})");
            // 2. Legible over anything: over white (Dark) the cap holds the glass at about 86 x 0.9 + 25.5 = 102.9; over black
            //    (Light) the tint alone lifts it to about 0.5 x 255 = 128 (ED-357 (3)).
            var overWhite = Grab(origin.X + 300, body.Y + 80, 60, 40).MeanLuma();
            var overBlack = Grab(origin.X + 700, body.Y + 80, 60, 40).MeanLuma();
            if (dark)
            {
                Assert.InRange(overWhite, 95, 111); // the label (white 0.85) keeps 4.5:1 below about 105
                Assert.InRange(overBlack, 12, 32);
            }
            else
            {
                Assert.InRange(overBlack, 118, 140); // black text keeps at least 4.5:1 above 118
                Assert.InRange(overWhite, 245, 256);
            }
            // 3. The edge, down a column from outside the top edge in: the backdrop (and the shadow), the dark hairline, the
            //    light rim, the glass. Dark over white, Light over black (a white rim cannot show over white glass).
            var column = Grab(origin.X + (dark ? 300 : 700), body.Y - 6, 1, 20);
            var lumas = Enumerable.Range(0, 20).Select(y => column.Luma(0, y)).ToArray();
            var glass = lumas[^1];
            var hairline = lumas.Skip(4).Take(4).Min(); // body.Y is the hairline's row
            var rim = lumas.Skip(6).Take(4).Max();
            Assert.True(hairline < glass - (dark ? 15 : 8) && (!dark || hairline < lumas[0] - 15), $"{name}: no dark hairline round the glass: {string.Join(",", lumas.Select(v => v.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)))}");
            Assert.True(rim > glass + (dark ? 12 : 1), $"{name}: no light rim inside the hairline: {string.Join(",", lumas.Select(v => v.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)))}");
            // 4. The corners are round: a few pixels in from the top-left corner of the body's box is the backdrop (white, at
            //    most the shadow's 0.22 black over it), not the glass; on the diagonal 12 px in it is the glass.
            var corner = Grab(body.X + 2, body.Y + 2, 2, 2).MeanLuma();
            var inCorner = Grab(body.X + 12, body.Y + 12, 2, 2).MeanLuma();
            Assert.True(corner > 190, $"{name}: the corner's wedge is not the backdrop: {corner:F0}");
            Assert.True(Math.Abs(inCorner - overWhite) < 20, $"{name}: inside the round corner is not the glass: {inCorner:F0} against {overWhite:F0}");
            // 5. The tip is glass over the stripes, and beside it the stripes stay sharp. The tip's block is 16 px wide, 9 to 10 px
            //    above its point (where the tip is 18 px across), so it always spans a stripe's edge (review of glass-g2: a 6 px
            //    block could sit inside one stripe and read 0 with no glass in the tip at all).
            var inTip = Grab(tip.X - 8, tip.Y - 10, 16, 2);
            var beside = Grab(tip.X + 16, tip.Y - 6, 24, 3);
            Assert.True(inTip.StripeContrast() < 6, $"{name}: the tip is not glass: contrast {inTip.StripeContrast():F1}");
            Assert.True(beside.StripeContrast() > Sharp - 12, $"{name}: beside the tip the stripes are not sharp: {beside.StripeContrast():F1}");
        }
        finally
        {
            Dismiss(scene);
        }
    }, fact, typeof(PopoverGlassNativeTests));

    [Fact]
    [Trait("Req", "PR-WIN-180")] // requirement 6: the glass is there before the grow-in ends
    public void The_glass_reaches_the_screen_before_the_popover_has_grown_in() => native.Run(subject =>
    {
        native.OnUi(() => { subject.Settings.Hide(); subject.Main.Hide(); });
        var work = Work();
        var scene = native.OnUi(() => Build(ThemeVariant.Dark, work));
        try
        {
            Thread.Sleep(600);
            var origin = native.OnUi(() => scene.Window.PointToScreen(new Point(0, 0)));
            // Just above the tip, over the stripes: the first place the growing card covers.
            using var sampler = new Sampler(new PixelPoint(origin.X + 495, origin.Y + 430));
            Thread.Sleep(200);
            double opened = -1;
            native.OnUi(() => scene.Popup.Opened += (_, _) => opened = sampler.Now);
            Present(scene);
            N.Until(() => native.OnUi(() => scene.Popup.IsOpen), "the popover never opened", 5000);
            Thread.Sleep(900);
            var samples = sampler.Stop();
            var card = native.OnUi(() => (PopoverCard)scene.Popup.Child!);
            var timeline = native.OnUi(() => card.Timeline);
            var bareContrast = samples.Where(s => s.Milliseconds < opened - 20).Average(s => s.Contrast);
            Assert.InRange(bareContrast, Sharp - 6, Sharp + 3);
            var firstGlass = samples.FirstOrDefault(s => s.Milliseconds >= opened && s.Contrast < Blurred);
            Assert.True(firstGlass != default, "the glass never reached the screen");
            var firstChange = samples.First(s => s.Milliseconds >= opened && Math.Abs(s.Contrast - bareContrast) > 3);
            // The tint over sharp stripes: frames that changed but still show stripes before the blur arrived.
            var tintedOverSharp = samples.Count(s => s.Milliseconds >= firstChange.Milliseconds && s.Milliseconds < firstGlass.Milliseconds && s.Contrast >= 20);
            var report = FormattableString.Invariant($"opened at {opened:F0} ms; first change on screen +{firstChange.Milliseconds - opened:F0} ms ({firstChange}); first blurred +{firstGlass.Milliseconds - opened:F0} ms ({firstGlass}); {tintedOverSharp} samples of a change over sharp stripes before it; card: {timeline}");
            Console.WriteLine("TIMING " + report);
            if (Artifacts() is { } folder) { Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder, "timing.txt"), DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture) + " " + report + Environment.NewLine); }
            Assert.True(firstGlass.Milliseconds - opened < PopoverAppear.Grow.Duration * 1000, "the glass came after the grow-in ended: " + report);
            Assert.True(timeline.FirstGlass != 0 && timeline.Ms(timeline.FirstGlass) < PopoverAppear.Grow.Duration * 1000, "the card's glass was put in late: " + report);
        }
        finally
        {
            Dismiss(scene);
        }
    }, nameof(The_glass_reaches_the_screen_before_the_popover_has_grown_in), typeof(PopoverGlassNativeTests));
}

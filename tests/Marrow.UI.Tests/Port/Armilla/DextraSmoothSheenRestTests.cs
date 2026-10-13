using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port.Armilla;
using Marrow.UI.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Armilla;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-smooth-13")]
public sealed class DextraSmoothSheenRestTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    public void Waking_during_the_finishing_pass_keeps_the_shimmer_running_without_a_cut(int hz) => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        using var display = new DextraRestDisplayPeriod(hz);
        var text = new TickText { Width = 200, Text = "Working on the current task", Sheen = true, Ink = Brushes.Gray };
        var window = new Window { Width = 200, Height = 24, Content = text, Background = Brushes.Gray };
        byte[] Frame() { DextraSmoothRestTests.Pump(clock, hz); return DextraSmoothRestTests.Pixels(window); }
        try
        {
            window.Show(); ArmillaWindowWatch.For(window).Focused(true);
            var prior = Frame(); long ownStep = 0;
            for (var i = 0; i < hz * (TickText.SheenPeriod + 2); i++) { var next = Frame(); ownStep = Math.Max(ownStep, DextraSmoothRestTests.Distance(prior, next)); prior = next; }
            Assert.True(ownStep > 0);
            ownStep = Math.Max(ownStep, OwnRasterStep(text, hz));
            DextraSmoothRestTests.Visual(text).SendHandlerMessage(SheenHandler.Sleep.On);
            for (var i = 0; i < hz / 10; i++) prior = Frame();
            DextraSmoothRestTests.Visual(text).SendHandlerMessage(SheenHandler.Sleep.Off);
            for (var i = 0; i < hz * 5; i++)
            {
                var next = Frame();
                Assert.True(DextraSmoothRestTests.Distance(prior, next) <= ownStep, "interrupted rest cut the current pass");
                prior = next;
            }
            Assert.True(text.SheenTicking, "wake during pass completion left the awake shimmer parked");
            var before = text.SheenFrames;
            for (var i = 0; i < hz; i++) Frame();
            Assert.True(text.SheenFrames > before + 2);
        }
        finally { window.Close(); DextraSmoothRestTests.Pump(clock, hz); }
    });

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    public void Shimmer_finishes_its_pass_sleeps_off_text_and_wakes_from_off_text(int hz) => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        using var display = new DextraRestDisplayPeriod(hz);
        var text = new TickText { Width = 200, Text = "Working on the current task", Sheen = true, Ink = Brushes.Gray };
        var window = new Window { Width = 200, Height = 24, Content = text, Background = Brushes.Gray };
        var peer = new Window { Width = 24, Height = 24, Content = new ArmillaMark { Kind = ArmillaKind.Live } };
        byte[] Frame() { DextraSmoothRestTests.Pump(clock, hz); return DextraSmoothRestTests.Pixels(window); }
        try
        {
            window.Show(); peer.Show(); ArmillaWindowWatch.For(window).Focused(true); ArmillaWindowWatch.For(peer).Focused(true);
            var prior = Frame(); long ownStep = 0;
            for (var i = 0; i < hz * (TickText.SheenPeriod + 2); i++) { var next = Frame(); ownStep = Math.Max(ownStep, DextraSmoothRestTests.Distance(prior, next)); prior = next; }
            Assert.True(ownStep > 0, "shimmer witness saw no band");
            ownStep = Math.Max(ownStep, OwnRasterStep(text, hz));
            var before = text.SheenFrames;
            DextraSmoothRestTests.Visual(text).SendHandlerMessage(SheenHandler.Sleep.On);
            for (var i = 0; i < hz * 5; i++)
            {
                var next = Frame();
                Assert.True(DextraSmoothRestTests.Distance(prior, next) <= ownStep, "shimmer jumped while finishing its pass");
                prior = next;
            }
            Assert.True(text.SheenFrames > before + 2, "sleep froze the current shimmer pass");
            Assert.False(text.SheenTicking);
            before = text.SheenFrames;
            for (var i = 0; i < hz; i++) Assert.Equal(prior, Frame());
            Assert.Equal(before, text.SheenFrames);
            text.Sheen = false; for (var i = 0; i < 4; i++) Frame();
            Assert.Equal(prior, Frame()); // identical to plain text, not a frozen overlay
            text.Sheen = true; Frame();
            Assert.Equal(prior, Frame());
            before = text.SheenFrames;
            DextraSmoothRestTests.Visual(text).SendHandlerMessage(SheenHandler.Sleep.Off);
            Assert.Equal(prior, Frame());
            for (var i = 0; i < hz * 5; i++)
            {
                var next = Frame();
                Assert.True(DextraSmoothRestTests.Distance(prior, next) <= ownStep,
                    $"wake shimmer step {DextraSmoothRestTests.Distance(prior, next)} exceeds own full-pass step {ownStep} at frame {i}");
                prior = next;
            }
            Assert.True(text.SheenTicking);
            Assert.True(text.SheenFrames > before + 2, "an awake shimmer must actually re-enter on the next shared pass");
            text.IsVisible = false; for (var i = 0; i < 4; i++) Frame(); before = text.SheenFrames;
            for (var i = 0; i < hz; i++) Frame();
            Assert.Equal(before, text.SheenFrames); Assert.False(text.SheenTicking);
        }
        finally { window.Close(); peer.Close(); DextraSmoothRestTests.Pump(clock, hz); }
    });

    // Sample the unchanged gradient's OWN raster motion over densely spaced
    // phases. Source parity of this painter is guarded independently by the
    // pre-optimization gradient oracle in DextraSmoothSheenTests. An arbitrary
    // sleep phase need not hit the first pass's subpixel/8-bit rounding.
    // No tolerance or speed multiplier: the interval is the 60 Hz slot's largest
    // possible separation on this display (one tick at 60, three at 144).
    private static long OwnRasterStep(TickText text, int hz)
    {
        var band = new SheenHandler.Band(0, text.InkWidth, text.Bounds.Height, true,
            text.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark);
        using var bitmap = new SKBitmap(200, 24); using var canvas = new SKCanvas(bitmap);
        var painter = new SheenHandler(new ArmillaHub(), null, TimeProvider.System);
        byte[] At(double at)
        {
            canvas.Clear(SKColors.Gray);
            painter.Draw(canvas, band, at, 1);
            return bitmap.Bytes;
        }
        // Match the fake clock's actual TimeSpan tick quantization, not an ideal
        // double interval (1/60 rounds to 166667 ticks on this test clock).
        var dt = Math.Ceiling(hz / 60.0) * TimeSpan.FromSeconds(1.0 / hz).TotalSeconds;
        long maximum = 0;
        for (var i = 0; i < 14400; i++)
        {
            var at = i * TickText.SheenPeriod / 14400;
            maximum = Math.Max(maximum, DextraSmoothRestTests.Distance(At(at), At(at + dt)));
        }
        painter.OnMessage(SheenHandler.Stop.Instance);
        return maximum;
    }
}

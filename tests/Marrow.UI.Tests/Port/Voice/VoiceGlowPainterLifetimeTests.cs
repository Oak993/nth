using Marrow.Core.Port.Voice.Glow;
using Marrow.UI.Port.Voice.Glow;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Voice;

// ED-NEW-voicefix-1 (release crash 2026-10-11, AccessViolation in sk_canvas_draw_drrect under VoiceGlowPainter.DrawInsetShadow): every native object the painter hands to the
// canvas is alive for the whole frame, whatever sizes, themes and states the composer goes through, and odd geometry never reaches Skia.
public sealed class VoiceGlowPainterLifetimeTests
{
    private static (VoiceGlowLook Look, VoiceGlowFrame Frame) LookAndFrame(bool dark, bool processing, double width, double height)
    {
        var look = VoiceGlowLook.From(VoiceGlowComposerProps.For(dark, processing, reduceMotion: false));
        var driver = new VoiceGlowDriver(look.Config);
        var frame = driver.Frame;
        for (var i = 0; i < 12; i++) frame = driver.Step(1.0 / 60, 0.7, width, height);
        return (look, frame);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-voicefix-1")]
    public void A_composer_that_keeps_changing_size_never_draws_a_disposed_round_rect()
    {
        using var bitmap = new SKBitmap(900, 300);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new VoiceGlowPainter();
        // the composer grows a line at a time and its width follows the window: far more geometries than the cache keeps, in both themes, listening and processing
        for (var i = 0; i < 160; i++)
        {
            var width = 400 + i * 3.25;
            var height = 52 + (i % 7) * 18.5;
            var (look, frame) = LookAndFrame(dark: i % 2 == 0, processing: i % 5 == 0, width, height);
            painter.Draw(canvas, null, look, frame, width, height, 1 + (i % 3) * 0.25, 1);
            Assert.Equal(0, painter.UseAfterDispose);
            Assert.True(painter.CachedRoundRects <= VoiceGlowPainter.RoundRectCacheLimit + 8, $"the cache is trimmed between frames ({painter.CachedRoundRects})");
        }
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-voicefix-1")]
    public void Look_and_size_changes_from_the_ui_thread_while_the_render_thread_draws_never_use_a_disposed_object()
    {
        // the handler's arrangement: the UI thread only publishes immutable inputs; the render thread owns the scene and the painter and is the only one to touch native objects
        VoiceGlowInput input = new(true, false, new VoiceGlowLevel { Value = 0.5 }, true, false, 600, 64, VoiceGlowInput.ComposerRadius);
        var stop = 0;
        Exception? failure = null;
        var painter = new VoiceGlowPainter();
        var drawn = 0;
        var render = new Thread(() =>
        {
            try
            {
                var scene = new VoiceGlowScene();
                using var bitmap = new SKBitmap(1000, 400);
                using var canvas = new SKCanvas(bitmap);
                var now = 0.0;
                while (Volatile.Read(ref stop) == 0 || drawn < 200)
                {
                    var s = Volatile.Read(ref input);
                    now += 1.0 / 60;
                    painter.Prepare(null, s.Width, s.Height, 1.25);
                    scene.Advance(now, s);
                    if (scene.Visible && scene.Frame is { } frame && scene.Look is { } look)
                        painter.Draw(canvas, null, look, frame, s.Width, s.Height, 1.25, scene.Opacity);
                    drawn++;
                    if (painter.UseAfterDispose != 0) throw new InvalidOperationException("a disposed native object reached the canvas");
                }
            }
            catch (Exception e) { failure = e; }
            finally { painter.Dispose(); } // disposed on the thread that drew with it, as VoiceGlowStop does
        });
        render.Start();
        var random = new Random(11);
        for (var i = 0; i < 4000; i++)
        {
            Volatile.Write(ref input, new VoiceGlowInput(random.Next(10) != 0, random.Next(3) == 0, new VoiceGlowLevel { Value = random.NextDouble() }, random.Next(2) == 0,
                false, 300 + random.Next(600), 40 + random.Next(300), random.Next(4) == 0 ? 12 : VoiceGlowInput.ComposerRadius));
            if (i % 50 == 0) Thread.Yield();
        }
        Volatile.Write(ref stop, 1);
        Assert.True(render.Join(TimeSpan.FromSeconds(60)), "the render thread finished");
        Assert.Null(failure);
        Assert.True(drawn >= 200);
    }

    [Theory]
    [InlineData(1.5, 40)]       // the half-size layer is narrower than its 1 px inset: the inner rectangle would be inverted (inner larger than outer)
    [InlineData(40, 1.5)]
    [InlineData(2, 2)]
    [InlineData(0, 0)]
    [InlineData(-10, 40)]
    [InlineData(40, -10)]
    [InlineData(double.NaN, 40)]
    [InlineData(40, double.NaN)]
    [InlineData(double.PositiveInfinity, 40)]
    [InlineData(40, double.NegativeInfinity)]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-voicefix-1")]
    public void Degenerate_sizes_draw_nothing_harmful(double width, double height)
    {
        var (look, frame) = LookAndFrame(dark: true, processing: false, 600, 64);
        using var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        using var painter = new VoiceGlowPainter();
        painter.Prepare(null, width, height, 1);
        painter.Draw(canvas, null, look, frame, width, height, 1, 1);
        painter.Draw(canvas, null, look, frame, width, height, double.NaN, 1);
        painter.Draw(canvas, null, look, frame, width, height, 1, 1, z: double.NaN);
        Assert.Equal(0, painter.UseAfterDispose);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-voicefix-1")]
    public void A_disposed_painter_draws_nothing()
    {
        var (look, frame) = LookAndFrame(dark: false, processing: true, 600, 64);
        using var bitmap = new SKBitmap(700, 100);
        using var canvas = new SKCanvas(bitmap);
        var painter = new VoiceGlowPainter();
        painter.Draw(canvas, null, look, frame, 600, 64, 1, 1);
        painter.Dispose();
        painter.Draw(canvas, null, look, frame, 600, 64, 1, 1);
        painter.Prepare(null, 600, 64, 1);
        Assert.Equal(0, painter.UseAfterDispose);
    }
}

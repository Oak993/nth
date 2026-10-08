using Avalonia;
using Avalonia.Media.Imaging;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class AuroraBootHandlerTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-103")]
    public void Waiting_for_initialization_keeps_requesting_frames_even_after_the_designed_floor()
    {
        var clock = new ManualTimeProvider();
        var started = clock.GetTimestamp();
        clock.Advance(TimeSpan.FromSeconds(30));
        var requests = 0;
        var handler = new AuroraBootHandler(() => { }, () => requests++);
        var window = new Rect(0, 0, 64, 64);
        var snapshot = new AuroraBootSnapshot(window.Size, window, new AuroraBootSequence(window, false).At(30, null), 30, true, false, null, clock, started, null);
        try
        {
            handler.OnMessage(snapshot);
            Assert.Equal(1, requests);
            handler.OnAnimationFrameUpdate();
            handler.OnMessage(snapshot);
            Assert.Equal(2, requests);
        }
        finally { handler.OnMessage(false); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-103")]
    public void A_completed_stage_does_not_register_frames_while_ui_detachment_is_delayed(bool reduceMotion) => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider();
        var requests = 0;
        var invalidations = 0;
        var handler = new AuroraBootHandler(() => invalidations++, () => requests++);
        var window = new Rect(0, 0, 64, 64);
        var sequence = new AuroraBootSequence(window, reduceMotion);
        handler.OnMessage(new AuroraBootSnapshot(window.Size, window, sequence.At(0, 0), 0, true, reduceMotion, null, clock, clock.GetTimestamp(), 0));
        clock.Advance(TimeSpan.FromSeconds(5));
        using var bitmap = new RenderTargetBitmap(new PixelSize(64, 64));
        try
        {
            for (var staleCallback = 0; staleCallback < 3; staleCallback++)
            {
                var rendered = false;
                Exception? error = null;
                using (var context = bitmap.CreateDrawingContext())
                    context.Custom(new BootHandlerDrawOperation(immediate =>
                    {
                        try { handler.OnRender(immediate); rendered = true; }
                        catch (Exception exception) { error = exception; }
                    }));
                Assert.Null(error);
                Assert.True(rendered);
                handler.OnAnimationFrameUpdate();
                Assert.Equal(1, requests);
            }
            Assert.True(invalidations >= 2); // queued callback still permits the final transparent paint
        }
        finally { handler.OnMessage(false); }
    });
}

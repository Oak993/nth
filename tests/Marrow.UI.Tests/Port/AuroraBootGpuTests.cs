using Avalonia;
using Avalonia.Media.Imaging;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

// ED-NEW-launch-fix (1): Boot B's first draw is the first thing that links GPU programs, so the app's program cache is
// installed from Boot B's handler on each GPU context, before it draws with that context.
[Collection("Headless")]
public sealed class AuroraBootGpuTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-launchfix-1")]
    public void The_hook_runs_once_for_each_new_context_and_never_for_none()
    {
        var seen = (object?)null;
        var calls = new List<object>();
        var first = new object(); var second = new object();
        Assert.False(AuroraBootGpu.Arrive(ref seen, null, calls.Add));
        Assert.True(AuroraBootGpu.Arrive(ref seen, first, calls.Add));
        Assert.False(AuroraBootGpu.Arrive(ref seen, first, calls.Add));
        Assert.False(AuroraBootGpu.Arrive(ref seen, first, calls.Add));
        Assert.True(AuroraBootGpu.Arrive(ref seen, second, calls.Add)); // a device lost or a display change: a new context
        Assert.Equal(new[] { first, second }, calls);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-launchfix-1")]
    public void A_failing_hook_never_stops_the_draw_and_is_not_retried_for_that_context()
    {
        var seen = (object?)null;
        var calls = 0;
        var context = new object();
        Assert.True(AuroraBootGpu.Arrive<object>(ref seen, context, _ => { calls++; throw new InvalidOperationException("scripted"); }));
        Assert.False(AuroraBootGpu.Arrive<object>(ref seen, context, _ => calls++));
        Assert.Equal(1, calls);
        Assert.False(AuroraBootGpu.Arrive(ref seen, new object(), null)); // no hook set (tests, the CPU renderer): nothing to do
    }

    [Fact]
    [Trait("Decision", "ED-NEW-launchfix-1")]
    public void Without_a_GPU_context_Boot_B_draws_and_the_hook_is_not_called() => headless.OnUiThread(() =>
    {
        var saved = AuroraBootGpu.ContextReady;
        var called = 0;
        AuroraBootGpu.ContextReady = _ => called++;
        var clock = new ManualTimeProvider();
        var handler = new AuroraBootHandler(() => { }, () => { });
        var window = new Rect(0, 0, 64, 64);
        handler.OnMessage(new AuroraBootSnapshot(window.Size, window, new AuroraBootSequence(window, false).At(0, null), 0, true, false, null, clock, clock.GetTimestamp(), null));
        using var bitmap = new RenderTargetBitmap(new PixelSize(64, 64));
        try
        {
            var rendered = false;
            using (var context = bitmap.CreateDrawingContext())
                context.Custom(new BootHandlerDrawOperation(immediate => { handler.OnRender(immediate); rendered = true; }));
            Assert.True(rendered);
            Assert.Equal(0, called); // the headless renderer is Skia on the CPU: no GRContext
        }
        finally { AuroraBootGpu.ContextReady = saved; handler.OnMessage(false); }
    });
}

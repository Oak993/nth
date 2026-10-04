// Source: none (PORT-ONLY, ED-320 15 and 20): the same-frame reach runs inside Avalonia's update on the render thread. When it cannot do its
// work it switches itself off for good, nothing escapes into the update, and it says so once (ResizeStretchLog, ED-101's one-time log).
using System.Reflection;
using Marrow.UI.Port;
using SkiaSharp;
using Xunit;

namespace Marrow.UI.Tests.Port;

public sealed class GlassMaterialReachOffTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    // A reach whose AddRect always refuses, and two rects under its material: the first switches it off with one log line naming the refusal, the
    // second neither calls AddRect nor logs again.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void A_reach_that_switches_off_says_so_once_with_the_reason()
    {
        var logs = new List<string>();
        using var log = GlassMaterial.ReachLog.Override(new ResizeStretchLog(logs.Add));
        var events = typeof(Avalonia.Rendering.Composition.CompositionCustomVisualHandler).Assembly.GetType("Avalonia.Rendering.Composition.ICompositionTargetDebugEvents", true)!;
        var reach = (GlassMaterial.Reach)DispatchProxy.Create(events, typeof(GlassMaterial.Reach));
        var calls = 0;
        reach.Bind(new object(), (_, _) => { calls++; throw new MemberAccessException("refused for the test"); });
        reach.Set(new object(), new GlassMaterial.Spread(new SKRectI(0, 0, 500, 500), 40, default, 36, 28));
        var ltrb = typeof(Avalonia.Rendering.Composition.CompositionCustomVisualHandler).Assembly.GetType("Avalonia.Platform.LtrbRect", true)!;
        object Box(double l, double t, double r, double b)
        {
            var boxed = Activator.CreateInstance(ltrb)!;
            ltrb.GetField("Left")!.SetValue(boxed, l);
            ltrb.GetField("Top")!.SetValue(boxed, t);
            ltrb.GetField("Right")!.SetValue(boxed, r);
            ltrb.GetField("Bottom")!.SetValue(boxed, b);
            return boxed;
        }
        var invalidate = events.GetMethod("RectInvalidated")!;
        invalidate.Invoke(reach, [Box(100, 100, 110, 110)]);
        invalidate.Invoke(reach, [Box(200, 200, 210, 210)]);
        Assert.Equal(1, calls);
        var line = Assert.Single(logs);
        Assert.Contains("MemberAccessException", line, StringComparison.Ordinal);
        Assert.Contains("refused for the test", line, StringComparison.Ordinal);
    }

    // A rect whose edges are near int's limits (an unbounded dirty rect) is grown without overflowing the checked arithmetic: clamped, and the reach stays on.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void A_huge_rect_is_grown_without_overflow()
    {
        var spread = new GlassMaterial.Spread(new SKRectI(0, 0, 500, 500), 66, default, 36, 28);
        var grown = spread.Of(new SKRectI(int.MinValue + 1, int.MinValue + 1, int.MaxValue - 1, int.MaxValue - 1));
        Assert.Equal(new SKRectI(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue), grown);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// PopoverAppear: NSPopover's appearance, measured from 2-chat-use.mov 33.70-33.95 s (relative width 0.34 at 40 ms,
// 0.82 at 115 ms, 0.96 at 165 ms, 1.0 by 218 ms, growing from the arrow; opaque by about 60 ms).
[Collection("Headless")]
public sealed class PopoverAppearTests(HeadlessSession headless)
{
    private static double Scale(Control content) => content.RenderTransform is ScaleTransform s ? s.ScaleX : 1;

    [Fact]
    [Trait("Decision", "ED-107")] // the port-owned presentation (PORT-ONLY seam for .popover)
    public void A_popover_grows_from_its_arrow_on_the_measured_curve_and_fades_in_first() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var card = new Border { Width = 334, Height = 140 };
        void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); }

        PopoverAppear.Play(card, PlacementMode.Top);
        Assert.Equal(new RelativePoint(0.5, 1, RelativeUnit.Relative), card.RenderTransformOrigin); // above its anchor: the arrow is at the bottom
        Assert.Equal(PopoverAppear.StartScale, Scale(card), 3);
        Assert.Equal(0, card.Opacity);

        Step(40);
        Assert.InRange(Scale(card), 0.24, 0.44); // video 0.34
        Assert.InRange(card.Opacity, 0.5, 0.8);
        Step(75); // 115 ms
        Assert.InRange(Scale(card), 0.74, 0.90); // video 0.82
        Assert.Equal(1, card.Opacity);
        Step(50); // 165 ms
        Assert.InRange(Scale(card), 0.90, 0.99); // video 0.96
        for (var i = 0; i < 30; i++) Step(20);
        Assert.Null(card.RenderTransform); // settled: nothing left behind
        Assert.Equal(1, card.Opacity);
    });

    [Theory]
    [Trait("Decision", "ED-107")]
    [InlineData(PlacementMode.Bottom, 0.5, 0.0)]
    [InlineData(PlacementMode.Top, 0.5, 1.0)]
    [InlineData(PlacementMode.Left, 1.0, 0.5)]
    [InlineData(PlacementMode.Right, 0.0, 0.5)]
    public void The_growth_starts_at_the_side_facing_the_anchor(PlacementMode placement, double x, double y) =>
        Assert.Equal(new RelativePoint(x, y, RelativeUnit.Relative), PopoverAppear.Origin(placement));
}

[Collection("Headless")]
public sealed class PopoverDismissTests(HeadlessSession headless)
{
    private static double Scale(Control content) => content.RenderTransform is ScaleTransform s ? s.ScaleX : 1;

    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_popover_shrinks_back_into_its_arrow_and_fades_then_closes() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var card = new Border { Width = 334, Height = 400 };
        void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); }
        var closed = false;

        PopoverAppear.Dismiss(card, PlacementMode.Top, () => closed = true);
        Assert.Equal(new RelativePoint(0.5, 1, RelativeUnit.Relative), card.RenderTransformOrigin);
        Step(43);
        Assert.InRange(Scale(card), 0.52, 0.72); // video 0.62
        Assert.InRange(card.Opacity, 0.6, 0.85);
        Step(50); // 93 ms
        Assert.InRange(Scale(card), 0.25, 0.42); // video 0.33
        Assert.False(closed);
        for (var i = 0; i < 6; i++) Step(20);
        Assert.True(closed); // gone by about 170 ms
        Assert.Null(card.RenderTransform);
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Presenting_again_while_closing_stops_the_close_and_restores_the_popover() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var card = new Border { Width = 100, Height = 100 };
        var closed = false;
        var stop = PopoverAppear.Dismiss(card, PlacementMode.Bottom, () => closed = true);
        clock.Advance(TimeSpan.FromMilliseconds(50)); frames.Advance(TimeSpan.FromMilliseconds(50));
        stop();
        clock.Advance(TimeSpan.FromMilliseconds(500)); frames.Advance(TimeSpan.FromMilliseconds(500));
        Assert.False(closed);
        Assert.Null(card.RenderTransform);
        Assert.Equal(1, card.Opacity);
    });
}

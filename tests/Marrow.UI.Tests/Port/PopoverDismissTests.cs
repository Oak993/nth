using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// PopoverAppear.Dismiss: NSPopover's close, measured from 2-chat-use.mov 42.77-42.94 s.
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

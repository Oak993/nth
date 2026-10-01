using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// SymbolReplace: SF Symbols' replace transition, measured from 2-chat-use.mov 87.75-88.60 s (old symbol draws off over
// about 0.19 s, then the new one grows from about 30 % to full over about 0.18 s).
[Collection("Headless")]
public sealed class SymbolReplaceTests(HeadlessSession headless)
{
    private static double Scale(Control control) => control.RenderTransform is ScaleTransform s ? s.ScaleX : 1;

    [Fact]
    [Trait("Decision", "ED-182")] // the symbol effects ED-182 deferred
    public void The_old_symbol_goes_first_then_the_new_one_grows_in_and_the_old_is_removed_at_the_end() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); }
        var outgoing = new Border { Width = 16, Height = 16 };
        var incoming = new Border { Width = 16, Height = 16 };
        var done = false;

        SymbolReplace.Play(outgoing, incoming, () => done = true);
        Assert.Equal(0, incoming.Opacity); // nothing of the new symbol yet

        Step(100); // the old one is on its way out; the new one has not started
        Assert.InRange(outgoing.Opacity, 0.3, 0.7);
        Assert.True(Scale(outgoing) < 1);
        Assert.Equal(0, incoming.Opacity);

        Step(130); // 230 ms: the old one is gone, the new one is in (opaque within 30 ms), small
        Assert.Equal(0, outgoing.Opacity);
        Assert.Equal(1, incoming.Opacity);
        Assert.InRange(Scale(incoming), SymbolReplace.InStartScale, 0.65); // video about 0.5 at 35 ms of growth

        Step(40); // 270 ms: about the video's 88.28 s, the square most of the way
        Assert.InRange(Scale(incoming), 0.7, 0.97);
        Assert.False(done);
        Step(120);
        Assert.True(done);
        Assert.Null(incoming.RenderTransform);
    });
}

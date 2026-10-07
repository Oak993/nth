using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
public sealed class StrikeRetainedFadeTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.3, 0.5)]
    [InlineData(0.6, 1)]
    [Trait("Decision", "ED-252")]
    public void The_retained_production_frame_crossfades_the_strike_tint(double seconds, double mix) => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 3, fast: true, brand: EffortBrand.Silver, fadeFrom: EffortBrand.Claude);
        rig.Frame(seconds);
        var effect = Assert.Single(rig.Slider.GetVisualDescendants().OfType<TrackEffect>());
        using var bitmap = new RenderTargetBitmap(new PixelSize(298, 34));
        using (var context = bitmap.CreateDrawingContext()) effect.Child!.Render(context);
        Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var frame));
        var expected = StrikeTint.Mix(StrikeTint.Claude, StrikeTint.For(EffortBrand.Silver), mix);
        Assert.Equal(expected.Wash, frame.MixInk.Tint.Wash); // SPEC K-126/K-127 replaces the production A2 light-op carrier.
        Assert.Equal(expected.Ink, frame.BubbleInk, 9);
    });
}

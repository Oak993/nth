using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
public sealed class StrikeToggleIntegrationTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision","ED-252")]
    public void A_kind_toggle_during_a_model_fade_keeps_the_original_ink_fade_origin() => headless.OnUiThread(() =>
    {
        using var rig=JewelFixtures.Rig(index:1,brand:EffortBrand.Silver,fadeFrom:EffortBrand.Claude);
        rig.Frame(0.2); rig.Slider.UpdateLook(true,"Title",null); rig.Frame(0.1);
        var effect=rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single(e=>TrackEffect.IsFast(e.Look)); var frame=Frame(effect);
        var expected=StrikeTint.Mix(StrikeTint.Claude,StrikeTint.For(EffortBrand.Silver),Strike.Ss(0,0.6,0.3));
        Assert.Equal(expected.Wash,frame.MixInk.Tint.Wash); Assert.Equal(expected.Ink,frame.MixInk.Amount,12);
    });
    private static LiquidTrack.Frame Frame(TrackEffect effect)
    {
        using var bitmap=new RenderTargetBitmap(new PixelSize((int)effect.Bounds.Width,34)); using(var context=bitmap.CreateDrawingContext()) effect.Child!.Render(context);
        Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var frame)); return frame;
    }
    [Fact]
    [Trait("Decision","ED-248")]
    public void A_solo_kind_replacement_keeps_the_tubes_clock_lanes_and_half_second_fast_ramp() => headless.OnUiThread(() =>
    {
        using var rig=JewelFixtures.Rig(index:1); rig.Frame(); var old=Assert.Single(rig.Slider.GetVisualDescendants().OfType<TrackEffect>()); var before=Frame(old);
        rig.Slider.UpdateLook(true,"Title",null); rig.Frame(); var fast=rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single(e=>TrackEffect.IsFast(e.Look)); var first=Frame(fast);
        Assert.NotSame(old,fast); Assert.Same(before.MixLanes,first.MixLanes); Assert.Same(before.FastClock,first.FastClock); Assert.Equal(old.StrikeSeed,fast.StrikeSeed);
        var at=fast.Time; rig.Frame(0.1); var rising=Frame(fast); Assert.Equal(Strike.Round(Strike.Ss(0,0.5,fast.Time-at)*1000)/1000d,rising.Mix!.Opacity);
        rig.Frame(0.5); Assert.Equal(1,Frame(fast).Mix!.Opacity);
        rig.Slider.UpdateLook(false,"Title",null); rig.Frame(); var slow=rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single(e=>!TrackEffect.IsFast(e.Look)); var falling=Frame(slow);
        Assert.NotSame(fast,slow); Assert.Same(before.MixLanes,falling.MixLanes); Assert.Same(before.FastClock,falling.FastClock); Assert.Equal(1,falling.Mix!.Opacity);
        at=slow.Time; rig.Frame(0.1); falling=Frame(slow); Assert.Equal(Strike.Round((1-Strike.Ss(0,0.5,slow.Time-at))*1000)/1000d,falling.Mix!.Opacity);
        rig.Frame(0.5); Assert.Equal(0,Frame(slow).Mix!.Opacity);
    });
}

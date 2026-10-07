using Avalonia;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Collection("Headless")]
[Trait("Decision","ED-NEW-modelspalette-18")]
public sealed class PaletteLoadingAndTimingTests(HeadlessSession headless)
{
    [Fact]
    public async Task The_first_visible_retarget_frame_starts_at_zero_and_effort_motion_takes_point_six_seconds()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        async Task<PaletteTrackTarget> Target(string effort){var s=new[]{new PaletteRenderSelection("openai/gpt-6-astra",effort)};return await engine.PrepareRender(s,[],false,(snapshot,c)=>PaletteTrackTarget.Create(snapshot,s,[],false,c!));}
        var a=await Target("low");var b=await Target("max");
        headless.OnUiThread(()=>
        {
            using var platform=Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());using var clock=SwiftClock.Provider.Override(fake.Time);using var frames=FrameClock.Source.Override(new ManualFrameSource());
            var effect=new TrackEffect(new TrackLook(TrackLook.Kind.Plain,EffortBrand.Purple,null));effect.PrepareScene(new Size(300,34));effect.SetPalette(a);
            Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var initial));var lanes=initial.MixLanes;
            effect.SetPalette(b);fake.Time.Advance(TimeSpan.FromSeconds(1));var first=effect.SamplePalette(SwiftDate.Now)!;
            Assert.Equal(0,first.Frame.Progress);Assert.Equal(a.Composite.Shade.Motion["glow_alpha"],first.Motion.Get("glow_alpha",0),12);
            fake.Time.Advance(TimeSpan.FromSeconds(.3));var middle=effect.SamplePalette(SwiftDate.Now)!;
            Assert.Equal(1,middle.Frame.Progress);Assert.Equal((a.Composite.Shade.Motion["glow_alpha"]+b.Composite.Shade.Motion["glow_alpha"])/2,middle.Motion.Get("glow_alpha",0),10);
            Assert.Same(lanes,initial.MixLanes);
        });
    }
    [Fact]
    public async Task Pending_uses_living_water_and_loading_until_three_seconds_then_reduce_motion_holds_the_carried_colour()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.Observe(["unlisted/model-1"]);
        var selected=new[]{new PaletteRenderSelection("unlisted/model-1","max")};var target=await engine.PrepareRender(selected,[],false,(s,c)=>PaletteTrackTarget.Create(s,selected,[],false,c!));
        headless.OnUiThread(()=>
        {
            using var platform=Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform{AccessibilityDisplayShouldReduceMotion=true});using var clock=SwiftClock.Provider.Override(fake.Time);using var frames=FrameClock.Source.Override(new ManualFrameSource());
            var effect=new TrackEffect(new TrackLook(TrackLook.Kind.Plain,EffortBrand.Purple,null));effect.PrepareScene(new Size(300,34));effect.SetValue(EnvironmentValues.AccessibilityReduceMotionProperty,true);Assert.True(EnvironmentValues.GetAccessibilityReduceMotion(effect));effect.SetPalette(target);
            Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var first));Assert.True(first.Palette!.Water);Assert.True(first.Palette.Loading);
            // A new scene requests the existing live-source frame at the fake clock without changing effect identity.
            fake.Time.Advance(TimeSpan.FromSeconds(2.99));effect.PrepareScene(new Size(300,34));Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var waiting));Assert.True(waiting.Palette!.Water);
            fake.Time.Advance(TimeSpan.FromSeconds(.01));effect.PrepareScene(new Size(300,34));Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var held));Assert.False(held.Palette!.Water);Assert.False(held.Palette.Loading);Assert.Equal(1,held.Palette.SolidMix);
        });
    }
    [Fact]
    public async Task An_unrelated_snapshot_does_not_replace_the_running_glides_GPU_from_row()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        async Task<PaletteTrackTarget> Target(string effort){var selected=new[]{new PaletteRenderSelection("acme/nova-1",effort)};return await engine.PrepareRender(selected,[],false,(snapshot,c)=>PaletteTrackTarget.Create(snapshot,selected,[],false,c!));}
        var a=await Target("low");var b=await Target("max");
        headless.OnUiThread(()=>
        {
            using var clock=SwiftClock.Provider.Override(fake.Time);using var frames=FrameClock.Source.Override(new ManualFrameSource());
            var effect=new TrackEffect(new TrackLook(TrackLook.Kind.Plain,EffortBrand.Purple,null));effect.PrepareScene(new Size(300,34));effect.SetPalette(a);effect.ReadMixSignal(new KnobSignal(),0,0,SwiftDate.Now);
            effect.SetPalette(b);effect.ReadMixSignal(new KnobSignal(),0,0,SwiftDate.Now);fake.Time.Advance(TimeSpan.FromSeconds(.15));effect.ReadMixSignal(new KnobSignal(),0,0,SwiftDate.Now);
            Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var before));var row=before.PreviousGradient!.ToArray();var mix=before.GradientMix;
            Assert.InRange(mix,.499999,.500001);
            effect.SetPalette(b with {SnapshotDigest=b.SnapshotDigest+"-fixture-unrelated"});effect.ReadMixSignal(new KnobSignal(),0,0,SwiftDate.Now);
            Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var after));Assert.Equal(mix,after.GradientMix);Assert.Equal(row,after.PreviousGradient);
        });
    }

}

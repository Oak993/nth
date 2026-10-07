using Avalonia;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Marrow.UI.Port;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Collection("Headless")]
[Trait("Decision","ED-NEW-modelspalette-21")]
public sealed class PaletteArrivalFloorTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Arrival_waits_until_point_eight_only_if_a_Pending_frame_was_shown(bool shown)
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        var selection=new[]{new PaletteRenderSelection("acme/nova-1","max")};await engine.Observe([selection[0].Id]);
        var pending=await engine.PrepareRender(selection,[],false,(s,c)=>PaletteTrackTarget.Create(s,selection,[],false,c!));Assert.Equal(PaletteState.Pending,pending.State);
        await engine.CheckNow();var ready=await engine.PrepareRender(selection,[],false,(s,c)=>PaletteTrackTarget.Create(s,selection,[],false,c!));Assert.Equal(PaletteState.Ready,ready.State);
        headless.OnUiThread(()=>
        {
            using var clock=SwiftClock.Provider.Override(fake.Time);using var frames=FrameClock.Source.Override(new ManualFrameSource());
            var effect=new TrackEffect(new TrackLook(TrackLook.Kind.Plain,EffortBrand.Purple,null));effect.PrepareScene(new Size(300,34));effect.SetPalette(pending);
            if(shown){effect.ReadMixSignal(new KnobSignal(),0,0,SwiftDate.Now);Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var water));Assert.True(water.Palette!.Water);}
            fake.Time.Advance(TimeSpan.FromSeconds(.1));effect.SetValue(EnvironmentValues.AccessibilityReduceMotionProperty,shown);effect.SetPalette(ready);effect.ReadMixSignal(new KnobSignal(),0,0,SwiftDate.Now);
            Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var first));Assert.Equal(shown,first.Palette!.Water);
            if(shown)
            {
                fake.Time.Advance(TimeSpan.FromTicks(6_900_000));effect.ReadMixSignal(new KnobSignal(),0,0,SwiftDate.Now);Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var waiting));Assert.True(waiting.Palette!.Water);
                fake.Time.Advance(TimeSpan.FromSeconds(.01));effect.ReadMixSignal(new KnobSignal(),0,0,SwiftDate.Now);Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var settled));Assert.False(settled.Palette!.Water);
            }
            else{Assert.Equal(1,first.Palette.Frame.Progress);Assert.Equal(ready.Composite.Shade.GradientRow,first.Palette.Row);}
            effect.SetPalette(null);
        });
    }
}

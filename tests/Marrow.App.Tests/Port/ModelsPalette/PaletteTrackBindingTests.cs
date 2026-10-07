using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Marrow.UI.Port;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Collection("Headless")]
[Trait("Decision","ED-NEW-modelspalette-14")]
public sealed class PaletteTrackBindingTests(HeadlessSession headless)
{
    [Fact]
    public async Task The_bound_Strike_retains_its_clock_and_reads_every_precomputed_model_row_and_dial()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        var workers=new[]{new PaletteRenderSelection("acme/nova-1","low"),new PaletteRenderSelection("openai/gpt-6-astra","high")};var leads=new[]{new PaletteRenderSelection("openai/gpt-6-astra","max")};
        var target=await engine.PrepareRender(workers,leads,false,(snapshot,composite)=>PaletteTrackTarget.Create(snapshot,workers,leads,false,composite!));
        headless.OnUiThread(()=>
        {
            using var platform=Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());using var clock=SwiftClock.Provider.Override(fake.Time);using var frames=FrameClock.Source.Override(new ManualFrameSource());
            var effect=new TrackEffect(new TrackLook(TrackLook.Kind.Plain,EffortBrand.Purple,null));effect.PrepareScene(new Size(300,34));var at=effect.TimeAt(Marrow.Core.Port.SwiftDate.Now);
            effect.SetPalette(target);Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var first));Assert.Equal(at,effect.TimeAt(Marrow.Core.Port.SwiftDate.Now));
            Assert.Equal(new[]{"openai/gpt-6-astra","acme/nova-1","openai/gpt-6-astra"},effect.PaletteArrangement!.Spans.Select(s=>s.ModelId));
            Assert.Equal(3,first.Spec.Segments.Count);Assert.Equal(5/18d,first.Spec.Segments[0].Weight/first.Spec.Segments.Sum(s=>s.Weight),12);Assert.Equal(256,first.Gradient.Length);
            for(var n=0;n<256;n++)Assert.Equal(new SkiaSharp.SKColor(target.Composite.Shade.GradientRow[n*4],target.Composite.Shade.GradientRow[n*4+1],target.Composite.Shade.GradientRow[n*4+2]),first.Gradient[n]);
            Assert.NotNull(first.Palette);foreach(var pair in target.Composite.Shade.Motion)Assert.Equal(pair.Value,first.Palette!.Motion.Get(pair.Key,0),12);
            Assert.NotNull(effect.Layers);Assert.False(ModelShades.ApplyToUi);
            using var bitmap=new SkiaSharp.SKBitmap(300,34);using var canvas=new SkiaSharp.SKCanvas(bitmap);effect.Layers!.Draw(canvas,null,1,first);
            Assert.NotEqual(SkiaSharp.SKColors.Transparent,bitmap.GetPixel(150,17));
        });
    }
    [Fact]
    public async Task The_actual_slider_adapter_draws_more_than_three_models_without_replacing_the_Strike()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        var workers=new[]{new PaletteRenderSelection("acme/nova-1","low"),new PaletteRenderSelection("openai/gpt-6-astra","high"),new PaletteRenderSelection("acme/nova-1","medium"),new PaletteRenderSelection("openai/gpt-6-astra","max")};var leads=new[]{new PaletteRenderSelection("acme/nova-1","high")};
        var target=await engine.PrepareRender(workers,leads,false,(snapshot,composite)=>{Assert.Equal(ThreadPriority.BelowNormal,Thread.CurrentThread.Priority);return PaletteTrackTarget.Create(snapshot,workers,leads,false,composite!);});
        headless.OnUiThread(()=>
        {
            using var platform=Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());using var clock=SwiftClock.Provider.Override(fake.Time);using var frames=FrameClock.Source.Override(new ManualFrameSource());
            var index=0;var slider=new EffortSlider(3,new Binding<int>(()=>index,x=>index=x),"Low");var window=new Avalonia.Controls.Window{Width=340,Height=100,Content=slider};window.Show();Dispatcher.UIThread.RunJobs();
            try
            {
                var effect=Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());var at=effect.TimeAt(SwiftDate.Now);
                slider.SetPalette(target);effect.PrepareScene(new Size(300,34));Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var f));
                Assert.Equal(5,f.Spec.Segments.Count);Assert.Equal(5,f.StrandColors.Count);Assert.Equal(target.Composite.Arrangement,effect.PaletteArrangement);Assert.Equal(at,effect.TimeAt(SwiftDate.Now));
                Assert.Same(effect,Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>()));
                using var bitmap=new SkiaSharp.SKBitmap(300,34);using var canvas=new SkiaSharp.SKCanvas(bitmap);effect.Layers!.Draw(canvas,null,1,f);Assert.NotEqual(SkiaSharp.SKColors.Transparent,bitmap.GetPixel(150,17));
                slider.SetPalette(null);Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var reset));Assert.Null(reset.Palette);
            }
            finally{window.Close();}
        });
    }

    [Fact]
    public async Task A_bound_target_requested_during_arrival_keeps_the_displayed_rows_and_arrangement()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        var a=new[]{new PaletteRenderSelection("acme/nova-1","low")};var b=new[]{new PaletteRenderSelection("openai/gpt-6-astra","max")};
        var first=await engine.PrepareRender(a,[],false,(s,c)=>PaletteTrackTarget.Create(s,a,[],false,c!));var next=await engine.PrepareRender(b,[],false,(s,c)=>PaletteTrackTarget.Create(s,b,[],false,c!));
        headless.OnUiThread(()=>
        {
            using var platform=Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());using var clock=SwiftClock.Provider.Override(fake.Time);using var frames=FrameClock.Source.Override(new ManualFrameSource());
            var effect=new TrackEffect(new TrackLook(TrackLook.Kind.Plain,EffortBrand.Purple,null));effect.PrepareScene(new Size(300,34));effect.SetPalette(first);effect.SetPalette(next,arrivalRunning:true);
            Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var waiting));Assert.Same(first.Gradient,waiting.Gradient);Assert.Equal(first.Composite.Arrangement,effect.PaletteArrangement);
            var at=effect.TimeAt(SwiftDate.Now);effect.PaletteArrivalFinished();Assert.Equal(next.Composite.Arrangement,effect.PaletteArrangement);Assert.Equal(at,effect.TimeAt(SwiftDate.Now));
            Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var released));Assert.Equal(0,released.GradientMix);Assert.Equal(0,released.Palette!.Frame.Progress);
        });
    }

}

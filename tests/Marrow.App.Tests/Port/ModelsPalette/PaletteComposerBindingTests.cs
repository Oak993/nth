using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Tests.App.Fixtures;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Marrow.UI.Port;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Collection("Headless")]
[Trait("Decision","ED-NEW-modelspalette-14")]
public sealed class PaletteComposerBindingTests(HeadlessSession headless)
{
    [Fact]
    public async Task The_gated_card_binding_maps_actual_provider_choices_on_the_actor_and_keeps_the_same_effect_when_effort_changes()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        headless.OnUiThread(()=>
        {
            using var platform=Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());using var clock=SwiftClock.Provider.Override(fake.Time);using var frames=FrameClock.Source.Override(new ManualFrameSource());using var harness=new AppModelHarness(withRuntime:false);
            var thread=new ChatThread(Guid.NewGuid(),ProviderKind.OpenRouter,"acme/nova-1","medium",RuntimeMode.Supervised){HydraEnabled=false};harness.WriteLibrary([],[thread]);var model=new AppModel();
            typeof(AppModel).GetField("paletteEngine",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(model,engine);
            HydraCookbookResolveFixture.SetModels(model.Providers,ProviderKind.OpenRouter,new ModelOption(thread.Model!,"Acme: Nova 1"){Efforts=new(["low","medium","high"]),DefaultEffort="medium"});
            var card=new EffortSliderCard("Nova 1",ProviderKind.OpenRouter,new(["low","medium","high"]),"medium",false,new Binding<string?>(()=>model.Thread(thread.Id)!.Effort,x=>model.UpdateThread(thread.Id,t=>t with {Effort=x})),new Binding<bool>(()=>false,_=>{}),()=>{});
            var binding=Assert.IsType<PaletteComposerBinding>(PaletteComposerBinding.Attach(model,thread.Id,card));
            var window=new Avalonia.Controls.Window{Width=340,Height=220,Content=card};window.Show();Dispatcher.UIThread.RunJobs();
            try
            {
                HeadlessSession.RunDispatcherUntil(binding.PendingRender,TimeSpan.FromSeconds(5));
                var effect=Assert.Single(card.GetVisualDescendants().OfType<TrackEffect>());effect.PrepareScene(new Avalonia.Size(300,34));Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var first));Assert.NotNull(first.Palette);Assert.Equal("acme/nova-1",Assert.Single(effect.PaletteArrangement!.Spans).ModelId);
                var at=effect.TimeAt(SwiftDate.Now);model.UpdateThread(thread.Id,t=>t with {Effort="low"});Dispatcher.UIThread.RunJobs();HeadlessSession.RunDispatcherUntil(binding.PendingRender,TimeSpan.FromSeconds(5));
                Assert.Same(effect,Assert.Single(card.GetVisualDescendants().OfType<TrackEffect>()));Assert.Equal(at,effect.TimeAt(SwiftDate.Now));Assert.True(ModelShades.ApplyToUi);
            }
            finally{window.Close();model.PaletteShades.Dispose();}
        });
    }
}

using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Tests.App.Fixtures;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Collection("Headless")]
[Trait("Decision","ED-NEW-modelspalette-13")]
public sealed class PalettePickerBindingTests(HeadlessSession headless)
{
    [Fact]
    public void The_real_OpenRouter_model_list_uses_the_approved_picker_and_keeps_history_and_back()=>headless.OnUiThread(()=>
    {
        using var motion=Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());using var harness=new AppModelHarness(withRuntime:false);using var frameScope=FrameClock.Source.Override(new ManualFrameSource());
        var thread=new ChatThread(Guid.NewGuid(),ProviderKind.OpenRouter,"anthropic/claude-sonnet-4.5","high",RuntimeMode.Supervised);harness.WriteLibrary([],[thread]);var model=new AppModel();
        foreach(var provider in SwiftEnum<ProviderKind>.AllCases)if(!provider.IsAPIKeyBased)model.Settings.SetBinaryPath(Path.Combine(harness.Scratch,"missing.exe"),provider);
        var sonnet=new ModelOption(thread.Model!,"Anthropic: Claude Sonnet 4.5");var kimi=new ModelOption("moonshotai/kimi-k2","Moonshot AI: Kimi K2");var codex=new ModelOption("gpt-6-sol","GPT-6 Sol");
        HydraCookbookResolveFixture.SetModels(model.Providers,ProviderKind.OpenRouter,sonnet,kimi);HydraCookbookResolveFixture.SetModels(model.Providers,ProviderKind.Codex,codex);
        foreach(var entry in new[]{new ModelCatalog.Entry(ProviderKind.OpenRouter,sonnet),new ModelCatalog.Entry(ProviderKind.OpenRouter,kimi),new ModelCatalog.Entry(ProviderKind.Codex,codex)})model.Settings.AddToModelList(new ModelPin(entry.Provider,entry.Option.Id));
        ModelCatalog.Entry? chosen=null;var back=0;var list=new ModelList(thread,true,e=>chosen=e,()=>back++);var host=new Border{Child=list};AppEnvironment.SetAppModel(host,model);
        var window=new Avalonia.Controls.Window{Content=host,Width=330,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var picker=Assert.Single(list.GetVisualDescendants().OfType<OpenRouterPicker>());Assert.Equal(3,picker.Rows.Count);Assert.False(picker.Rows.Single(r=>r.Model.Id=="codex/gpt-6-sol").Model.Enabled);
            picker.Search.Text="kimi";Dispatcher.UIThread.RunJobs();picker.Search.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Assert.Equal(kimi,chosen?.Option);
            picker.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(1,back);
        }
        finally{window.Close();}
    });
    [Fact]
    public void Hydra_choices_keep_their_existing_rows_callbacks_and_provider_history_guard()=>headless.OnUiThread(()=>
    {
        using var motion=Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());using var harness=new AppModelHarness(withRuntime:false);using var frameScope=FrameClock.Source.Override(new ManualFrameSource());var model=new AppModel();
        var thread=new ChatThread(Guid.NewGuid(),ProviderKind.OpenRouter,"anthropic/claude-sonnet-4.5",null,RuntimeMode.Supervised);
        var allowed=new HydraPair(ProviderKind.OpenRouter){Name="Allowed"};var locked=new HydraPair(ProviderKind.Codex){Name="Locked"};HydraPair? selected=null;
        var content=ModelPickerBinding.TryCreate(model,thread,true,[new(ProviderKind.OpenRouter,new ModelOption(thread.Model!,"Sonnet"))],new([allowed,locked]),_=>{},()=>{},p=>selected=p);
        Assert.NotNull(content);var rows=content.GetLogicalDescendants().OfType<HydraPairListRow>().ToArray();Assert.Equal(2,rows.Length);
        var buttons=rows.SelectMany(r=>r.GetLogicalDescendants().OfType<Button>()).ToArray();Assert.True(buttons[0].IsEnabled);Assert.False(buttons[1].IsEnabled);
        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(allowed,selected);
    });
}

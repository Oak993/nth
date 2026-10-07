// PORT-ONLY: RR-19's additive integration seam, for Strike to consume after its EffortSlider merge;
// approved Q-PICKER A; ED-NEW-models-palette-5. This file does not replace or alter the Strike-owned files.
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port.ModelsPalette;
using Marrow.UI.Port;
namespace Marrow.App.Port.ModelsPalette;
public static class ModelPickerSeam
{
    /// <summary>Preserves the original Choose callback (including the history/provider guard) and the Back callback.</summary>
    public static OpenRouterPicker Create(IReadOnlyList<ModelCatalog.Entry> entries,ModelCatalog.Entry? selected,bool hasHistory,
        Action<ModelCatalog.Entry> choose,Action back,Func<ModelCatalog.Entry,string> colour,ProviderKind? historyProvider=null,bool leadingPair=false)
    {
        var allowedProvider=historyProvider??selected?.Provider;
        var choices=entries.ToDictionary(e=>e.Id,StringComparer.Ordinal);
        var models=entries.Select(e=>
        {
            var maker=Makers.Resolve(e.Provider.Id,e.Option.Id,PaletteSourcePack.Default.Baseline);
            var name=maker.Maker switch {"anthropic"=>"Anthropic","openai"=>"OpenAI","google"=>"Google","meta"=>"Meta","deepseek"=>"DeepSeek","x-ai"=>"xAI","mistralai"=>"Mistral AI","moonshotai"=>"Moonshot AI","qwen"=>"Qwen",var x=>x};
            var enabled=!hasHistory||allowedProvider is null||e.Provider==allowedProvider;
            return new PickerModel(e.Id,e.Option.ShortName,name,colour(e),enabled,enabled?e.Option.Id+" · "+e.Provider.DisplayName:"New chats only");
        }).ToArray();
        return new(models,leadingPair?"":selected?.Id??"",id=>choose(choices[id]),back);
    }
    /// <summary>The approved card ends 10px above the chip throughout its height easing.</summary>
    public static void Place(Popup popup)
    {popup.Placement=PlacementMode.Top;popup.VerticalOffset=-OpenRouterPicker.ChipClearance;}
}

// PORT-ONLY: approved Q-PICKER A after T9 merged Strike; preserve original pair/Choose/Back guards.
// ED-NEW-models-palette-13. The legacy Hydra rows surround the unchanged approved picker.
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.App.Port.ModelsPalette;
public static class ModelPickerBinding
{
    public static Control? TryCreate(AppModel model,ChatThread thread,bool hasHistory,IReadOnlyList<ModelCatalog.Entry> entries,ValueList<HydraPair> pairs,Action<ModelCatalog.Entry> choose,Action back,Action<HydraPair>? enterPair,Control? previous=null)
    {
        if(thread.Provider!=ProviderKind.OpenRouter)return null;
        var selected=entries.FirstOrDefault(e=>e.Provider==thread.Provider&&e.Option.Id==thread.Model);
        var picker=ModelPickerSeam.Create(entries,selected,hasHistory,choose,back,LockedColour,thread.Provider,pairs.Any(p=>model.LeadsWithHydraPair(p,thread)));
        PalettePickerColours.Attach(picker,model.PaletteShades);
        if(previous is not null&&((previous as OpenRouterPicker)??previous.GetLogicalDescendants().OfType<OpenRouterPicker>().FirstOrDefault()) is {} old)picker.Search.Text=old.Search.Text;
        if(pairs.Count==0)return picker;
        var rows=new StackPanel{Spacing=1};rows.Children.Add(new PopoverSectionHeader("Pairs"){Height=22});
        foreach(var pair in pairs)
        {
            var locked=hasHistory&&pair.Provider!=thread.Provider;
            var detail=string.Join(" · ",new[]{pair.CustomName is null?null:HydraPairSummary.ModelsTitle(pair,model.Providers),HydraPairSummary.Providers(pair),HydraPairSummary.Efforts(pair)}.Where(x=>x is not null));
            rows.Children.Add(new HydraPairListRow(pair,HydraPairSummary.Title(pair,model.Providers),locked?"New chats only":detail,46,model.LeadsWithHydraPair(pair,thread),!locked,()=>enterPair?.Invoke(pair)));
        }
        return new StackPanel{Children={rows,picker}};
    }
    private static string LockedColour(ModelCatalog.Entry entry)
    {
        var source=PaletteSourcePack.Default;var maker=Makers.Resolve(entry.Provider.Id,entry.Option.Id,source.Baseline,entry.Option.ShortName);
        return source.AuthoritativeForMaker(maker.Maker).Stops[1];
    }
}

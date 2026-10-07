// PORT-ONLY: ready-to-enable C19/C25 card binding after T9 merged Strike; ED-NEW-models-palette-14.
// W2 remains an explicit build gate. A closed gate neither creates an engine nor changes a source-twin card.
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.ModelsPalette;
namespace Marrow.App.Port.ModelsPalette;
internal sealed class PaletteComposerBinding
{
    private readonly AppModel model;private readonly Guid threadId;private readonly EffortSliderCard card;
    private readonly PaletteEngine engine;private readonly ModelShades facade;
    private long intent;private bool attached;private string? last;
    private PaletteComposerBinding(AppModel model,Guid threadId,EffortSliderCard card){this.model=model;this.threadId=threadId;this.card=card;engine=model.PaletteEngine;facade=model.PaletteShades;}
    internal Task PendingRender {get;private set;}=Task.CompletedTask;
    internal static PaletteComposerBinding? Attach(AppModel model,Guid threadId,EffortSliderCard card,bool apply=ModelShades.ApplyToUi)
    {
        if(!apply)return null;
        var binding=new PaletteComposerBinding(model,threadId,card);
        card.AttachedToVisualTree+=(_,_)=>{if(binding.attached)return;binding.attached=true;binding.facade.Changed+=binding.PaletteChanged;model.PropertyChanged+=binding.ModelChanged;card.ActualThemeVariantChanged+=binding.ThemeChanged;binding.Update();};
        card.DetachedFromVisualTree+=(_,_)=>{binding.attached=false;binding.intent++;binding.facade.Changed-=binding.PaletteChanged;model.PropertyChanged-=binding.ModelChanged;card.ActualThemeVariantChanged-=binding.ThemeChanged;binding.last=null;};
        return binding;
    }
    private void PaletteChanged()=>Update();private void ModelChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)=>Update();private void ThemeChanged(object? sender,EventArgs e)=>Update();
    private void Update()
    {
        if(!attached||model.Thread(threadId) is not {} thread)return;
        var (workers,leads)=PaletteSurfaceBinding.Selections(model,thread);
        var light=card.ActualThemeVariant==ThemeVariant.Light;
        var fingerprint=facade.Current.Digest+"|"+thread.FastMode+"|"+light+"|"+string.Join('|',workers.Concat(leads));if(fingerprint==last)return;last=fingerprint;
        var generation=++intent;PendingRender=Apply(workers,leads,light,generation);
    }
    private async Task Apply(PaletteProviderRenderSelection[] workers,PaletteProviderRenderSelection[] leads,bool light,long generation)
    {
        var target=await engine.PrepareProviderRender(workers,leads,light,(snapshot,w,l,composite)=>composite is null?null:PaletteTrackTarget.Create(snapshot,w,l,light,composite)).ConfigureAwait(false);
        await MainActor.Run(()=>{if(!attached||generation!=intent)return;card.SetPalette(target);}).ConfigureAwait(false);
    }
}

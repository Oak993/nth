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
        var workers=new List<PaletteProviderRenderSelection>();
        PaletteProviderRenderSelection? Selection(ProviderKind provider,string? id,string? effort)
        {var option=model.Providers.Model(id,provider)??(id is null?model.Providers.DefaultModel(provider):null);return (option?.Id??id) is {} resolved?new(provider.Id,resolved,option?.Name??resolved,effort):null;}
        var heads=model.HydraIsOn(thread)?model.HydraHeads(threadId):ValueList<ChatThread>.Empty;
        foreach(var head in heads)if(Selection(head.Provider,head.Model,head.Effort) is {} selected)workers.Add(selected);
        // An unstarted pair uses its actual launch selections, following AppModel+Hydra's routing/default rules.
        if(workers.Count==0&&model.HydraIsOn(thread)&&model.HydraLaunch(thread) is {} launch)
        {
            if(launch.HeadProfiles.Count==0){var p=launch.HeadsProvider;if(Selection(p,launch.WorkerModel??(p==thread.Provider?thread.Model:null),launch.WorkerEffort??(p==thread.Provider?thread.Effort:null)) is {} selected)workers.Add(selected);}
            else foreach(var profile in launch.HeadProfiles){var p=profile.Provider??launch.HeadsProvider;if(Selection(p,profile.Model??launch.WorkerModel??(p==thread.Provider?thread.Model:null),profile.Effort??launch.WorkerEffort??(p==thread.Provider?thread.Effort:null)) is {} selected)workers.Add(selected);}
        }
        var leads=Selection(thread.Provider,thread.Model,thread.Effort) is {} lead?new[]{lead}:Array.Empty<PaletteProviderRenderSelection>();
        var light=card.ActualThemeVariant==ThemeVariant.Light;
        var fingerprint=facade.Current.Digest+"|"+light+"|"+string.Join('|',workers.Concat(leads));if(fingerprint==last)return;last=fingerprint;
        var generation=++intent;PendingRender=Apply(workers.ToArray(),leads,light,generation);
    }
    private async Task Apply(PaletteProviderRenderSelection[] workers,PaletteProviderRenderSelection[] leads,bool light,long generation)
    {
        var target=await engine.PrepareProviderRender(workers,leads,light,(snapshot,w,l,composite)=>composite is null?null:PaletteTrackTarget.Create(snapshot,w,l,light,composite)).ConfigureAwait(false);
        await MainActor.Run(()=>{if(!attached||generation!=intent)return;card.GetLogicalDescendants().OfType<EffortSlider>().FirstOrDefault()?.SetPalette(target);}).ConfigureAwait(false);
    }
}

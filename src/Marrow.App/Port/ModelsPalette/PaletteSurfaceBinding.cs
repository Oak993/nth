// PORT-ONLY: C20 read-only visible surfaces share the process facade; actor prepares every colour.
// OD-0048/0050/0051/0063..0066, OD-0089; ED-NEW-models-palette-17.
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.Core.Models;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Services.Port.ModelsPalette;
using Marrow.UI.Chrome;
namespace Marrow.App.Port.ModelsPalette;
internal sealed record PaletteSurfaceTarget(PaletteTrackTarget? Track,ComposerLightPalette Composer);
internal sealed class PaletteSurfaceBinding
{
    private readonly Control owner;private readonly AppModel model;private readonly Guid id;private readonly Action<PaletteSurfaceTarget> apply;
    private readonly PaletteEngine engine;private readonly ModelShades facade;private long intent;private bool attached;private string? fingerprint;
    internal Task Pending {get;private set;}=Task.CompletedTask;
    private PaletteSurfaceBinding(Control owner,AppModel model,Guid id,Action<PaletteSurfaceTarget> apply)
    {this.owner=owner;this.model=model;this.id=id;this.apply=apply;engine=model.PaletteEngine;facade=model.PaletteShades;}
    internal static PaletteSurfaceBinding Attach(Control owner,AppModel model,Guid id,Action<PaletteSurfaceTarget> apply)
    {
        var binding=new PaletteSurfaceBinding(owner,model,id,apply);
        owner.AttachedToVisualTree+=(_,_)=>binding.Start();owner.DetachedFromVisualTree+=(_,_)=>binding.Stop();
        if(owner.IsAttachedToVisualTree())binding.Start();return binding;
    }
    private void Start(){if(attached)return;attached=true;facade.Changed+=Update;model.PropertyChanged+=Changed;owner.ActualThemeVariantChanged+=Changed;Update();}
    private void Stop(){attached=false;intent++;fingerprint=null;facade.Changed-=Update;model.PropertyChanged-=Changed;owner.ActualThemeVariantChanged-=Changed;}
    private void Changed(object? sender,EventArgs e)=>Update();
    private void Update()
    {
        if(!attached||model.Thread(id) is not {} thread)return;
        var (workers,leads)=Selections(model,thread);var light=owner.ActualThemeVariant==ThemeVariant.Light;
        var accentDark=Chrome.Accent.Resolve(ThemeVariant.Dark);var accentLight=Chrome.Accent.Resolve(ThemeVariant.Light);
        var key=facade.Current.Digest+"|"+thread.FastMode+"|"+light+"|"+string.Join('|',workers.Concat(leads))+"|"+accentDark+"|"+accentLight;
        if(key==fingerprint)return;fingerprint=key;Pending=Prepare(workers,leads,light,++intent,$"#{accentDark.R:X2}{accentDark.G:X2}{accentDark.B:X2}",$"#{accentLight.R:X2}{accentLight.G:X2}{accentLight.B:X2}");
    }
    private async Task Prepare(PaletteProviderRenderSelection[] workers,PaletteProviderRenderSelection[] leads,bool light,long generation,string accentDark,string accentLight)
    {
        var target=await engine.PrepareProviderRender(workers,leads,light,(snapshot,w,l,c)=>new PaletteSurfaceTarget(c is null?null:PaletteTrackTarget.Create(snapshot,w,l,light,c),PaletteVisibleProjection.Composer(snapshot,w,l,accentDark,accentLight))).ConfigureAwait(false);
        await MainActor.Run(()=>{if(attached&&generation==intent)apply(target);}).ConfigureAwait(false);
    }
    internal static (PaletteProviderRenderSelection[] Workers,PaletteProviderRenderSelection[] Leads) Selections(AppModel model,ChatThread thread)
    {
        PaletteProviderRenderSelection? Select(ProviderKind provider,string? id,string? effort)
        {var option=model.Providers.Model(id,provider)??(id is null?model.Providers.DefaultModel(provider):null);return (option?.Id??id) is {} resolved?new(provider.Id,resolved,option?.Name??resolved,effort??option?.DefaultEffort):null;}
        var workers=new List<PaletteProviderRenderSelection>();
        var heads=model.HydraIsOn(thread)?model.HydraHeads(thread.Id):ValueList<ChatThread>.Empty;
        foreach(var head in heads)if(Select(head.Provider,head.Model,head.Effort) is {} selection)workers.Add(selection);
        if(workers.Count==0&&model.HydraIsOn(thread)&&model.HydraLaunch(thread) is {} launch)
        {
            if(launch.HeadProfiles.Count==0){var p=launch.HeadsProvider;if(Select(p,launch.WorkerModel??(p==thread.Provider?thread.Model:null),launch.WorkerEffort??(p==thread.Provider?thread.Effort:null)) is {} s)workers.Add(s);}
            else foreach(var profile in launch.HeadProfiles){var p=profile.Provider??launch.HeadsProvider;if(Select(p,profile.Model??launch.WorkerModel??(p==thread.Provider?thread.Model:null),profile.Effort??launch.WorkerEffort??(p==thread.Provider?thread.Effort:null)) is {} s)workers.Add(s);}
        }
        return (workers.ToArray(),Select(thread.Provider,thread.Model,thread.Effort) is {} lead?[lead]:[]);
    }
}

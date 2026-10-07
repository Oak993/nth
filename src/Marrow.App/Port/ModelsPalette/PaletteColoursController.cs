// PORT-ONLY: OD-0127, locked palette-refresh option A; ED-NEW-models-palette-7.
using Marrow.Core.Port;
using Marrow.Services.Port.ModelsPalette;
using Marrow.App.Port.Settings;
namespace Marrow.App.Port.ModelsPalette;
internal sealed record PaletteDot(string Maker,string Name,string Colour);
internal sealed record PaletteColoursView(bool Running,string Detail,PaletteDot[] Dots,string[] Summary,int Completed,int Total);
internal sealed class PaletteColoursController : IDisposable
{
    private readonly PaletteEngine engine;
    private readonly Func<int> providerCount;
    private readonly TimeProvider time;
    private readonly Func<Task> collectCatalogues;
    private ModelShades? facade;
    private readonly ModelShades? sharedFacade;
    private long checkedTick;
    private bool attached,disposed,noChanges,offline,hasChecked;
    private Task? refreshing;
    public PaletteColoursView View {get;private set;}
    public event Action? Changed;
    public PaletteColoursController(PaletteEngine engine,Func<int> providerCount,TimeProvider? time=null,Func<Task>? collectCatalogues=null,ModelShades? sharedFacade=null)
    {
        this.engine=engine;this.sharedFacade=sharedFacade;this.providerCount=providerCount;this.time=time??TimeProvider.System;this.collectCatalogues=collectCatalogues??(()=>Task.CompletedTask);
        hasChecked=engine.LastCheckedAtServerSeconds>0;checkedTick=this.time.GetTimestamp();View=new(false,Rest(),Dots(),[],0,engine.Current.Models.Count);
    }
    internal PaletteDot? FindMaker(string name)=>engine.Current.Makers.Values.FirstOrDefault(m=>m.Name==name) is {} m?new(m.Id,m.Name,m.Heart):null;
    public void Attach(){if(attached||disposed)return;attached=true;facade=sharedFacade??new(engine,time:time);facade.Changed+=FacadeChanged;Tick();}
    public void Detach(){if(!attached)return;attached=false;if(facade is {} old){old.Changed-=FacadeChanged;if(sharedFacade is null)old.Dispose();}facade=null;View=View with {Summary=[]};Changed?.Invoke();}
    public void Tick(){if(!View.Running&&!offline){View=View with {Detail=Rest()};Changed?.Invoke();}}
    private string Rest()=>noChanges?"Checked just now · no new models":$"Checked {(hasChecked?UpdatesProjector.Age(time.GetElapsedTime(checkedTick)):"never")} · {engine.Current.Models.Count} models from {engine.Current.Makers.Count} makers";
    private PaletteDot[] Dots()=>engine.Current.Makers.Values.OrderBy(m=>m.Id switch {"anthropic"=>0,"openai"=>1,"google"=>2,"mistralai"=>3,_=>4}).ThenBy(m=>m.Id,StringComparer.Ordinal).Take(5).Select(m=>new PaletteDot(m.Id,m.Name,m.Heart)).ToArray();
    private string Running(int done,int total)=>$"Checking OpenRouter and {providerCount()} providers… {done} of {total} models";
    public Task Refresh()
    {
        if(refreshing is {IsCompleted:false})return refreshing;
        offline=false;View=View with {Running=true,Detail=Running(0,engine.Current.Models.Count),Summary=[],Completed=0,Total=engine.Current.Models.Count};Changed?.Invoke();
        return refreshing=Complete();
    }
    private async Task Complete()
    {
        await collectCatalogues().ConfigureAwait(false);
        var result=await engine.RefreshNow().ConfigureAwait(false);
        await MainActor.Run(()=>
        {
            if(disposed)return;
            offline=!result.CatalogueSucceeded;if(!offline){hasChecked=true;checkedTick=time.GetTimestamp();}
            var summary=new List<string>();
            if(!offline)
            {
                var coloured=result.NewModels.Count(id=>engine.Current.Models.TryGetValue(id,out var model)&&model.State is PaletteState.Ready or PaletteState.Known);
                if(coloured>0)summary.Add($"{coloured} new models coloured");
                foreach(var maker in result.ReSpacedModels.Where(engine.Current.Models.ContainsKey).Select(id=>engine.Current.Models[id].Maker).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                    summary.Add($"{engine.Current.Makers[maker].Name}'s shades re-spaced");
                foreach(var maker in result.NewMakers)if(engine.Current.Makers.TryGetValue(maker,out var m))summary.Add($"New maker: {m.Name}");
            }
            noChanges=!offline&&summary.Count==0&&result.NewModels.Count==0;
            View=View with {Running=false,Detail=offline?"Couldn't reach the model lists. Check your connection and try again.":Rest(),Dots=offline?View.Dots:Dots(),Summary=attached?summary.ToArray():[],Completed=0,Total=engine.Current.Models.Count};Changed?.Invoke();
        }).ConfigureAwait(false);
    }
    private void FacadeChanged()
    {
        if(!attached||disposed||offline)return;
        if(View.Running&&facade?.Progress is {} progress&&progress.Phase!=PaletteRefreshPhase.Done)
        {
            var total=progress.ModelsTotal>0?progress.ModelsTotal:View.Total;
            var completed=progress.ModelsTotal>0?progress.ModelsCompleted:View.Completed;
            View=View with {Detail=Running(completed,total),Completed=completed,Total=total};
        }
        View=View with {Dots=Dots()};Changed?.Invoke();
    }
    public void Dispose(){Detach();disposed=true;}
}

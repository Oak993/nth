// PORT-ONLY: RR-21 / AC-3 3A: the automatic pipeline brought forward, OD-0127 visible option A.
// ED-NEW-models-palette-7. Same source chain, boundary snapshot and daily seed authority.
namespace Marrow.Services.Port.ModelsPalette;
public sealed partial class PaletteEngine
{
    private long? lastRefreshTick;
    private PaletteCheckResult? lastRefresh;
    public Task<PaletteCheckResult> RefreshNow()=>Isolated(async()=>
    {
        if(running is {} joined)return await joined;
        if(lastRefresh is {} cached&&lastRefreshTick is {} tick&&time.GetElapsedTime(tick).TotalSeconds<120)return cached with {NewModels=Marrow.Core.Port.ValueList<string>.Empty,NewMakers=Marrow.Core.Port.ValueList<string>.Empty,ReSpacedModels=Marrow.Core.Port.ValueList<string>.Empty};
        lastRefreshTick=time.GetTimestamp();running=Run(true);
        try{return lastRefresh=await running;}finally{running=null;}
    });
}

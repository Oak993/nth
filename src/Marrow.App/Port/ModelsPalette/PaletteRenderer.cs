// PORT-ONLY: retained C25 renderer handoff; v22 all-model 8:5 arrangement, one caller-owned TrackEffect clock.
// ED-NEW-models-palette-11. This creates no frame source, effect, timer or startedAt value.
using System.Collections.Frozen;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
namespace Marrow.App.Port.ModelsPalette;
public sealed class PaletteRenderer
{
    private PaletteGlide? glide;
    public int MotionCount=>glide?.MotionCount??0;
    public IReadOnlyList<string> MotionKeys=>glide?.MotionKeys??Array.Empty<string>();
    public PaletteArrangement? Arrangement {get;private set;}
    // Offline baker/test convenience. Product callers use Engine.Compose, then the precomputed overload.
    public void Retarget(PaletteSnapshot snapshot,IReadOnlyList<PaletteRenderSelection> workers,IReadOnlyList<PaletteRenderSelection> leads,bool light,double effectTime,bool arrivalRunning=false,bool reduceMotion=false)=>Retarget(PaletteComposition.Create(snapshot,workers,leads,light),effectTime,arrivalRunning,reduceMotion);
    public void Retarget(PaletteComposite? target,double effectTime,bool arrivalRunning=false,bool reduceMotion=false)
    {
        if(target is null)return;Arrangement=target.Arrangement;
        if(glide is null)glide=new(target.Shade);else glide.Retarget(target.Shade,reduceMotion?effectTime-1.2:effectTime,arrivalRunning&&!reduceMotion);
    }
    public void ArrivalFinished(double effectTime)=>glide?.ArrivalFinished(effectTime);
    public PaletteGlideFrame Sample(double effectTime,Span<byte> row,Span<double> motion)=>glide?.Sample(effectTime,row,motion)??default;
}

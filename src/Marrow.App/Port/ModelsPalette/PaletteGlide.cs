// PORT-ONLY: AC-3 C25, PLAN 4.6, same-effect handoff for Strike; ED-NEW-models-palette-6.
// The carrier owns no effect or startedAt. Sampling interpolates precomputed RGB rows and every dial with one t.
using Marrow.Services.Port.ModelsPalette;
namespace Marrow.App.Port.ModelsPalette;
public readonly record struct PaletteGlideFrame(double Progress,uint Title,uint Glow);
public sealed class PaletteGlide
{
    private readonly double[] fromRow=new double[1024],toRow=new double[1024];
    private readonly double[] fromTitle=new double[3],toTitle=new double[3],fromGlow=new double[3],toGlow=new double[3];
    private double[] fromMotion,toMotion;
    private double began;
    private double colourSeconds=1.2,motionSeconds=1.2;
    private bool active,canRebase,previousActive;
    private double previousBegan,previousColourSeconds,previousMotionSeconds;
    private readonly double[] previousFromRow=new double[1024],previousToRow=new double[1024];
    private readonly double[] previousFromTitle=new double[3],previousToTitle=new double[3],previousFromGlow=new double[3],previousToGlow=new double[3];
    private double[] previousFromMotion=[],previousToMotion=[];
    private PaletteShade? waiting;
    private PaletteShade targetShade;
    public IReadOnlyList<string> MotionKeys {get;private set;}
    public int MotionCount=>MotionKeys.Count;
    public PaletteGlide(PaletteShade initial)
    {
        targetShade=initial;
        MotionKeys=Array.AsReadOnly(initial.Motion.Keys.Order(StringComparer.Ordinal).ToArray());
        fromMotion=new double[MotionCount];toMotion=new double[MotionCount];Load(initial);
        Array.Copy(toRow,fromRow,1024);Array.Copy(toTitle,fromTitle,3);Array.Copy(toGlow,fromGlow,3);Array.Copy(toMotion,fromMotion,MotionCount);
    }
    public void Retarget(PaletteShade target,double now,bool arrivalRunning=false,double colourSeconds=1.2,double motionSeconds=1.2)
    {
        if(arrivalRunning){waiting=target;return;}
        waiting=null;
        if(targetShade.Equals(target)){canRebase=false;return;}targetShade=target;
        previousActive=active;previousBegan=began;previousColourSeconds=this.colourSeconds;previousMotionSeconds=this.motionSeconds;
        Array.Copy(fromRow,previousFromRow,1024);Array.Copy(toRow,previousToRow,1024);Array.Copy(fromTitle,previousFromTitle,3);Array.Copy(toTitle,previousToTitle,3);Array.Copy(fromGlow,previousFromGlow,3);Array.Copy(toGlow,previousToGlow,3);
        var previousDials=MotionKeys.Select((key,i)=>(key,From:fromMotion[i],To:toMotion[i])).ToDictionary(x=>x.key,StringComparer.Ordinal);
        var t=Progress(now);BlendInto(fromRow,toRow,t);BlendInto(fromTitle,toTitle,t);BlendInto(fromGlow,toGlow,t);BlendInto(fromMotion,toMotion,MotionProgress(now));
        var keys=MotionKeys.Union(target.Motion.Keys,StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if(keys.Length!=MotionCount)
        {
            var displayed=MotionKeys.Select((key,index)=>(key,fromMotion[index])).ToDictionary(x=>x.key,x=>x.Item2,StringComparer.Ordinal);
            fromMotion=keys.Select(key=>displayed.GetValueOrDefault(key)).ToArray();toMotion=new double[keys.Length];MotionKeys=Array.AsReadOnly(keys);
        }
        previousFromMotion=MotionKeys.Select(key=>previousDials.TryGetValue(key,out var d)?d.From:0).ToArray();previousToMotion=MotionKeys.Select(key=>previousDials.TryGetValue(key,out var d)?d.To:0).ToArray();
        Load(target);began=now;active=true;canRebase=true;this.colourSeconds=colourSeconds;this.motionSeconds=motionSeconds;
    }
    public void ArrivalFinished(double now){if(waiting is {} target){waiting=null;Retarget(target,now);}}
    private double Progress(double now)=>active?Math.Clamp((now-began)/colourSeconds,0,1):1;
    public void StartAt(double now)
    {
        if(!active||!canRebase)return;canRebase=false;
        var t=previousActive?Math.Clamp((now-previousBegan)/previousColourSeconds,0,1):1;
        var mt=previousActive?Math.Clamp((now-previousBegan)/previousMotionSeconds,0,1):1;
        CopyBlend(previousFromRow,previousToRow,t,fromRow);CopyBlend(previousFromTitle,previousToTitle,t,fromTitle);CopyBlend(previousFromGlow,previousToGlow,t,fromGlow);CopyBlend(previousFromMotion,previousToMotion,mt,fromMotion);began=now;
    }
    private static void CopyBlend(double[] a,double[] b,double t,double[] into){for(var i=0;i<into.Length;i++)into[i]=a[i]+(b[i]-a[i])*t;}
    public double MotionProgress(double now)=>active?Math.Clamp((now-began)/motionSeconds,0,1):1;
    public PaletteGlideFrame Sample(double now,Span<byte> row,Span<double> motion)
    {
        if(row.Length<1024||motion.Length<MotionCount)throw new ArgumentException("The target buffers must fit the precomputed tables.");
        var t=Progress(now);
        for(var i=0;i<1024;i++)row[i]=(byte)Math.Round(fromRow[i]+(toRow[i]-fromRow[i])*t);
        var mt=MotionProgress(now);for(var i=0;i<MotionCount;i++)motion[i]=fromMotion[i]+(toMotion[i]-fromMotion[i])*mt;
        return new(t,Colour(fromTitle,toTitle,t),Colour(fromGlow,toGlow,t));
    }
    private static uint Colour(double[] a,double[] b,double t)=>
        0xFF000000u|((uint)Math.Round(a[0]+(b[0]-a[0])*t)<<16)|((uint)Math.Round(a[1]+(b[1]-a[1])*t)<<8)|(uint)Math.Round(a[2]+(b[2]-a[2])*t);
    private static void BlendInto(double[] a,double[] b,double t){for(var i=0;i<a.Length;i++)a[i]+=(b[i]-a[i])*t;}
    private void Load(PaletteShade target)
    {
        if(target.GradientRow.Count!=1024)throw new ArgumentException("Expected one 256-pixel RGBA gradient row.");
        for(var i=0;i<1024;i++)toRow[i]=target.GradientRow[i];
        Convert.FromHexString(target.Title[1..]).Select(x=>(double)x).ToArray().CopyTo(toTitle,0);
        Convert.FromHexString(target.Glow[1..]).Select(x=>(double)x).ToArray().CopyTo(toGlow,0);
        for(var i=0;i<MotionCount;i++)toMotion[i]=target.Motion.GetValueOrDefault(MotionKeys[i]);
    }
}

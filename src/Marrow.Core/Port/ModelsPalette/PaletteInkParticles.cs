// PORT-ONLY: v22 paintInk :399-427, fixed Float32 tracer block, source Math.random seam.
namespace Marrow.Core.Port.ModelsPalette;
public readonly record struct PaletteInkParticle(float X,float Y,float Life,float Radius);
public sealed class PaletteInkParticles
{
    private readonly float[] data;private readonly double[] alpha;private readonly Func<double> random;private readonly int rate;private double spawn;
    public int Count {get;private set;}
    public PaletteInkParticles(bool lite,Func<double>? random=null){data=new float[(lite?260:700)*4];alpha=new double[data.Length/4];rate=lite?3:7;this.random=random??Random.Shared.NextDouble;}
    public double Alpha(int i)=>alpha[i];
    public PaletteInkParticle At(int i)=>new(data[i*4],data[i*4+1],data[i*4+2],data[i*4+3]);
    public static double Visibility(double density){var a=1-Math.Exp(-1.6*Math.Max(0,density));return Math.Min(1,a/.2)*(1-Math.Min(1,Math.Max(0,(a-.55)/.35)))*.8;}
    public void Advance(PaletteInk fluid,double dt,bool settled)
    {
        spawn+=dt*60*rate;if(settled)spawn=0;
        for(;spawn>=1;spawn--)
        {
            if(Count*4==data.Length){Array.Copy(data,4,data,0,data.Length-4);Count--;}
            var o=Count++*4;data[o]=(float)(fluid.NX-1-random()*4);data[o+1]=(float)(1+random()*fluid.NY);data[o+2]=(float)(2.5+random()*3);data[o+3]=(float)(.5+random()*1.1);
        }
        var keep=0;
        for(var q=0;q<Count;q++)
        {
            var o=q*4;var v=fluid.Velocity(data[o],data[o+1]);var x=data[o]+v.U*dt;var y=data[o+1]+v.V*dt;var life=data[o+2]-dt;
            if(life>0&&x>.5){var k=keep++*4;data[k]=(float)x;data[k+1]=(float)y;data[k+2]=(float)life;data[k+3]=data[o+3];alpha[keep-1]=Visibility(v.D);}
        }
        Count=keep;
    }
}

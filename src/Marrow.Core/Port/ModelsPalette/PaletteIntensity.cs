// PORT-ONLY: draft IR-3, exact reference arithmetic and dials; ED-NEW-models-palette-6.
// UI application remains gated by AC-3 PLAN 9.2 (W1/W2), independently of engine execution.
using System.Collections.Concurrent;
using System.Numerics;
using System.Collections.Frozen;
namespace Marrow.Core.Port.ModelsPalette;

public sealed record PaletteLevelScale(int Count,FrozenDictionary<int,ValueList<double>> Positions);
public static class PaletteIntensity
{
    public const string Version="IR-3";
    private static readonly ConcurrentDictionary<ValueList<string>,PaletteLevelScale> scales=new();
    public static string? Word(string? word)=>word?.Trim().ToLowerInvariant() switch {"extra-high" or "extra_high" or "extra high"=>"xhigh","ultra"=>"max",var x=>x};
    public static double Effort(string? word)=>Word(word) switch {null=>1,"off" or "none"=>0,"minimal"=>1d/6,"low"=>2d/6,"medium"=>3d/6,"high"=>4d/6,"xhigh"=>5d/6,"max"=>1,_=>.5};
    public static ValueList<string> Stops(PaletteAnchor palette,double s,double e)
    {
        if(!double.IsFinite(s)||!double.IsFinite(e)||s<0||s>1||e<0||e>1)throw new ArgumentOutOfRangeException(nameof(s));
        var u=(s-.5)*2;var v=1-e;var b=palette.Stops.Select(PaletteMath.Lab).ToArray();
        var lh=b[1].L;var ch=b[1].C;
        if(ch<.03)
        {
            var p=lh>.5?1:-1;var pole=p>0?1d:0;var d=Math.Abs(pole-lh);
            var shift=(u>=0?-.08*u:.05*-u)+.035*v;
            var dh=shift>=0?d+shift:d*Math.Exp(shift/d);var lh2=pole-p*dh;
            var spread=(1+.15*u)*(1-.75*v);
            return new(b.Select(x=>PaletteMath.Fit(Math.Clamp(lh2+(x.L-lh)*spread,0,1),x.C*(1-.60*v)*(1+.25*u),x.Hue).Hex));
        }
        var lam=Math.Clamp((lh-.25)/.30,.55,1);
        double[] ls=new double[3],cs=new double[3],deep=[.085,.065,.035],thin=[.12,.095,.06],calm=[.05,.045,.03];
        for(var i=0;i<3;i++)
        {
            var dl=u>=0?-deep[i]*u:thin[i]*-u*lam;
            var kc=u>=0?1+.12*u:1-.12*-u;
            kc*=1-.40*v; dl+=calm[i]*v*lam;kc=Math.Max(.55,kc);
            if(dl<0)kc=Math.Max(kc,kc+(.75-kc)*Math.Min(1,-dl/.01));
            ls[i]=b[i].L+dl;cs[i]=b[i].C*kc;
        }
        var sigma=(1-.55*v)*(1+.15*u);
        var span=b.SelectMany(x=>b.Select(y=>Math.Abs(((x.Hue-y.Hue+540)%360)-180))).Max();
        if(span>60)sigma=Math.Max(sigma,.75+.25*(1-v));
        foreach(var i in new[]{0,2})ls[i]=ls[1]+(ls[i]-ls[1])*sigma;
        var result=new string[3];
        for(var i=0;i<3;i++)
        {
            var x=b[i];var ln=Math.Clamp(ls[i],.04,x.L<.94?.94:x.L);var cn=cs[i];
            if(ln>x.L&&cn>x.C)cn=x.C+(cn-x.C)*Math.Max(0,1-(ln-x.L)/.01);
            var fitted=PaletteMath.Fit(ln,cn,x.Hue);var target=.85*Math.Min(cn,x.C);
            if(ln<x.L&&fitted.Chroma<target)
            {
                double low=ln,high=Math.Min(x.L,.94);
                for(var j=0;j<30;j++){var mid=(low+high)/2;if(Math.Min(cn,PaletteMath.MaximumChroma(mid,x.Hue))>=target)high=mid;else low=mid;}
                fitted=PaletteMath.Fit(high,cn,x.Hue);
            }
            result[i]=fitted.Hex;
        }
        return new(result);
    }
    public static string? Title(PaletteAnchor palette,IReadOnlyList<string> shaded)
    {
        if(palette.Key=="Silver")return null;
        var h0=PaletteMath.Lab(palette.Stops[1]);var h1=PaletteMath.Lab(shaded[1]);
        if(palette.Title is null)return PaletteMath.Fit(h1.L+.22*(1-h1.L),h1.C*.9,h1.Hue).Hex;
        var t=PaletteMath.Lab(palette.Title);var lab=new PaletteLab(t.L+h1.L-h0.L,t.A+h1.A-h0.A,t.B+h1.B-h0.B);
        return PaletteMath.Fit(lab.L,lab.C,lab.Hue).Hex;
    }
    public static FrozenDictionary<string,double> Motion(string key,double s,double e)
    {
        var u=(s-.5)*2;var light=1+.12*u;
        double Geo(double a,double b)=>a*Math.Pow(b/a,e);double Lin(double a,double b)=>a+(b-a)*e;
        var d=new Dictionary<string,double>(StringComparer.Ordinal)
        {
            ["current_fast_period_s"]=Geo(12,7),["current_fast_opacity"]=Lin(.30,.55),["current_fast_amplitude"]=Lin(18,30),
            ["current_slow_period_s"]=Geo(16,11),["current_slow_opacity"]=Lin(.22,.35),["current_slow_amplitude"]=7,
            ["caustic_period_s"]=Geo(9,6),["caustic_alpha"]=Math.Min(.92,Lin(.50,.80)*light),
            ["bubble_px_per_lane"]=Geo(64,32),["bubble_rise_scale"]=Lin(1.5,1),["bubble_pause_max_s"]=Lin(2.4,1.4),["bubble_large_share"]=Lin(.04,.10),
            ["breathe_period_s"]=Geo(6.4,4.8),["breathe_saturate_amp"]=Lin(.05,.12)*(1+.25*u),["breathe_bright_amp"]=Lin(.03,.06),
            ["glow_alpha"]=Math.Min(.70,.6*Lin(.60,1)*(1+.15*u)),["glow_blur"]=Lin(4,8)
        };
        if(key=="Grok"){d["oil_sheen_period_s"]=Geo(9,6.5);d["oil_sheen_alpha"]=Math.Min(1,.9*Lin(.6,1)*(1+.15*u));}
        if(key=="Silver")d["specular_scale"]=Math.Min(1.15,(.75+.25*e)*(1+.15*u));
        return d.ToFrozenDictionary(x=>x.Key,x=>ReferenceRound(x.Value,3),StringComparer.Ordinal);
    }
    // Python round(x, n) rounds the binary value against decimal midpoints; Math.Round's scaled
    // intermediate can erase which side of a midpoint it occupied (e.g. 0.63749999999999996).
    private static double ReferenceRound(double x,int digits)
    {
        var bits=BitConverter.DoubleToUInt64Bits(x);var negative=(bits>>63)!=0;
        var exponent=(int)((bits>>52)&2047)-1023-52;
        var numerator=new BigInteger((bits&0xFFFFFFFFFFFFFUL)|0x10000000000000UL)*BigInteger.Pow(10,digits);
        BigInteger rounded;
        if(exponent>=0)rounded=numerator<<exponent;
        else
        {
            var denominator=BigInteger.One<<-exponent;
            rounded=BigInteger.DivRem(numerator,denominator,out var remainder);
            var cmp=(remainder*2).CompareTo(denominator);
            if(cmp>0||(cmp==0&&!rounded.IsEven))rounded++;
        }
        return (negative?-1:1)*(double)rounded/Math.Pow(10,digits);
    }
    public static PaletteLevelScale Levels(PaletteAnchor palette)=>scales.GetOrAdd(palette.Stops,_=>ComputeLevels(palette));
    private static PaletteLevelScale ComputeLevels(PaletteAnchor palette)
    {
        const int samples=200;var shades=Enumerable.Range(0,samples+1).Select(j=>Stops(palette,j/(double)samples,1)).ToArray();
        var cum=new double[samples+1];for(var i=1;i<=samples;i++)cum[i]=cum[i-1]+PaletteMath.Distance(shades[i-1],shades[i]);
        double At(double p)
        {
            var arc=cum[^1]*p;if(arc<=0)return 0;if(arc>=cum[^1])return 1;
            int low=0,high=samples;while(high-low>1){var mid=(low+high)/2;if(cum[mid]<=arc)low=mid;else high=mid;}
            var span=cum[low+1]-cum[low];return (low+(span==0?0:(arc-cum[low])/span))/samples;
        }
        var positions=new Dictionary<int,ValueList<double>>{[1]=new([.5])};var count=1;
        for(var n=2;n<=16;n++)
        {
            var basis=Math.Min(1,(n-1)/3d);double[]? found=null;
            for(var k=0;k<=8;k++)
            {
                var w=basis+(1-basis)*k/8;
                var ss=Enumerable.Range(0,n).Select(i=>Math.Round(At(.5+(1-i/(double)(n-1)-.5)*w),4,MidpointRounding.ToEven)).ToArray();
                var st=ss.Select(s=>Stops(palette,s,1)).ToArray();
                if(Enumerable.Range(1,n-1).Min(i=>PaletteMath.Distance(st[i-1],st[i]))>=.02){found=ss;break;}
            }
            if(found is null)break;positions[n]=new(found);count=n;
        }
        return new(count,positions.ToFrozenDictionary());
    }
}

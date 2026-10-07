// PORT-ONLY: OKLab math copied from the mirrored IR-3 executable; ED-617.
using System.Globalization;
namespace Marrow.Core.Port.ModelsPalette;

public readonly record struct PaletteLab(double L,double A,double B)
{
    public double C=>Math.Sqrt(A*A+B*B);
    public double Hue=>(Math.Atan2(B,A)*180/Math.PI+360)%360;
    public static PaletteLab Lch(double l,double c,double h)=>new(l,c*Math.Cos(h*Math.PI/180),c*Math.Sin(h*Math.PI/180));
}
public static class PaletteMath
{
    public static PaletteLab Lab(string hex)
    {
        var rgb=Enumerable.Range(0,3).Select(i=>int.Parse(hex.AsSpan(1+i*2,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture)/255d)
            .Select(c=>c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4)).ToArray();
        var l=Math.Cbrt(.4122214708*rgb[0]+.5363325363*rgb[1]+.0514459929*rgb[2]);
        var m=Math.Cbrt(.2119034982*rgb[0]+.6806995451*rgb[1]+.1073969566*rgb[2]);
        var s=Math.Cbrt(.0883024619*rgb[0]+.2817188376*rgb[1]+.6299787005*rgb[2]);
        return new(.2104542553*l+.7936177850*m-.0040720468*s,1.9779984951*l-2.4285922050*m+.4505937099*s,.0259040371*l+.7827717662*m-.8086757660*s);
    }
    private static double[] Linear(PaletteLab x)
    {
        var l=Math.Pow(x.L+.3963377774*x.A+.2158037573*x.B,3);
        var m=Math.Pow(x.L-.1055613458*x.A-.0638541728*x.B,3);
        var s=Math.Pow(x.L-.0894841775*x.A-1.2914855480*x.B,3);
        return [4.0767416621*l-3.3077115913*m+.2309699292*s,-1.2684380046*l+2.6097574011*m-.3413193965*s,-.0041960863*l-.7034186147*m+1.7076147010*s];
    }
    public static bool InGamut(double l,double c,double h)=>Linear(PaletteLab.Lch(l,c,h)).All(v=>v>=-.0001&&v<=1.0001);
    public static double MaximumChroma(double l,double h)
    {
        double low=0,high=.45;
        for(var i=0;i<32;i++){var mid=(low+high)/2;if(InGamut(l,mid,h))low=mid;else high=mid;}
        return low;
    }
    public static (string Hex,double Chroma) Fit(double l,double c,double h)
    {
        l=Math.Clamp(l,0,1); if(!InGamut(l,c,h))c=MaximumChroma(l,h);
        var rgb=Linear(PaletteLab.Lch(l,c,h)).Select(v=>Math.Clamp(v,0,1)).Select(v=>v<=.0031308?12.92*v:1.055*Math.Pow(v,1/2.4)-.055)
            .Select(v=>(int)Math.Round(255*Math.Clamp(v,0,1),MidpointRounding.ToEven));
        return ("#"+string.Concat(rgb.Select(v=>v.ToString("X2",CultureInfo.InvariantCulture))),c);
    }
    public static double Distance(PaletteLab a,PaletteLab b)=>Math.Sqrt(Math.Pow(a.L-b.L,2)+Math.Pow(a.A-b.A,2)+Math.Pow(a.B-b.B,2));
    public static double Distance(IReadOnlyList<string> a,IReadOnlyList<string> b)=>a.Zip(b,(x,y)=>Distance(Lab(x),Lab(y))).Average();
    public static string Mix(string a,string b,double t)
    {
        var x=Lab(a);var y=Lab(b);var z=new PaletteLab(x.L+(y.L-x.L)*t,x.A+(y.A-x.A)*t,x.B+(y.B-x.B)*t);
        return Fit(z.L,z.C,z.Hue).Hex;
    }
}

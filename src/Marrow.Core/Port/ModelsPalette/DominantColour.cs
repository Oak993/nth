// PORT-ONLY: S2 dominant extraction and area-average evidence resample, AC-3 PLAN 4.2/5.5;
// colours.py dominant_img and hardening/h5_dominant_edges.py; ED-617.
namespace Marrow.Core.Port.ModelsPalette;

public sealed record ColourSeed(bool Mono,int Lightness,int Chroma,int Hue);
public sealed record LogoPixels(int Width,int Height,byte[] Rgba);
public sealed record DominantResult(bool Decisive,ColourSeed? Seed,ValueList<byte> Rgba64);
public interface ILogoDecoder { LogoPixels? Decode(byte[] bytes,string contentType); }
public static class DominantColour
{
    public const string Version="S2";
    public static DominantResult Extract(LogoPixels image)
    {
        var w=image.Width;var h=image.Height;
        if(w<32||h<32||w>1024||h>1024||image.Rgba.Length!=w*h*4)return new(false,null,ValueList<byte>.Empty);
        var pixels=new byte[64*64*4];
        for(var dy=0;dy<64;dy++)for(var dx=0;dx<64;dx++)
        {
            long[] sums=new long[4];
            var x0=dx*w;var x1=(dx+1)*w;var y0=dy*h;var y1=(dy+1)*h;
            for(var sy=y0/64;sy<(y1+63)/64;sy++)for(var sx=x0/64;sx<(x1+63)/64;sx++)
            {
                var weight=(long)(Math.Min(x1,(sx+1)*64)-Math.Max(x0,sx*64))*(Math.Min(y1,(sy+1)*64)-Math.Max(y0,sy*64));
                for(var c=0;c<4;c++)sums[c]+=image.Rgba[(sy*w+sx)*4+c]*weight;
            }
            for(var c=0;c<4;c++)pixels[(dy*64+dx)*4+c]=(byte)Math.Round(sums[c]/(double)(w*h),MidpointRounding.ToEven);
        }
        var opaque=new List<PaletteLab>();
        for(var i=0;i<pixels.Length;i+=4)if(pixels[i+3]>=160)
            opaque.Add(PaletteMath.Lab("#"+Convert.ToHexString(pixels.AsSpan(i,3))));
        var evidence=new ValueList<byte>(pixels);
        if(opaque.Count<.02*4096)return new(false,null,evidence);
        var colour=opaque.Where(x=>x.C>=.06&&x.L>.25&&x.L<.95).ToArray();
        if(colour.Length<.08*opaque.Count)return new(true,new(true,0,0,0),evidence);
        var bins=new double[24];foreach(var x in colour)bins[(int)(x.Hue/15)%24]+=x.C;
        var best=Enumerable.Range(0,24).OrderByDescending(i=>.5*bins[(i+23)%24]+bins[i]+.5*bins[(i+1)%24]).First();
        var members=colour.Where(x=>Math.Min(Math.Abs(x.Hue-(best*15+7.5)),360-Math.Abs(x.Hue-(best*15+7.5)))<=22.5).ToArray();
        var sxh=members.Sum(x=>x.C*Math.Cos(x.Hue*Math.PI/180));var syh=members.Sum(x=>x.C*Math.Sin(x.Hue*Math.PI/180));
        var hue=(Math.Atan2(syh,sxh)*180/Math.PI+360)%360;
        var ls=members.Select(x=>x.L).Order().ToArray();var cs=members.Select(x=>x.C).Order().ToArray();
        return new(true,new(false,(int)Math.Round(ls[ls.Length/2]/.005),(int)Math.Round(cs[(int)(cs.Length*.75)]/.005),(int)Math.Round(hue)%360),evidence);
    }
}

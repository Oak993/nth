// PORT-ONLY: AC-3 D3 nearest-free palette search and frozen reserved set; ED-NEW-models-palette-6.
// Identity-cloud is the production rule until W1. IR-3 shade-cloud validation can be supplied explicitly.
namespace Marrow.Core.Port.ModelsPalette;

public sealed record DerivedPalette(PaletteAnchor Palette,bool Crowded);
public static class PaletteDeriver
{
    private static readonly (double H,double L0,double L1,double C0,double C1)[] Bands=[(5,.50,.70,.13,.21),(35,.52,.70,.12,.20),(62.5,.62,.78,.12,.18),(95,.72,.80,.12,.17),(130,.58,.76,.12,.18),(195,.55,.75,.09,.15),(265,.45,.68,.12,.22),(317.5,.50,.70,.12,.22)];
    private static (double L0,double L1,double C0,double C1) Band(double h)
    {
        for(var i=0;i<Bands.Length;i++)
        {
            var a=Bands[i];var b=Bands[(i+1)%Bands.Length];var end=b.H+(i==Bands.Length-1?360:0);var at=h<a.H?h+360:h;
            if(at<a.H||at>end)continue;var t=(at-a.H)/(end-a.H);
            return (a.L0+(b.L0-a.L0)*t,a.L1+(b.L1-a.L1)*t,a.C0+(b.C0-a.C0)*t,a.C1+(b.C1-a.C1)*t);
        }
        throw new InvalidOperationException("Hue has no band.");
    }
    private sealed record Candidate(PaletteAnchor Palette,long Cost,int Dh,int LIndex,double Chroma,double Clearance);
    public static DerivedPalette Derive(ColourSeed seed,IReadOnlyList<PaletteAnchor> frozenReserved,Func<PaletteAnchor,IReadOnlyList<PaletteLab>>? cloud=null)
    {
        var hue=seed.Mono?250:seed.Hue;var l=seed.Mono?.56:seed.Lightness*.005;var c=seed.Mono?.045:seed.Chroma*.005;
        var target=seed.Mono?(.42,.68,.05,.07):Band(hue);
        l=Math.Clamp(l,target.Item1,target.Item2);if(!seed.Mono)c=Math.Clamp(c,target.Item3,target.Item4);
        var reserved=frozenReserved.SelectMany(p=>p.Key is "Antigravity" or "Copilot"?p.Stops: new ValueList<string>([p.Stops[1]])).Select(PaletteMath.Lab).ToArray();
        var candidates=new List<Candidate>();
        for(var dh=-180;dh<=180;dh+=3)
        {
            var h=(hue+dh+360)%360;var band=seed.Mono?(.42,.68,.05,.07):Band(h);
            for(var li=(int)Math.Ceiling(band.Item1/.02);li<=(int)Math.Floor(band.Item2/.02);li++)
            {
                var lc=li*.02;var chromas=seed.Mono?new[]{.05,.06,.07}:new[]{c,c*.8,c*1.2}.Select(x=>Math.Clamp(x,band.Item3,band.Item4)).ToArray();
                for(var ci=0;ci<chromas.Length;ci++)
                {
                    var cc=chromas[ci];var heart=PaletteMath.Fit(lc,cc,h);if(cc-heart.Chroma>.02)continue;
                    var stops=new ValueList<string>([PaletteMath.Fit(lc-.07,cc*1.02,h).Hex,heart.Hex,PaletteMath.Fit(Math.Min(.88,lc+.10),cc*.88,h).Hex]);
                    var palette=new PaletteAnchor("Derived",stops,null);var lab=PaletteMath.Lab(heart.Hex);
                    var clearance=reserved.Select(r=>PaletteMath.Distance(lab,r)).DefaultIfEmpty(1).Min();
                    var cost=(seed.Mono?Math.Abs(dh)/360d:Math.Abs(dh)/30d)+Math.Abs(lc-l)/.08+Math.Abs(cc-c)/.06;
                    candidates.Add(new(palette,(long)Math.Round(cost*1e9),dh,li,cc,clearance));
                }
            }
        }
        var ordered=candidates.OrderBy(x=>x.Cost).ThenBy(x=>Math.Abs(x.Dh)).ThenByDescending(x=>x.Dh>0).ThenBy(x=>x.LIndex).ThenBy(x=>x.Chroma);
        var reservedClouds=cloud is null?[]:frozenReserved.Select(cloud).ToArray();
        foreach(var candidate in ordered.Where(x=>x.Clearance>=.060))
        {
            if(cloud is not null)
            {
                var shadeCloud=cloud(candidate.Palette);
                if(reservedClouds.Any(rc=>rc.Zip(shadeCloud,(a,b)=>PaletteMath.Distance(a,b)).Any(distance=>distance<.035)))continue;
            }
            return new(candidate.Palette,false);
        }
        var best=candidates.OrderByDescending(x=>x.Clearance).ThenBy(x=>x.Cost).ThenBy(x=>Math.Abs(x.Dh)).ThenByDescending(x=>x.Dh>0).ThenBy(x=>x.LIndex).ThenBy(x=>x.Chroma).First();
        return new(best.Palette,true);
    }
}

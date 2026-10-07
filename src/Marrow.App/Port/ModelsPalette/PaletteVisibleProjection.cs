// PORT-ONLY: OD-0089 real ComposerLights projection, C20 all-model title/glow; ED-NEW-models-palette-17.
using Avalonia.Media;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
namespace Marrow.App.Port.ModelsPalette;
public readonly record struct PaletteColourSpan(Color Color,double Start,double End);
public static class PaletteVisibleProjection
{
    public static PaletteColourSpan[] Spans(PaletteSnapshot snapshot,IReadOnlyList<PaletteRenderSelection> workers,IReadOnlyList<PaletteRenderSelection> leads,bool light,bool glow)
    {
        var chosen=workers.Reverse().Select(x=>(x,Weight:5)).Concat(leads.Reverse().Select(x=>(x,Weight:8))).Select(x=>(Shade:snapshot.Resolve(x.x.Id,x.x.Effort,light),x.Weight)).Where(x=>x.Shade is not null).ToArray();
        var total=chosen.Sum(x=>x.Weight);double at=0;
        return chosen.Select(x=>{var from=at/total;at+=x.Weight;return new PaletteColourSpan(Color.Parse(glow?x.Shade!.Glow:x.Shade!.Title),from,at/total);}).ToArray();
    }
    public static ComposerLightPalette Composer(PaletteSnapshot snapshot,IReadOnlyList<PaletteRenderSelection> workers,IReadOnlyList<PaletteRenderSelection> leads,string accentDark,string accentLight)
    {
        var dark=PaletteComposition.Create(snapshot,workers,leads,false)?.Shade;
        var light=PaletteComposition.Create(snapshot,workers,leads,true)?.Shade;
        return dark is null||light is null?ComposerLightPalette.Default with {AccentDark=accentDark,AccentLight=accentLight}:new(dark.Stops[1],dark.Stops[2],light.Stops[0],light.Stops[1],accentDark,accentLight);
    }
    public static LinearGradientBrush Brush(IReadOnlyList<PaletteColourSpan> spans)
    {
        var brush=new LinearGradientBrush{StartPoint=new(0,.5,Avalonia.RelativeUnit.Relative),EndPoint=new(1,.5,Avalonia.RelativeUnit.Relative)};
        for(var i=0;i<spans.Count;i++)
        {
            var span=spans[i];var third=(span.End-span.Start)/3;
            if(i==0)brush.GradientStops.Add(new(span.Color,0));
            brush.GradientStops.Add(new(span.Color,span.Start+third));brush.GradientStops.Add(new(span.Color,span.End-third));
            if(i==spans.Count-1)brush.GradientStops.Add(new(span.Color,1));
        }
        return brush;
    }
}

// PORT-ONLY: locked v22 blend function :1161-1195; ED-617.
// Unlike the Round-1 geometry helper, the source explicitly reverses BOTH lists before drawing.
namespace Marrow.Core.Port.ModelsPalette;
public sealed record PaletteSelection(string Id,bool IsLead,ValueList<string> ShadeStops);
public readonly record struct PaletteRenderSelection(string Id,string? Effort);
public readonly record struct PaletteGradientStop(double Position,string Color);
public sealed record PaletteArrangement(ValueList<SelectedModelSpan> Spans,ValueList<PaletteGradientStop> Stops)
{
    public static PaletteArrangement Create(IReadOnlyList<PaletteSelection> selection)
    {
        if(selection.Count==0)return new(ValueList<SelectedModelSpan>.Empty,ValueList<PaletteGradientStop>.Empty);
        var ordered=selection.Where(x=>!x.IsLead).Reverse().Concat(selection.Where(x=>x.IsLead).Reverse()).ToArray();
        var total=ordered.Sum(x=>x.IsLead?8:5);var edge=0;var spans=new List<SelectedModelSpan>();var stops=new List<PaletteGradientStop>();
        for(var i=0;i<ordered.Length;i++)
        {
            var x=ordered[i];var from=edge/(double)total;edge+=x.IsLead?8:5;var to=edge/(double)total;var w=to-from;var heart=x.ShadeStops[1];
            spans.Add(new(x.Id,x.IsLead,from,to));if(i==0)stops.Add(new(0,heart));
            // v22 serializes each percentage with toFixed(1) before the CSS gradient consumes it.
            stops.Add(new(Math.Round(100*(from+w/3),1,MidpointRounding.AwayFromZero)/100,heart));
            stops.Add(new(Math.Round(100*(to-w/3),1,MidpointRounding.AwayFromZero)/100,heart));
            if(i==ordered.Length-1)stops.Add(new(1,heart));
        }
        return new(new(spans),new(stops));
    }
    public string Sample(double fraction)
    {
        if(Stops.Count==0)throw new InvalidOperationException("An empty selection has no gradient.");
        var t=Math.Clamp(fraction,0,1);var i=1;while(i<Stops.Count-1&&Stops[i].Position<t)i++;
        var a=Stops[i-1];var b=Stops[i];return a.Color==b.Color?a.Color:PaletteMath.Mix(a.Color,b.Color,(t-a.Position)/(b.Position-a.Position));
    }
}

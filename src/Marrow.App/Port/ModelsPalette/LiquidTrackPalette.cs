// PORT-ONLY: additive palette shader variants; the existing unbound programs/uniforms remain byte-identical.
// C25 IR-3 dials and v22 all-model strands. ED-NEW-models-palette-14.
using System.Collections.Concurrent;
using Marrow.App.Port.ModelsPalette;
using SkiaSharp;
namespace Marrow.App.Views.Composer;
public static partial class LiquidTrack
{
    private static readonly ConcurrentDictionary<(Lazy<SKRuntimeEffect>,int),Lazy<SKRuntimeEffect>> paletteEffects=new();
    private static SKRuntimeEffect PaletteEffect(Lazy<SKRuntimeEffect> original,in Frame frame)
    {
        if(frame.Palette is null||ReferenceEquals(original,BubbleEffect))return original.Value;
        var count=frame.Spec.Segments.Count;
        return paletteEffects.GetOrAdd((original,count),static key=>
        {
            var (effect,n)=key;
            var uniforms=Uniforms_.Replace("strandShape[3]",$"strandShape[{n}]",StringComparison.Ordinal).Replace("strandColor[3]",$"strandColor[{n}]",StringComparison.Ordinal)+"\nuniform float paletteCaustic; uniform float paletteOil; uniform float paletteSpecular;\n";
            var glass=GlassMain.Replace("o = over(o, gl);","o = over(o, gl * half(paletteSpecular));",StringComparison.Ordinal);
            string prefix="",main;
            if(ReferenceEquals(effect,BaseEffect)){prefix="uniform shader grad;\n";main=BaseMain;}
            else if(ReferenceEquals(effect,OilEffect))main=OilMain.Replace("0.9 * m","paletteOil * m",StringComparison.Ordinal);
            else if(ReferenceEquals(effect,StrandsEffect))main=StrandsMain.Replace("i < 3",$"i < {n}",StringComparison.Ordinal);
            else if(ReferenceEquals(effect,SlowEffect))main=SlowMain;
            else if(ReferenceEquals(effect,CausticEffect))main=CausticMain.Replace("ca * 0.8","ca * paletteCaustic",StringComparison.Ordinal);
            else if(ReferenceEquals(effect,CoverEffect))main=CoverMain;
            else if(ReferenceEquals(effect,MixEffect)){prefix="uniform shader liquid;\nuniform shader strikeOver;\nuniform shader light;\n";main=glass.Replace("//LIGHT","half4 k = strikeOver.eval(p); o = over(o, k); half4 l = light.eval(p); o = l + o - l * o;",StringComparison.Ordinal);}
            else if(ReferenceEquals(effect,LitEffect)){prefix="uniform shader liquid;\nuniform shader light;\n";main=glass.Replace("//LIGHT","half4 l = light.eval(p); o = l + o - l * o;",StringComparison.Ordinal);}
            else {prefix="uniform shader liquid;\n";main=glass;}
            return Compile(prefix+uniforms+Helpers+main,"palette "+n);
        }).Value;
    }
    internal sealed partial class PassUniforms
    {
        private float[] paletteShapes=[],paletteColors=[];
        private void ApplyPalette(in Frame f)
        {
            if(f.Palette is not {} p)return;
            var d=p.Motion;var count=f.Spec.Segments.Count;
            if(paletteShapes.Length!=count*4){paletteShapes=new float[count*4];paletteColors=new float[count*4];}
            var h=AlternateAt(p.BreathePhase*2);
            breathe[0]=(float)(1+d.Get("breathe_saturate_amp",.12)*h);breathe[1]=(float)(1+d.Get("breathe_bright_amp",.06)*h);Set("breathe",breathe);
            for(var i=0;i<count;i++)
            {
                var c=f.Spec.IsBlend?f.StrandColors[i]:Avalonia.Media.Colors.White;
                var a=c.A/255f;
                paletteColors[i*4]=c.R/255f*a;paletteColors[i*4+1]=c.G/255f*a;paletteColors[i*4+2]=c.B/255f*a;paletteColors[i*4+3]=a;
                paletteShapes[i*4]=f.Spec.IsBlend?f.Spec.Segments[i].Lead?10:6:5;
                paletteShapes[i*4+1]=4;paletteShapes[i*4+2]=f.Spec.IsBlend?(float)(i*Math.PI*2/count):0;paletteShapes[i*4+3]=f.Spec.IsBlend?3:5;
            }
            Set("strands",count);Set("strandShape",paletteShapes);Set("strandColor",paletteColors);
            // IR-3's fast amplitude is the SVG viewBox height, with wave amplitude/cycles from v22.
            var (fast,slow,caustic)=Offsets(f.Flow,Math.Max(f.Track,f.Size.Width));
            curA[2]=(float)fast;curB[2]=(float)slow;curB[3]=1;waveB[1]=2;waveB[2]=f.Spec.IsBlend?1:2;waveB[3]=5;Set("caustic",(float)caustic);
            curA[0]=(float)(d.Get("current_fast_amplitude",30)*(f.Spec.IsBlend?40/30d:1));curA[1]=(float)d.Get("current_fast_opacity",.55);
            curB[0]=curA[0];curB[1]=(float)d.Get("current_slow_opacity",.35);waveB[0]=(float)(d.Get("current_slow_amplitude",7)*(f.Spec.IsBlend?8/7d:1));
            Set("curA",curA);Set("curB",curB);Set("waveB",waveB);
            Set("paletteCaustic",(float)d.Get("caustic_alpha",.8));Set("paletteOil",(float)d.Get("oil_sheen_alpha",.9));Set("paletteSpecular",(float)d.Get("specular_scale",1));
            // OilInto expects seconds on the original 6.5 s alternating clock. Our accumulated phase supplies it.
            OilMask=OilInto(oil,f.Spec,f.Size.Width,p.OilPhase*6.5);Set("oil",oil);Set("oilMask",OilMask);
        }
    }
}

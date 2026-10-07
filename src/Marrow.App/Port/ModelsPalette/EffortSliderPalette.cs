// PORT-ONLY: post-Strike intent seam; ED-NEW-models-palette-14. No new effect or clock.
using Marrow.App.Port.ModelsPalette;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;
using SkiaSharp;
namespace Marrow.App.Views.Composer;
public sealed partial class EffortSlider
{
    private PaletteTrackTarget? palette;
    private TrackEffect? paletteEffect;
    private MorphingTextBlock? paletteTitle;
    private LinearGradientBrush? paletteTitleBrush;
    private SolidColorBrush? paletteSolidTitle;
    private float[]? paletteJewelFrom;
    private readonly SKColor[] paletteAlong=new SKColor[5];
    private SolidColorBrush? paletteFill;
    private uint paletteTitleHash;
    internal void SetPaletteTitle(MorphingTextBlock title,PaletteTrackTarget target)
    {
        paletteTitle=title;
        if(fastMode){paletteSolidTitle=new(EffortPalette.Fast.Resolve(ActualThemeVariant));paletteTitleBrush=null;if(ReduceMotion)EffortView.SetBrush(title,b=>title.Foreground=b,paletteSolidTitle);else EffortView.AnimateBrush(title,()=>title.Foreground,b=>title.Foreground=b,paletteSolidTitle,Animation.Smooth(.3));RequestFrame();return;}
        if(target.TitleStops.Count==1&&paletteTitleBrush is null){paletteTitleBrush=null;paletteSolidTitle=new(target.TitleStops[0].Color);EffortView.SetBrush(title,b=>title.Foreground=b,paletteSolidTitle);}
        else{paletteSolidTitle=null;paletteTitleBrush=new LinearGradientBrush{StartPoint=new(0,.5,Avalonia.RelativeUnit.Relative),EndPoint=new(1,.5,Avalonia.RelativeUnit.Relative)};
            for(var i=0;i<256;i++)paletteTitleBrush.GradientStops.Add(new(target.TitleStops[0].Color,i/255d));EffortView.SetBrush(title,b=>title.Foreground=b,paletteTitleBrush);}
        UpdatePaletteFrame();RequestFrame();
    }
    public void SetPalette(PaletteTrackTarget? target)
    {
        if(target is null){paletteEffect?.SetPalette(null);palette=null;paletteEffect=null;paletteJewelFrom=null;return;}
        if(palette is {} displayed&&displayed.SameVisual(target)){palette=displayed with {SnapshotDigest=target.SnapshotDigest};paletteEffect?.SetPalette(palette);return;}
        if(palette is not null)lock(jewelGate)paletteJewelFrom=(float[])Uniforms.Values.Clone();
        palette=target;paletteEffect=null;BindPaletteEffect(currentEffect);RequestFrame();
    }
    private void BindPaletteEffect(TrackEffect? effect)
    {
        if(palette is null||effect is null||ReferenceEquals(effect,paletteEffect))return;
        effect.SetPalette(palette);paletteEffect=effect;
    }
    private JewelMaterial PaletteMaterial(JewelMaterial fallback)
    {
        if(palette is null)return fallback;
        UpdatePaletteFrame();return palette.Jewel;
    }
    private void UpdatePaletteFrame()
    {
        if(palette is null||paletteEffect is null)return;
        var sample=paletteEffect.SamplePalette(SwiftDate.Now);if(sample is null)return;
        for(var i=0;i<5;i++){var n=(int)Math.Round(i*255/4d)*4;paletteAlong[i]=new(sample.Row[n],sample.Row[n+1],sample.Row[n+2]);}
        paletteFill??=new SolidColorBrush();paletteFill.Color=sample.Water?Colors.Transparent:new((byte)(sample.Frame.Glow>>24),(byte)(sample.Frame.Glow>>16),(byte)(sample.Frame.Glow>>8),(byte)sample.Frame.Glow);fill.Background=paletteFill;
        if(paletteTitle is not null&&!fastMode)
        {
            Color Colour(int n)=>new(sample.TitleRow[n+3],sample.TitleRow[n],sample.TitleRow[n+1],sample.TitleRow[n+2]);
            if(paletteSolidTitle is not null)paletteSolidTitle.Color=Colour(512);
            if(paletteTitleBrush is not null)foreach(var stop in paletteTitleBrush.GradientStops)stop.Color=Colour((int)Math.Clamp(Math.Round(stop.Offset*255),0,255)*4);
            var hash=2166136261u;foreach(var value in sample.TitleRow)hash=(hash^value)*16777619;
            if(hash!=paletteTitleHash){paletteTitleHash=hash;paletteTitle.UpdateLiveForeground();}
            if(sample.Frame.Progress>=1&&palette.TitleStops.Count==1&&paletteTitleBrush is not null){paletteSolidTitle=new(Colour(512));paletteTitleBrush=null;EffortView.SetBrush(paletteTitle,b=>paletteTitle.Foreground=b,paletteSolidTitle);}
        }
    }
    private void ApplyPaletteJewel(float[] into,DateTimeOffset date)
    {
        if(paletteEffect?.SamplePalette(date) is not {} sample)return;
        var t=sample.Frame.Progress;
        if(paletteJewelFrom is {} from)
        {
            for(var i=24;i<27;i++)into[i]=from[i]+(into[i]-from[i])*(float)t;
            for(var i=28;i<76;i++)into[i]=from[i]+(into[i]-from[i])*(float)t;
        }
        // Palette glow uses the same sample as the tube and title; the Jewel's own phase stays intact.
        var k=sample.GlowRow.Length-4;var glow=fastMode?EffortPalette.Fast.Resolve(ActualThemeVariant):new Color(255,sample.GlowRow[k],sample.GlowRow[k+1],sample.GlowRow[k+2]);
        into[72]=glow.R/255f;into[73]=glow.G/255f;into[74]=glow.B/255f;
        into[112]=(float)(fastMode?.6:sample.Motion.Get("glow_alpha",.6));into[113]=(float)(fastMode?8:sample.Motion.Get("glow_blur",8));
        for(var i=0;i<5;i++){var n=(int)Math.Round(i*255/4d)*4;into[88+i*4]=sample.Row[n]/255f;into[89+i*4]=sample.Row[n+1]/255f;into[90+i*4]=sample.Row[n+2]/255f;}
    }
    private SKColor[]? PaletteFillColours()=>palette is null?null:paletteAlong;
}

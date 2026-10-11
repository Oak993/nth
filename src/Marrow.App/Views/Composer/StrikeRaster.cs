using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Views.Composer;

// SPEC 6.2-6.4. All managed surfaces, filters, paints, paths and uniform blocks live until resize/retirement.
public sealed class StrikeRaster : IDisposable
{
    internal static readonly Lazy<SKRuntimeEffect> ColourEffect = new(() => SKRuntimeEffect.CreateShader("""
        uniform shader first; uniform shader second;
        uniform float gain; uniform float useFirst; uniform float4 colours[5];
        half4 main(float2 p) {
            float a=first.eval(p).a*useFirst,b=second.eval(p).a;
            float alpha=(1.0-(1.0-a)*(1.0-b))*gain;
            return half4(colours[0].rgb*alpha,alpha);
        }
        """, out var error) ?? throw new InvalidOperationException("Strike colour: " + error));
    internal static readonly Lazy<SKRuntimeEffect> WashEffect = new(() => SKRuntimeEffect.CreateShader("""
        uniform float3 tint;
        half4 main(float2 p) { float r=length(p)/100.0; float a=r<0.45?mix(1.0,0.35,r/0.45):mix(0.35,0.0,clamp((r-0.45)/0.55,0.0,1.0)); return half4(tint*a,a); }
        """, out var error) ?? throw new InvalidOperationException("Strike Mix wash: " + error));

    private readonly SKImageInfo info;
    private readonly SKSurface[] masks = new SKSurface[6];
    private readonly SKCanvas[] maskCanvases = new SKCanvas[6];
    private readonly SKCanvas overCanvas,screenCanvas,lightCanvas;
    private readonly nint[] images = new nint[6], shaders = new nint[6];
    private readonly SKSurface light;
    private readonly SKPaint stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint fill = new() { IsAntialias = true };
    private readonly SKPath path = new();
    private readonly LiquidTrack.PassUniforms colour = new(ColourEffect.Value), wash = new(WashEffect.Value);
    private readonly float[] colours = new float[20], tint = new float[3];
    private readonly SKPoint[] gradientPoints = new SKPoint[2];
    private readonly uint[] gradientColours = new uint[5];
    private readonly float[] gradientStops = [0,0.25f,0.5f,0.75f,1];
    private SKImageFilter? blurBig, blurSmall;
    private double filterScale;
    private double gradientWidth;
    private readonly StrikeBurstDrawing burstDrawing = new();
    public SKSurface Over { get; }
    public SKSurface Screen { get; }
    public int InkDraws { get; private set; }
    public int LightDraws { get; private set; }
    public int WashDraws { get; private set; }
    public int CompositeDraws { get; private set; }
    public int SurfacesMade => 9;
    public int FiltersMade { get; private set; }
    public StrikeRaster(SKImageInfo info, GRContext? gpu = null)
    {
        this.info = info;
        for (var i = 0; i < masks.Length; i++) masks[i] = Surface(info.WithColorType(SKColorType.Alpha8));
        Over = Surface(info); Screen = Surface(info); light = Surface(info);
        overCanvas=Over.Canvas; screenCanvas=Screen.Canvas; lightCanvas=light.Canvas;
        for(var i=0;i<masks.Length;i++) maskCanvases[i]=masks[i].Canvas;
        SKSurface Surface(SKImageInfo desired) => (gpu is null ? SKSurface.Create(desired) : SKSurface.Create(gpu, true, desired)) ?? throw new InvalidOperationException("Strike surface creation failed");
    }

    public void Paint(StrikeGeometry g, StrikeResolvedFrame frame, StrikeInk ink, double time, int seed, StrikeContacts? contacts = null)
    {
        // ED-NEW-warmupsplit-4: the raster's GPU draws are six warm-up sites (GpuWarmSites), numbered on every paint in this order:
        // the masks' strokes, the soft ink layer's fill and its blur, the sharp ink layer, the light, and the bursts with the light's
        // screen. Outside a warm-up part every site draws, so a frame paints as it did.
        var (strokes, softFill, softBlur, sharpInk, lit, screen) = (GpuWarmSites.Draw(), GpuWarmSites.Draw(), GpuWarmSites.Draw(), GpuWarmSites.Draw(), GpuWarmSites.Draw(), GpuWarmSites.Draw());
        InkDraws = LightDraws = WashDraws = CompositeDraws = 0; overCanvas.Clear(SKColors.Transparent); screenCanvas.Clear(SKColors.Transparent); lightCanvas.Clear(SKColors.Transparent);
        var scale = info.Height / g.Height;
        gradientWidth = g.Width * scale;
        if (filterScale != scale)
        {
            blurBig?.Dispose(); blurSmall?.Dispose(); blurBig = SKImageFilter.CreateBlur((float)(3 * scale), (float)(3 * scale)); blurSmall = SKImageFilter.CreateBlur((float)(2 * scale), (float)(2 * scale)); filterScale = scale; FiltersMade += 2;
        }
        for (var i = 0; i < masks.Length; i++) maskCanvases[i].Clear(SKColors.Transparent);
        if (frame.Opacity <= 0.002) { if (screen) DrawBursts(); return; }
        for (var i = 0; strokes && i < frame.Masks.Count; i++)
        {
            var m = frame.Masks.At(i); var c = maskCanvases[m.Set * 3 + m.Layer]; var saved = c.Save(); c.Scale((float)scale);
            Trace(m.Trace); stroke.StrokeWidth = (float)m.Width; stroke.ColorF = new SKColorF(1, 1, 1, (float)m.Alpha); c.DrawPath(path, stroke); c.RestoreToCount(saved);
        }
        try
        {
            for (var i = 0; i < masks.Length; i++) { images[i] = SkiaShaders.Snapshot(masks[i]); shaders[i] = SkiaShaders.ImageShader(images[i], SKMatrix.Identity); }
            if (ink.Amount > 0.01 && frame.Masks.Count > 0)
            {
                for (var layer = 0; layer < 2; layer++)
                {
                    if (!(layer == 0 ? softFill || softBlur : sharpInk)) continue;
                    var gain = (layer == 0 ? 0.55 : layer == 1 ? 0.92 : 1) * ink.Amount * frame.Opacity;
                    for (var i = 0; i < 5; i++) SetColour(i, layer == 2 ? ink.Palette.Core : ink.Gradient(layer == 0, time, seed % 7 * 0.9, i));
                    Composite(overCanvas, layer, gain, true, true, layer == 0 ? g.Height >= 34 ? blurBig : blurSmall : null,ink.Amount*frame.Opacity,ink.Palette.Core,softFill,softBlur); InkDraws+=layer==1?2:1;
                }
            }
            if (frame.LightOpacity >= 0.002 && lit)
            {
                for (var i = 0; i < frame.LightCount; i++) if (frame.Light[i].Layer == 0) { Light(lightCanvas, frame.Light[i], ink, g, scale); LightDraws++; }
                for (var layer = 0; layer < 3; layer++)
                {
                    var c = layer == 0 ? ink.Tint.Outer : layer == 1 ? ink.Tint.Inner : SKColors.White; for (var i = 0; i < 5; i++) SetColour(i, c);
                    Composite(lightCanvas, layer, layer == 0 ? 0.14 : layer == 1 ? 0.36 : 0.92, false, false, null); LightDraws++;
                }
            }
            if (screen) DrawBursts();
            if (frame.LightOpacity >= 0.002 && screen) { fill.ColorF = new SKColorF(1, 1, 1, (float)frame.LightOpacity); fill.BlendMode = SKBlendMode.Screen; light.Draw(screenCanvas, 0, 0, fill); CompositeDraws++; fill.BlendMode = SKBlendMode.SrcOver; }
        }
        finally { colour.SetChild("first", 0); colour.SetChild("second", 0); for (var i = 0; i < masks.Length; i++) { SkiaShaders.Release(shaders[i]); SkiaShaders.ReleaseImage(images[i]); images[i] = shaders[i] = 0; } }

        void DrawBursts()
        {
            for (var i = 0; i < frame.LightCount; i++) if (frame.Light[i].Layer == 1) Light(screenCanvas, frame.Light[i], ink, g, scale);
            if (contacts is null) return;
            var s = screenCanvas.Save(); var o = overCanvas.Save(); screenCanvas.Scale((float)scale); overCanvas.Scale((float)scale);
            for (var i = 0; i < contacts.RecentCount; i++)
            {
                burstDrawing.Resolve(g, contacts.Recent[i], time, ink); if (burstDrawing.RingCount == 0) continue;
                Glints(screenCanvas, ink.Tint.Inner, burstDrawing.GlintAlpha, false);
                if (ink.Amount >= 0.01) Glints(overCanvas, ink.InkColor, burstDrawing.InkGlintAlpha, true);
            }
            screenCanvas.RestoreToCount(s); overCanvas.RestoreToCount(o);
        }
    }

    private void SetColour(int i, SKColor c) { gradientColours[i] = (uint)c; colours[i * 4] = c.Red / 255f; colours[i * 4 + 1] = c.Green / 255f; colours[i * 4 + 2] = c.Blue / 255f; colours[i * 4 + 3] = 1; }
    private void Composite(SKCanvas canvas,int layer,double gain,bool useLock,bool perMask,SKImageFilter? filter,double coreGain=0,SKColor coreColor=default,bool fillSite=true,bool blurSite=true)
    {
        nint shader=0;
        try {
            if(useLock) shader=InkShader(layer,gain,coreGain,coreColor);
            else {
                colour.SetChild("first",shaders[layer]);colour.SetChild("second",shaders[3+layer]);
                colour.Set("gain",(float)gain);colour.Set("useFirst",0);colour.Set("colours",colours);
                shader=Required(colour.MakeShader());
            }
            SkiaShaders.SetShader(fill,shader);fill.Color=SKColors.White;fill.ImageFilter=null;
            if(filter is null) {canvas.DrawRect(new SKRect(0,0,info.Width,info.Height),fill);CompositeDraws++;}
            else {
                // P:579-585: device-scale RGBA8 soft canvas first, then its CSS blur.
                lightCanvas.Clear(SKColors.Transparent);if(fillSite){lightCanvas.DrawRect(new SKRect(0,0,info.Width,info.Height),fill);CompositeDraws++;}
                SkiaShaders.SetShader(fill,0);fill.ImageFilter=filter;
                var image=SkiaShaders.Snapshot(light);
                try {if(blurSite){SkiaShaders.DrawImage(canvas,image,0,0,fill);CompositeDraws++;}}
                finally {SkiaShaders.ReleaseImage(image);lightCanvas.Clear(SKColors.Transparent);}
            }
        }
        finally {fill.ImageFilter=null;SkiaShaders.SetShader(fill,0);SkiaShaders.Release(shader);}
    }
    private static nint Required(nint shader)=>shader!=0?shader:throw new InvalidOperationException("Strike shader graph creation failed");
    private static SKColor GainColour(double gain)=>new(255,255,255,(byte)Math.Clamp(Strike.Round(gain*255),0,255));
    private nint InkShader(int layer,double gain,double coreGain,SKColor coreColour)
    {
        nint body=0,core=0,alpha=0,scaled=0;
        try {
            body=InkLayer(layer,gain);
            if(layer==0)return body;
            for(var i=0;i<5;i++)gradientColours[i]=(uint)coreColour;
            core=InkLayer(2,1);alpha=Required(SkiaShaders.Solid(GainColour(coreGain)));
            scaled=Required(SkiaShaders.Blend(SKBlendMode.Modulate,core,alpha));
            return Required(SkiaShaders.Blend(SKBlendMode.SrcOver,body,scaled));
        }
        finally {if(layer!=0)SkiaShaders.Release(body);SkiaShaders.Release(core);SkiaShaders.Release(alpha);SkiaShaders.Release(scaled);}
    }
    private nint InkLayer(int layer,double gain)
    {
        nint alpha=0,first=0,second=0,union=0,gradient=0;
        try {
            alpha=Required(SkiaShaders.Solid(GainColour(gain)));
            first=Required(SkiaShaders.Blend(SKBlendMode.Modulate,shaders[layer],alpha));
            second=Required(SkiaShaders.Blend(SKBlendMode.Modulate,shaders[3+layer],alpha));
            union=Required(SkiaShaders.Blend(SKBlendMode.SrcOver,first,second));
            gradientPoints[1]=new SKPoint((float)gradientWidth,0);
            gradient=Required(SkiaShaders.LinearGradient(gradientPoints,gradientColours,gradientStops));
            return Required(SkiaShaders.Blend(SKBlendMode.SrcIn,union,gradient));
        }
        finally {SkiaShaders.Release(alpha);SkiaShaders.Release(first);SkiaShaders.Release(second);SkiaShaders.Release(union);SkiaShaders.Release(gradient);}
    }
    private void Trace(StrikeTrace trace) { path.Rewind(); path.MoveTo((float)trace.Line.X(trace.First), (float)trace.Y(trace.First)); for (var i = trace.First + 1; i <= trace.Last; i++) path.LineTo((float)trace.Line.X(i), (float)trace.Y(i)); }
    private void Light(SKCanvas canvas, StrikeLight op, StrikeInk ink, StrikeGeometry g, double scale)
    {
        var saved = canvas.Save(); canvas.Scale((float)scale);
        if (op.Kind == 0)
        {
            WashDraws++;
            tint[0] = ink.Tint.Wash.Red / 255f; tint[1] = ink.Tint.Wash.Green / 255f; tint[2] = ink.Tint.Wash.Blue / 255f; wash.Set("tint", tint); var shader = wash.MakeShader();
            SkiaShaders.SetShader(fill, shader); fill.ColorF = new SKColorF(1, 1, 1, (float)op.Alpha); canvas.Translate((float)op.X, (float)op.Y); canvas.Scale((float)(op.ScaleX * op.Radius / 100), (float)(op.Radius / 100));
            canvas.DrawRect(new SKRect(-100, -100, 100, 100), fill); SkiaShaders.SetShader(fill, 0); SkiaShaders.Release(shader);
        }
        else
        {
            Trace(op.Trace);
            for (var i = 0; i < 3; i++) { var c = i == 0 ? ink.Tint.Outer : i == 1 ? ink.Tint.Inner : SKColors.White; stroke.ColorF = new SKColorF(c.Red / 255f, c.Green / 255f, c.Blue / 255f, (float)StrikeStorm.FixedAlpha(op.Alpha * (i == 0 ? 0.14 : i == 1 ? 0.36 : 0.92))); stroke.StrokeWidth = (float)(i == 0 ? op.Wide * g.K : i == 1 ? 2.4 * g.K : Math.Max(0.8, 1.05 * g.K)); canvas.DrawPath(path, stroke); }
        }
        canvas.RestoreToCount(saved);
    }
    private void Glints(SKCanvas canvas, SKColor c, double alpha, bool pale)
    {
        path.Rewind(); for (var i = 0; i < burstDrawing.GlintCount; i++) { var g = burstDrawing.Glints[i]; path.AddCircle((float)g.X, (float)g.Y, (float)(g.Radius + (pale ? 0.7 : 0))); }
        fill.ColorF = new SKColorF(c.Red / 255f, c.Green / 255f, c.Blue / 255f, (float)alpha); canvas.DrawPath(path, fill);
    }
    public void Dispose() { foreach (var m in masks) m.Dispose(); light.Dispose(); Over.Dispose(); Screen.Dispose(); blurBig?.Dispose(); blurSmall?.Dispose(); colour.Dispose(); wash.Dispose(); stroke.Dispose(); fill.Dispose(); path.Dispose(); }
}

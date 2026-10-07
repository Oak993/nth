using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeRasterTests
{
    private static dynamic Raster(SKImageInfo info)
    {
        var type = typeof(Strike).Assembly.GetType("Marrow.App.Views.Composer.StrikeRaster"); Assert.NotNull(type); return Activator.CreateInstance(type, [info, null])!;
    }
    private static StrikeTrace Line(double x0, double y0, double x1, double y1) => new(new([x0, y0, x1, y1], 0, 2, 1, false, 3, 0), 0, 1, 0, 0);
    private static SKBitmap Pixels(SKSurface s) { var b = new SKBitmap(s.Canvas.DeviceClipBounds.Width, s.Canvas.DeviceClipBounds.Height); using var image = s.Snapshot(); Assert.True(image.ReadPixels(b.Info, b.GetPixels(), b.RowBytes, 0, 0)); return b; }

    [Theory]
    [InlineData(0, 0.14)]
    [InlineData(1, 0.36)]
    [InlineData(2, 0.92)]
    [Trait("Decision", "ED-248")]
    public void Crossing_filaments_union_before_the_whole_light_layer_gain(int layer, double gain)
    {
        var info = new SKImageInfo(64, 40, SKColorType.Rgba8888, SKAlphaType.Premul); dynamic raster = Raster(info);
        try
        {
            var f = new StrikeResolvedFrame { Opacity = 1, LightOpacity = 1 }; f.Masks.Add(new(1, layer, Line(8, 20, 56, 20), 6, 1)); f.Masks.Add(new(1, layer, Line(32, 4, 32, 36), 6, 1));
            var ink = StrikeInk.Of(StrikeOracleLiquid.From("Claude", "dark")); raster.Paint(new StrikeGeometry(64, 40, 50, 28), f, ink, 0d, 1274, null);
            using var pixels = Pixels((SKSurface)raster.Screen); var c = pixels.GetPixel(32, 20); Assert.InRange(Math.Abs(c.Alpha / 255d - gain), 0, 1 / 255d);
            var expected = layer switch { 0 => ink.Tint.Outer, 1 => ink.Tint.Inner, _ => SKColors.White };
            // Lead-approved correction: SPEC 6.4 defines precision in premultiplied channels.
            Assert.InRange(Math.Abs(c.Red * c.Alpha / (255d * 255) - expected.Red / 255d * gain), 0, 1 / 255d);
            Assert.InRange(Math.Abs(c.Green * c.Alpha / (255d * 255) - expected.Green / 255d * gain), 0, 1 / 255d);
            Assert.InRange(Math.Abs(c.Blue * c.Alpha / (255d * 255) - expected.Blue / 255d * gain), 0, 1 / 255d);
        }
        finally { ((IDisposable)raster).Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-248")]
    public void Ink_matches_an_independent_mask_union_recolour_and_gaussian_reference(bool spark)
    {
        var info = new SKImageInfo(96, 40, SKColorType.Rgba8888, SKAlphaType.Premul); var f = new StrikeResolvedFrame { Opacity = 1, LightOpacity = 0 };
        for (var set = spark ? 0 : 1; set < 2; set++) for (var layer = 0; layer < 3; layer++) f.Masks.Add(new(set, layer, Line(12, 12 + set * 12, 84, 28 - set * 12), layer == 0 ? 7 : layer == 1 ? 3 : 1, set == 0 ? 0.7 : 0.8));
        var ink = StrikeInk.Of(StrikeOracleLiquid.From("OpenAI", "dark")); dynamic raster = Raster(info);
        try
        {
            raster.Paint(new StrikeGeometry(96, 40, 80, 28), f, ink, 17.3, 1274, null);
            using var actual = Pixels((SKSurface)raster.Over); using var reference = Reference(info, f, ink, 17.3);
            for (var y = 0; y < info.Height; y++) for (var x = 0; x < info.Width; x++)
            {
                var a = actual.GetPixel(x, y); var b = reference.GetPixel(x, y);
                // Keep the original spark+plume guard at 3/255; add the stricter 1/255 no-spark case.
                var precision = spark ? 3 : 1;
                try {
                Assert.InRange(Math.Abs(a.Alpha - b.Alpha), 0, precision);
                // SPEC6.4: compare stored premultiplied bytes. GetPixel decodes straight RGB;
                // multiplying that rounded decode back can inflate a one-byte difference.
                for(var channel=0;channel<3;channel++)Assert.InRange(Math.Abs(RawChannel(actual,x,y,channel)-RawChannel(reference,x,y,channel)),0,precision);
                } catch (Xunit.Sdk.XunitException error) { throw new Xunit.Sdk.XunitException(error.Message + $" spark={spark} ({x},{y}) actual={a} expected={b} {RawPrecision(actual,reference)}"); }
            }
        }
        finally { ((IDisposable)raster).Dispose(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [Trait("Decision","ED-248")]
    public void Each_ink_layer_preserves_the_independent_canvas_reference(int layer)
    {
        var info=new SKImageInfo(96,40,SKColorType.Rgba8888,SKAlphaType.Premul);
        var f=new StrikeResolvedFrame {Opacity=1,LightOpacity=0};
        f.Masks.Add(new(1,layer,Line(12,24,84,16),layer==0?7:layer==1?3:1,0.8));
        var ink=StrikeInk.Of(StrikeOracleLiquid.From("OpenAI","dark")); using var raster=new StrikeRaster(info);
        raster.Paint(new(96,40,80,28),f,ink,17.3,1274);using var actual=Pixels(raster.Over);using var reference=Reference(info,f,ink,17.3);
        var max=0d; var worst="";
        for(var y=0;y<40;y++)for(var x=0;x<96;x++) {
            var a=actual.GetPixel(x,y);var b=reference.GetPixel(x,y);
            var delta=(double)Math.Abs(a.Alpha-b.Alpha);
            for(var channel=0;channel<3;channel++)delta=Math.Max(delta,Math.Abs(RawChannel(actual,x,y,channel)-RawChannel(reference,x,y,channel)));
            if(delta>max){max=delta;worst=$"({x},{y}) actual={a} expected={b}";}
        }
        Assert.True(max<=1,$"layer{layer}: difference {max} {worst} {RawPrecision(actual,reference)}");
    }

    private static string RawPrecision(SKBitmap actual,SKBitmap expected)
    {
        var rgb=0;var alpha=0;
        for(var y=0;y<actual.Height;y++)for(var x=0;x<actual.Width;x++)for(var c=0;c<4;c++) {
            var a=System.Runtime.InteropServices.Marshal.ReadByte(actual.GetPixels(),y*actual.RowBytes+x*4+c);
            var b=System.Runtime.InteropServices.Marshal.ReadByte(expected.GetPixels(),y*expected.RowBytes+x*4+c);
            if(c==3)alpha=Math.Max(alpha,Math.Abs(a-b));else rgb=Math.Max(rgb,Math.Abs(a-b));
        }
        return $"raw premul maxRGB={rgb},maxAlpha={alpha},types={actual.ColorType}/{actual.AlphaType},{expected.ColorType}/{expected.AlphaType}";
    }

    private static byte RawChannel(SKBitmap bitmap,int x,int y,int channel)
    {
        Assert.Equal(SKAlphaType.Premul,bitmap.AlphaType);Assert.Equal(SKColorType.Bgra8888,bitmap.ColorType);
        return System.Runtime.InteropServices.Marshal.ReadByte(bitmap.GetPixels(),y*bitmap.RowBytes+x*4+channel);
    }

    private static SKBitmap Reference(SKImageInfo info, StrikeResolvedFrame f, StrikeInk ink, double time)
    {
        var masks = Enumerable.Range(0, 6).Select(_ => SKSurface.Create(info)).ToArray();
        using var output = SKSurface.Create(info); using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var path = new SKPath();
        try
        {
            for (var i = 0; i < f.Masks.Count; i++)
            {
                var m = f.Masks.At(i); path.Rewind(); path.MoveTo((float)m.Trace.Line.X(m.Trace.First), (float)m.Trace.Y(m.Trace.First));
                for (var j = m.Trace.First + 1; j <= m.Trace.Last; j++) path.LineTo((float)m.Trace.Line.X(j), (float)m.Trace.Y(j));
                paint.StrokeWidth = (float)m.Width; paint.ColorF = new SKColorF(1, 1, 1, (float)m.Alpha); masks[m.Set * 3 + m.Layer].Canvas.DrawPath(path, paint);
            }
            for (var layer = 0; layer < 3; layer++)
            {
                using var combined = SKSurface.Create(info); using var p = new SKPaint(); var gain = layer switch { 0 => 0.55, 1 => 0.92, _ => 1 };
                p.ColorF = new SKColorF(1, 1, 1, (float)(layer == 2 ? 1 : gain*f.Opacity));
                masks[layer].Draw(combined.Canvas, 0, 0, p); masks[3 + layer].Draw(combined.Canvas, 0, 0, p);
                var colors = Enumerable.Range(0, 5).Select(i => layer == 2 ? ink.Palette.Core : ink.Gradient(layer == 0, time, 1274 % 7 * 0.9, i)).ToArray();
                using var gradient = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(info.Width, 0), colors, [0, 0.25f, 0.5f, 0.75f, 1], SKShaderTileMode.Clamp);
                p.Shader = gradient; p.BlendMode = SKBlendMode.SrcIn; p.Color = SKColors.White; combined.Canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height), p); p.Shader = null; p.BlendMode = SKBlendMode.SrcOver;
                using var blur = layer == 0 ? SKImageFilter.CreateBlur(3, 3) : null; p.ImageFilter = blur; p.ColorF=new SKColorF(1,1,1,(float)(layer==2?f.Opacity:1)); combined.Draw(output.Canvas, 0, 0, p);
            }
            return Pixels(output);
        }
        finally { foreach (var m in masks) m.Dispose(); }
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(0.5)]
    [InlineData(0.1)]
    [InlineData(0.003)]
    [InlineData(0.002)]
    [InlineData(0d)]
    [Trait("Decision","ED-248")]
    public void Pale_ink_gains_follow_the_fast_opacity_through_the_source_cutoff(double opacity)
    {
        var info=new SKImageInfo(96,40,SKColorType.Rgba8888,SKAlphaType.Premul); var frame=new StrikeResolvedFrame {Opacity=opacity,LightOpacity=0};
        for(var layer=0;layer<3;layer++) frame.Masks.Add(new(1,layer,Line(12,20,84,20),layer==0?7:layer==1?3:1,1));
        var ink=StrikeInk.Of(StrikeOracleLiquid.From("OpenAI","dark")); using var raster=new StrikeRaster(info);
        raster.Paint(new(96,40,80,28),frame,ink,17.3,1274); using var actual=Pixels(raster.Over);
        if(opacity<=0.002) { Assert.Equal(0,raster.InkDraws); for(var y=0;y<40;y++) for(var x=0;x<96;x++) Assert.Equal(0,actual.GetPixel(x,y).Alpha); return; }
        Assert.Equal(3,raster.InkDraws); using var expected=Reference(info,frame,ink,17.3);
        for(var y=0;y<40;y++) for(var x=0;x<96;x++)
        {
            try {
            var a=actual.GetPixel(x,y); var b=expected.GetPixel(x,y); Assert.InRange(Math.Abs(a.Alpha-b.Alpha),0,1);
            for(var channel=0;channel<3;channel++)Assert.InRange(Math.Abs(RawChannel(actual,x,y,channel)-RawChannel(expected,x,y,channel)),0,1);
            }catch(Xunit.Sdk.XunitException error){throw new Xunit.Sdk.XunitException(error.Message+$" op={opacity} {RawPrecision(actual,expected)}");}
        }
    }

    [Theory]
    [InlineData("Claude", true, false)]
    [InlineData("OpenAI", false, true)]
    [InlineData("AWS", true, true)]
    [Trait("Decision", "ED-248")]
    public void Light_and_ink_draw_only_in_their_source_proportions(string maker, bool light, bool inked)
    {
        var info = new SKImageInfo(298, 34, SKColorType.Rgba8888, SKAlphaType.Premul); dynamic raster = Raster(info);
        try
        {
            var ink = StrikeInk.Of(StrikeOracleLiquid.From(maker, "dark")); var p = StrikeMix.Timing(36863, 1274); var f = new StrikeStorm(1274).Resolve(new(298, 34, 272, 24), p.At + 0.03, 1, 1, ink.Amount);
            raster.Paint(new StrikeGeometry(298, 34, 272, 24), f, ink, 1d, 1274, null);
            Assert.Equal(light, (int)raster.LightDraws > 0); Assert.Equal(inked, (int)raster.InkDraws > 0); Assert.Equal(Strike.Round((1 - ink.Amount) * 1000) / 1000d, f.LightOpacity);
        }
        finally { ((IDisposable)raster).Dispose(); }
    }
}

using Avalonia;
using Avalonia.Styling;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Port.FirstRun;

// The existing GR-1 Strike family now prepares every Mix effect as well as the retained legacy API programs.
public static class StrikeWarmup
{
    public static GpuProgram Program { get; } = GpuProgram.Draw("strike", FirstRunSteps.Strike, Draw);
    private static void Draw(GpuWarmContext context)
    {
        var scale=Math.Max(1,context.Scaling); var legacy=GpuWarmup.LiquidFrame(true,scale);
        var info=new SKImageInfo((int)Math.Ceiling(legacy.Size.Width*scale),(int)Math.Ceiling(legacy.Size.Height*scale),SKColorType.Rgba8888,SKAlphaType.Premul);
        using var target=GpuWarmup.WarmTarget.Open(context.Gpu,info,scale);
        LiquidTrack.Draw(target.Context,legacy.Size,legacy.Track,legacy.Time,legacy.Spec,legacy.Gradient,legacy.Bubbles,false,ThemeVariant.Dark,legacy.Flow,legacy.Strike,legacy.BubbleInk);
        // The test factory deliberately returns a CPU stand-in. Real GPU targets retain the supplied GPU context.
        var gpu=target.Surface.Context is null ? null : context.Gpu;
        PaintMix(target.Surface,gpu,scale,EffortBrand.Claude); PaintMix(target.Surface,gpu,scale,EffortBrand.Silver); PaintMix(target.Surface,gpu,scale,EffortBrand.Copilot);
    }
    internal static int DrawMix(GpuWarmContext context, EffortBrand brand)
    {
        var scale = Math.Max(1, context.Scaling); var size = new Size(298, 34); var g = new StrikeGeometry(298, 34, 272, 24);
        var info = new SKImageInfo((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = context.Gpu is { } gpu ? SKSurface.Create(gpu, true, info) ?? throw new InvalidOperationException("Mix warm-up GPU target failed") : SKSurface.Create(info);
        return PaintMix(surface,context.Gpu,scale,brand);
    }
    private static int PaintMix(SKSurface surface,GRContext? gpu,double scale,EffortBrand brand)
    {
        var size=new Size(298,34); var g=new StrikeGeometry(298,34,272,24);
        var ink = StrikeInk.Of(JewelLiquid.Of(brand)); var storm = new StrikeStorm(1274);
        var spec = LiquidSpec.For(new TrackLook(TrackLook.Kind.Fast, brand)); var lanes = new StrikeBubbleLanes("53,9,3,1", 298, 34); var clock = new StrikeFastClock();
        var layers = new LiquidTrack.LiquidLayers(); var row = LiquidTrack.GradientFor(spec,(int)Math.Ceiling(size.Width*scale), ThemeVariant.Dark); var strands = LiquidTrack.StrandColorsOf(spec, ThemeVariant.Dark);
        var saved=surface.Canvas.Save(); surface.Canvas.ResetMatrix();
        try
        {
            surface.Canvas.Scale((float)scale); var passes = 0;
            foreach(var k in new[]{36862,36863,36870,36875,36887,36898,36918})
            {
                var plan = StrikeMix.Timing(k,1274);
                foreach(var age in new[]{0.035,1.3})
                {
                    var resolved = storm.Resolve(g,plan.At+age,1.3,1,ink.Amount);
                    var frame = new LiquidTrack.Frame(size,298,1.3,row,spec,null,false,strands,default,null,Layers:layers,
                        Mix:resolved,MixGeometry:g,MixInk:ink,MixLanes:lanes,FastClock:clock,MixSeed:1274);
                    layers.Draw(surface.Canvas,gpu,1,frame); passes |= layers.MixPasses;
                }
            }
            return passes;
        }
        finally { layers.Dispose(); surface.Canvas.RestoreToCount(saved); }
    }
}

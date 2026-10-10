using Avalonia;
using Avalonia.Styling;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Port.FirstRun;

// The existing GR-1 Strike family now prepares every Mix effect as well as the retained legacy API programs.
// ED-NEW-warmupsplit-1: the family is warmed in frame-sized parts, each its own step: the legacy strike frame one GPU draw (site)
// a part, then for each brand and Mix strike three parts: the fresh lightning (35 ms in) with half of its masks, with the other
// half, and the settled frame (1.3 s in). Every frame the whole family drew before is still drawn (a mask set split in two
// strokes the same layers with the same paints, so it makes the same programs: on ANGLE the cache took the same 24 entries
// whole, halved or in thirds); the parts only spread it over steps.
public static class StrikeWarmup
{
    /// <summary>The family every part makes (the registry's one "strike" program before the split).</summary>
    internal const string Family = "strike";

    /// <summary>How many GPU draws (sites) the legacy strike frame makes, so how many parts it is drawn in (a test counts them).</summary>
    internal const int LegacySites = 8;

    private static readonly int[] mixStrikes = [36862, 36863, 36870, 36875, 36887, 36898, 36918];
    private static readonly EffortBrand[] mixBrands = [EffortBrand.Claude, EffortBrand.Silver, EffortBrand.Copilot];

    /// <summary>The Mix strikes drawn for each brand (tests).</summary>
    internal static IReadOnlyList<int> MixStrikes => mixStrikes;

    /// <summary>The brands whose Mix is drawn (tests).</summary>
    internal static IReadOnlyList<EffortBrand> MixBrands => mixBrands;
    private const double Fresh = 0.035, Settled = 1.3;

    /// <summary>The family's parts, in the order a pass draws them (see the file note).</summary>
    public static IReadOnlyList<GpuProgram> Programs { get; } = Parts();

    private static GpuProgram[] Parts()
    {
        var parts = new List<GpuProgram>();
        for (var k = 0; k < LegacySites; k++)
        {
            var part = k;
            parts.Add(GpuProgram.Draw(Family + "[legacy " + part + "]", FirstRunSteps.Strike, context => GpuWarmup.DrawLiquid(context, strike: true, part, LegacySites), Family));
        }
        foreach (var brand in mixBrands)
            foreach (var strike in mixStrikes)
                for (var third = 0; third < 3; third++)
                {
                    var (b, s, t) = (brand, strike, third);
                    parts.Add(GpuProgram.Draw(Family + "[" + b + " " + s + " " + "abc"[t] + "]", FirstRunSteps.Strike, context => DrawMixPart(context, b, s, t), Family));
                }
        return [.. parts];
    }

    internal static int DrawMix(GpuWarmContext context, EffortBrand brand)
    {
        using var surface = MixSurface(context, out var scale);
        return PaintMix(surface,context.Gpu,scale,brand,mixStrikes,-1);
    }

    /// <summary>One part of a brand's Mix strike: <paramref name="part"/> 0 the fresh frame's even masks, 1 its odd masks, 2 the
    /// settled frame (whole).</summary>
    internal static int DrawMixPart(GpuWarmContext context, EffortBrand brand, int strike, int part)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(part);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(part, 2);
        using var surface = MixSurface(context, out var scale);
        return PaintMix(surface, context.Gpu, scale, brand, [strike], part);
    }

    private static SKSurface MixSurface(GpuWarmContext context, out double scale)
    {
        scale = Math.Max(1, context.Scaling); var size = new Size(298, 34);
        var info = new SKImageInfo((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), SKColorType.Rgba8888, SKAlphaType.Premul);
        return context.Gpu is { } gpu ? SKSurface.Create(gpu, true, info) ?? throw new InvalidOperationException("Mix warm-up GPU target failed") : SKSurface.Create(info);
    }

    // part -1: both frames whole; 0: the fresh frame's even masks; 1: its odd masks; 2: the settled frame whole
    private static int PaintMix(SKSurface surface,GRContext? gpu,double scale,EffortBrand brand,int[] strikes,int part)
    {
        var size=new Size(298,34); var g=new StrikeGeometry(298,34,272,24);
        var ink = StrikeInk.Of(JewelLiquid.Of(brand)); var storm = new StrikeStorm(1274);
        var spec = LiquidSpec.For(new TrackLook(TrackLook.Kind.Fast, brand)); var lanes = new StrikeBubbleLanes("53,9,3,1", 298, 34); var clock = new StrikeFastClock();
        var layers = new LiquidTrack.LiquidLayers(); var row = LiquidTrack.GradientFor(spec,(int)Math.Ceiling(size.Width*scale), ThemeVariant.Dark); var strands = LiquidTrack.StrandColorsOf(spec, ThemeVariant.Dark);
        var saved=surface.Canvas.Save(); surface.Canvas.ResetMatrix();
        try
        {
            surface.Canvas.Scale((float)scale); var passes = 0;
            foreach(var k in strikes)
            {
                var plan = StrikeMix.Timing(k,1274);
                foreach(var age in part switch { -1 => new[]{Fresh,Settled}, 2 => [Settled], _ => [Fresh] })
                {
                    var resolved = storm.Resolve(g,plan.At+age,1.3,1,ink.Amount);
                    if (part is 0 or 1) Halve(resolved.Masks, part);
                    var frame = new LiquidTrack.Frame(size,298,1.3,row,spec,null,false,strands,default,null,Layers:layers,
                        Mix:resolved,MixGeometry:g,MixInk:ink,MixLanes:lanes,FastClock:clock,MixSeed:1274);
                    layers.Draw(surface.Canvas,gpu,1,frame); passes |= layers.MixPasses;
                }
            }
            return passes;
        }
        finally { layers.Dispose(); surface.Canvas.RestoreToCount(saved); }
    }

    // keeps the masks whose index has the half's parity, in their order
    internal static void Halve(StrikeMaskQueue masks, int half)
    {
        var all = new StrikeMask[masks.Count];
        for (var i = 0; i < all.Length; i++) all[i] = masks.At(i);
        masks.Clear();
        for (var i = half; i < all.Length; i += 2) masks.Add(all[i]);
    }
}

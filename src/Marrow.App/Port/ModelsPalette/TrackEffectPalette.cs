// PORT-ONLY: C25 same effect, startedAt, Strike state, layers and frame clock; ED-NEW-models-palette-14.
using Avalonia.Media;
using Marrow.App.Port.ModelsPalette;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
using Marrow.UI.Port;
using SkiaSharp;
namespace Marrow.App.Views.Composer;
public sealed partial class TrackEffect
{
    private readonly PaletteRenderer paletteRenderer=new();
    private readonly PaletteLiveSample paletteSample=new();
    private PaletteTrackTarget? paletteTarget,paletteWaiting;
    private SKColor[]? paletteFrom;
    private double? paletteTime;
    public PaletteArrangement? PaletteArrangement=>paletteRenderer.Arrangement;
    public void SetPalette(PaletteTrackTarget? target,bool arrivalRunning=false)
    {
        // UI intent changes and the retained render callback synchronize on the existing layers lock.
        lock((object?)layers??this)
        {
            if(target is null){paletteTarget=null;paletteWaiting=null;paletteTime=null;resolvedScene=null;canvas.InvalidateVisual();return;}
            if(arrivalRunning&&!EnvironmentValues.GetAccessibilityReduceMotion(this)){paletteWaiting=target;return;}
            paletteWaiting=null;
            var at=TimeAt(SwiftDate.Now)+mixPageOrigin;
            if(paletteTarget is not null)
            {
                paletteRenderer.Sample(at,paletteSample.Row,paletteSample.Values);
                paletteFrom=new SKColor[256];for(var n=0;n<256;n++)paletteFrom[n]=new(paletteSample.Row[n*4],paletteSample.Row[n*4+1],paletteSample.Row[n*4+2]);
            }
            paletteRenderer.Retarget(target.Composite,at,arrivalRunning,EnvironmentValues.GetAccessibilityReduceMotion(this));
            paletteTarget=target;paletteSample.Keys=paletteRenderer.MotionKeys;
            if(paletteSample.Values.Length!=paletteRenderer.MotionCount)paletteSample.Values=new double[paletteRenderer.MotionCount];
            resolvedScene=null;canvas.InvalidateVisual();
        }
    }
    public void PaletteArrivalFinished()
    {lock((object?)layers??this){if(paletteWaiting is {} target)SetPalette(target);}}
    private LiquidTrack.Frame ApplyPalette(LiquidTrack.Frame frame,double t)
    {
        if(paletteTarget is not {} target)return frame;
        paletteSample.Frame=paletteRenderer.Sample(t,paletteSample.Row,paletteSample.Values);
        // Integrate phases with the live interpolated periods. Retargeting never restarts any loop.
        if(paletteTime is null){paletteSample.FastPhase=frame.Flow.Fast;paletteSample.SlowPhase=frame.Flow.Slow;paletteSample.CausticPhase=frame.Flow.Caustic;paletteSample.BreathePhase=frame.Time/4.8;paletteSample.OilPhase=frame.Time/6.5;}
        var dt=paletteTime is {} previous?Math.Max(0,t-previous):0;paletteTime=t;
        var flowDelta=frame.MixStill?0:dt;var d=paletteSample.Motion;
        // Strike's continuous fast clock owns the speed-up: its fast value is read at the same t.
        var rate=frame.FastClock is {} clock?1+.5*clock.FastAt(t):1;
        paletteSample.FastPhase+=flowDelta*rate/d.Get("current_fast_period_s",7);
        paletteSample.SlowPhase+=flowDelta*rate/d.Get("current_slow_period_s",11);
        paletteSample.CausticPhase+=flowDelta*rate/d.Get("caustic_period_s",6);
        paletteSample.BreathePhase+=flowDelta/d.Get("breathe_period_s",4.8);
        paletteSample.OilPhase+=flowDelta/d.Get("oil_sheen_period_s",6.5);
        return frame with {Spec=target.Spec,Gradient=target.Gradient,StrandColors=target.StrandColors,Palette=paletteSample,
            PreviousGradient=paletteSample.Frame.Progress<1?paletteFrom:null,GradientMix=paletteSample.Frame.Progress,
            Flow=new(paletteSample.FastPhase,paletteSample.SlowPhase,paletteSample.CausticPhase)};
    }
}

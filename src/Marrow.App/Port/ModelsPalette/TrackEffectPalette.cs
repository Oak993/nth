// PORT-ONLY: C25 same effect, startedAt, Strike state, layers and frame clock; ED-625.
using Avalonia.Media;
using Marrow.App.Port.ModelsPalette;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.UI.Port;
using SkiaSharp;
namespace Marrow.App.Views.Composer;
public sealed partial class TrackEffect
{
    private readonly PaletteRenderer paletteRenderer=new();
    private readonly PaletteLiveSample paletteSample=new();
    private PaletteGlide? paletteTitleGlide,paletteGlowGlide;
    private double specularFrom=1,specularTo=1,specularBegan,specularSeconds=1.2;
    private double previousSpecularFrom=1,previousSpecularTo=1,previousSpecularBegan,previousSpecularSeconds=1.2;
    private PaletteTrackTarget? paletteTarget,paletteWaiting;
    private SKColor[]? paletteFrom;
    private double? paletteTime;
    private double? paletteBorn,solidBegan;
    private bool paletteStartPending,paletteHasDrawn,hasShownPending;
    private readonly TimeProvider paletteClock=SwiftClock.Provider.Current;
    private ITimer? palettePendingTimer;
    private Color[] paletteStrands=[];
    private bool inkRequested,neutralHeld;
    private PaletteInkWorker? paletteInk;
    // Lead (native gallery, 2026-10-08): ApplyPalette runs on the render thread (LiquidTrack.Live.Render -> TryFrame), and so does
    // SetPalette when PaletteArrivalFinished runs from there; reading the AccessibilityReduceMotion property off the UI thread threw
    // "a different thread owns it" on every frame and the render loop dropped the whole effort popover (it showed empty). The UI
    // thread reads the property and keeps this copy; any other thread reads the copy.
    private volatile bool paletteReduceCopy;
    private bool PaletteReduceMotion => Avalonia.Threading.Dispatcher.UIThread.CheckAccess()
        ? paletteReduceCopy = EnvironmentValues.GetAccessibilityReduceMotion(this)
        : paletteReduceCopy;
    private bool PaletteTransitionActive
    {
        get
        {
            lock ((object?)layers ?? this)
                return paletteTarget is not null && (Time + mixPageOrigin < specularBegan + specularSeconds
                    || paletteTarget.State == PaletteState.Pending && !neutralHeld || inkRequested || paletteInk is { Finished: false }
                    || solidBegan is { } solid && Time + mixPageOrigin < solid + .5);
        }
    }
    private static readonly Animation InkEase=Animation.TimingCurve(.25,.1,.25,1,.5);
    internal void ReleasePaletteInk(){palettePendingTimer?.Dispose();palettePendingTimer=null;paletteInk?.Retire();paletteInk=null;paletteSample.Ink=null;}
    internal void ReleasePaletteView(){ReleasePaletteInk();paletteHasDrawn=hasShownPending=false;paletteBorn=null;}
    public PaletteArrangement? PaletteArrangement=>paletteRenderer.Arrangement;
    internal PaletteLiveSample? SamplePalette(DateTimeOffset date)
    {
        lock((object?)layers??this)
        {
            if(paletteTarget is not {} target)return null;
            paletteSample.Water=target.State==PaletteState.Router||target.State==PaletteState.Pending&&!neutralHeld||paletteInk is not null||inkRequested;
            paletteSample.Loading=(target.State==PaletteState.Pending||inkRequested)&&!neutralHeld&&paletteInk is null;
            SampleColours(TimeAt(date)+mixPageOrigin);return paletteSample;
        }
    }
    public void SetPalette(PaletteTrackTarget? target,bool arrivalRunning=false)
    {
        // UI intent changes and the retained render callback synchronize on the existing layers lock.
        lock((object?)layers??this)
        {
            if(target is null){ReleasePaletteInk();paletteTarget=null;paletteWaiting=null;paletteTime=null;paletteBorn=null;solidBegan=null;paletteHasDrawn=hasShownPending=false;inkRequested=neutralHeld=false;resolvedScene=null;canvas.InvalidateVisual();return;}
            if(paletteTarget is {} displayed&&displayed.SameVisual(target)){paletteTarget=displayed with {SnapshotDigest=target.SnapshotDigest};return;}
            if(paletteInk is not null&&!paletteInk.Finished){paletteWaiting=target;return;}
            if(target.State==PaletteState.Pending&&paletteTarget?.State!=PaletteState.Pending){ReleasePaletteInk();paletteBorn=null;solidBegan=null;hasShownPending=false;neutralHeld=false;}
            if(hasShownPending&&paletteTarget?.State==PaletteState.Pending&&target.State==PaletteState.Ready)
            {ReleasePaletteInk();solidBegan=null;neutralHeld=false;inkRequested=true;}
            if(arrivalRunning&&!PaletteReduceMotion){paletteWaiting=target;return;}
            paletteWaiting=null;
            var at=TimeAt(SwiftDate.Now)+mixPageOrigin;
            if(paletteTarget is not null)
            {
                paletteRenderer.Sample(at,paletteSample.Row,paletteSample.Values);
                paletteFrom=new SKColor[256];for(var n=0;n<256;n++)paletteFrom[n]=new(paletteSample.Row[n*4],paletteSample.Row[n*4+1],paletteSample.Row[n*4+2]);
            }
            var reduce=PaletteReduceMotion;var effort=paletteTarget is {} old&&old.SnapshotDigest==target.SnapshotDigest&&old.ModelsKey==target.ModelsKey;
            previousSpecularFrom=specularFrom;previousSpecularTo=specularTo;previousSpecularBegan=specularBegan;previousSpecularSeconds=specularSeconds;
            specularBegan=at;specularSeconds=effort?.6:1.2;
            var sampleAt=reduce?at-1.2:at;specularFrom=paletteSample.Specular;specularTo=target.Composite.Shade.Motion.GetValueOrDefault("specular_scale",1);
            if(!paletteHasDrawn){paletteRenderer.Snap(target.Composite);paletteFrom=null;}else paletteRenderer.Retarget(target.Composite,at,arrivalRunning,reduce,effort);
            if(!paletteHasDrawn||paletteTitleGlide is null){paletteTitleGlide=new(target.TitleShade);paletteGlowGlide=new(target.GlowShade);specularFrom=specularTo;}
            else{paletteTitleGlide.Retarget(target.TitleShade,sampleAt,colourSeconds:effort?.3:1.2,motionSeconds:effort?.6:1.2);paletteGlowGlide!.Retarget(target.GlowShade,sampleAt,colourSeconds:effort?.3:1.2,motionSeconds:effort?.6:1.2);}
            paletteStartPending=!reduce&&paletteHasDrawn&&paletteTarget is not null;
            paletteSample.OilSpec=target.Spec.OilRange() is not null?target.Spec:paletteTarget?.Spec.OilRange() is not null?paletteTarget.Spec:paletteSample.OilSpec;
            if(paletteStrands.Length!=target.StrandColors.Length)paletteStrands=new Color[target.StrandColors.Length];
            if(target.State!=PaletteState.Pending){palettePendingTimer?.Dispose();palettePendingTimer=null;}
            if(inkRequested&&paletteBorn is {} bornAt){var remaining=.8-(paletteClock.GetTimestamp()/(double)paletteClock.TimestampFrequency-bornAt);if(remaining>0)palettePendingTimer=paletteClock.CreateTimer(_=>MainActor.Post(()=>{lock((object?)layers??this){resolvedScene=null;canvas.InvalidateVisual();}}),null,TimeSpan.FromTicks((long)Math.Ceiling(remaining*TimeSpan.TicksPerSecond)),Timeout.InfiniteTimeSpan);}
            paletteTarget=target;paletteSample.Keys=paletteRenderer.MotionKeys;
            if(paletteSample.Values.Length!=paletteRenderer.MotionCount)paletteSample.Values=new double[paletteRenderer.MotionCount];
            resolvedScene=null;canvas.InvalidateVisual();
            RenderFrameSource.WakeAmbientFor(this);
        }
    }
    public void PaletteArrivalFinished()
    {lock((object?)layers??this){if(paletteWaiting is {} target)SetPalette(target);}}
    private LiquidTrack.Frame ApplyPalette(LiquidTrack.Frame frame,double t)
    {
        if(paletteTarget is not {} target)return frame;
        paletteHasDrawn=true;if(target.State==PaletteState.Pending)hasShownPending=true;
        var stateAt=paletteClock.GetTimestamp()/(double)paletteClock.TimestampFrequency;
        if(paletteBorn is null){paletteBorn=stateAt;if(target.State==PaletteState.Pending)palettePendingTimer=paletteClock.CreateTimer(_=>MainActor.Post(()=>{lock((object?)layers??this){resolvedScene=null;canvas.InvalidateVisual();}}),null,TimeSpan.FromSeconds(3),Timeout.InfiniteTimeSpan);}
        var reduce=PaletteReduceMotion;
        if(target.State==PaletteState.Pending&&!neutralHeld&&stateAt-paletteBorn>=3)inkRequested=true;
        if(inkRequested&&stateAt-paletteBorn>=.8)
        {
            inkRequested=false;
            if(reduce){neutralHeld=true;solidBegan=t-.5;PaletteArrivalFinished();}
            else paletteInk??=new PaletteInkWorker(target.Gradient);
        }
        paletteInk?.Advance(t);
        if(paletteInk?.Finished==true&&solidBegan is null)solidBegan=t;
        var solid=solidBegan is {} since?InkEase.Progress(Math.Clamp(t-since,0,.5)):0;
        if(solid>=1&&paletteInk is not null){neutralHeld=true;ReleasePaletteInk();PaletteArrivalFinished();}
        SampleColours(t);
        paletteSample.Ink=paletteInk;paletteSample.SolidMix=solid;
        paletteSample.Water=target.State==PaletteState.Router||target.State==PaletteState.Pending&&!neutralHeld||paletteInk is not null||inkRequested;
        paletteSample.Loading=(target.State==PaletteState.Pending||inkRequested)&&paletteInk is null&&!neutralHeld;
        paletteSample.LoadingPhase=stateAt-paletteBorn.Value;
        for(var i=0;i<paletteStrands.Length;i++){var span=target.TitleStops[i];var x=(int)Math.Clamp(Math.Round((span.Start+span.End)*.5*255),0,255)*4;paletteStrands[i]=new(255,paletteSample.TitleRow[x],paletteSample.TitleRow[x+1],paletteSample.TitleRow[x+2]);}
        // Integrate phases with the live interpolated periods. Retargeting never restarts any loop.
        var d=paletteSample.Motion;
        if(paletteTime is null){paletteSample.FastPhase=frame.Flow.Fast*7/d.Get("current_fast_period_s",7);paletteSample.SlowPhase=frame.Flow.Slow*11/d.Get("current_slow_period_s",11);paletteSample.CausticPhase=frame.Flow.Caustic*6/d.Get("caustic_period_s",6);paletteSample.BreathePhase=frame.Time/d.Get("breathe_period_s",4.8);paletteSample.OilPhase=frame.Time/d.Get("oil_sheen_period_s",6.5);}
        var dt=paletteTime is {} previous?Math.Max(0,t-previous):0;paletteTime=t;
        var flowDelta=frame.MixStill?0:dt;
        frame.MixLanes?.SetPaletteDials(d.Get("bubble_px_per_lane",32),d.Get("bubble_rise_scale",1),d.Get("bubble_pause_max_s",1.4),d.Get("bubble_large_share",.1));
        // Strike's continuous fast clock owns the speed-up: its fast value is read at the same t.
        var rate=frame.FastClock is {} clock?1+.5*clock.FastAt(t):1;
        paletteSample.FastPhase+=flowDelta*rate/d.Get("current_fast_period_s",7);
        paletteSample.SlowPhase+=flowDelta*rate/d.Get("current_slow_period_s",11);
        paletteSample.CausticPhase+=flowDelta*rate/d.Get("caustic_period_s",6);
        paletteSample.BreathePhase+=flowDelta/d.Get("breathe_period_s",4.8);
        paletteSample.OilPhase+=flowDelta/d.Get("oil_sheen_period_s",6.5);
        return frame with {Spec=target.Spec,Gradient=target.Gradient,StrandColors=paletteStrands,Palette=paletteSample,
            PreviousGradient=paletteSample.Frame.Progress<1?paletteFrom:null,GradientMix=paletteSample.Frame.Progress,
            Flow=new(paletteSample.FastPhase,paletteSample.SlowPhase,paletteSample.CausticPhase)};
    }
    private void PreparePaletteBubbleLanes(double at,StrikeBubbleLanes lanes)
    {if(paletteTarget is null)return;SampleColours(at);var d=paletteSample.Motion;lanes.SetPaletteDials(d.Get("bubble_px_per_lane",32),d.Get("bubble_rise_scale",1),d.Get("bubble_pause_max_s",1.4),d.Get("bubble_large_share",.1));}
    private void SampleColours(double at)
    {
        var first=paletteStartPending;
        if(first){paletteStartPending=false;paletteRenderer.StartAt(at);paletteTitleGlide?.StartAt(at);paletteGlowGlide?.StartAt(at);specularFrom=previousSpecularFrom+(previousSpecularTo-previousSpecularFrom)*Math.Clamp((at-previousSpecularBegan)/previousSpecularSeconds,0,1);specularBegan=at;}
        paletteSample.Frame=paletteRenderer.Sample(at,paletteSample.Row,paletteSample.Values);
        if(first&&paletteFrom is not null)for(var n=0;n<256;n++)paletteFrom[n]=new(paletteSample.Row[n*4],paletteSample.Row[n*4+1],paletteSample.Row[n*4+2]);
        paletteTitleGlide?.Sample(at,paletteSample.TitleRow,paletteSample.Values);paletteGlowGlide?.Sample(at,paletteSample.GlowRow,paletteSample.Values);
        paletteSample.Specular=specularFrom+(specularTo-specularFrom)*paletteRenderer.MotionProgress(at);
    }
}

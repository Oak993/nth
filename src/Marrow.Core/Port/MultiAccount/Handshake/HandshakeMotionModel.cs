// PORT-ONLY: MA-4-1, live event-driven phase timeline; page timing :3039, statuses :3374-3394; OD-0121/0122.
using Marrow.Core.Accounts;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Thread;
namespace Marrow.Core.Port.MultiAccount.Handshake;
public sealed class HandshakeMotionModel : IDisposable
{
    private readonly Guid thread;
    private readonly ICardPort port;
    private readonly Func<IReadOnlyList<ThreadAccount>> accounts;
    private readonly TimeProvider clock;
    private readonly Func<bool> reduce;
    private readonly Func<HandshakeEvent,HandshakeReadiness> readiness;
    private readonly List<HandshakeStage> stages=[];
    private readonly List<HandshakeTone> sounds=new(3);
    private static readonly HandshakeTone LinkLow=new(523.25,1.1,.03,0,.04,.0001,.05), LinkHigh=new(783.99,1.3,.022,.08,.04,.0001,.05), SyncTick=new(1318.5,.18,.025,0,.04,.0001,.05);
    private string? from;
    private string? to;
    private bool closed;
    private double outgoingLeft = 1, incomingLeft = 1;
    private string standbyText = "", linkingText = "", syncText = "", injectedText = "", completeText = "", deferredText = "";
    private const string PauseText = "Safe pause · the switch waits for the edit to finish";
    public HandshakeMotionModel(Guid thread,ICardPort port,Func<IReadOnlyList<ThreadAccount>> accounts,TimeProvider clock,
        Func<bool>? reduceMotion=null,Func<HandshakeEvent,HandshakeReadiness>? readiness=null)
    {
        this.thread=thread;this.port=port;this.accounts=accounts;this.clock=clock;reduce=reduceMotion??(()=>false);this.readiness=readiness??(_=>HandshakeReadiness.Primed);
        port.Changed+=Observe;
        if(port.State(thread) is {} state) Observe(port,state);
    }
    public bool SoundEnabled {get;set;}
    public event Action? Changed;
    public void Dispose()=>port.Changed-=Observe;
    private static int Rank(HandshakePhase p)=>p switch {HandshakePhase.Standby=>1,HandshakePhase.Linking=>2,HandshakePhase.InSync=>3,HandshakePhase.Switch=>4,_=>0};
    private static double Minimum(HandshakePhase p)=>p switch {HandshakePhase.Standby=>1000,HandshakePhase.Linking=>1000,HandshakePhase.InSync=>1400,_=>620};
    private void Observe(object? sender,HandshakeEvent e)
    {
        if(e.ThreadId!=thread)return;
        if(e.To==HandshakePhase.Idle&&stages.LastOrDefault()?.Phase==HandshakePhase.Switch){closed=true;return;}
        if(Rank(e.To)==0){stages.Clear();sounds.Clear();closed=false;Changed?.Invoke();return;}
        if(e.To==HandshakePhase.Standby&&closed){stages.Clear();sounds.Clear();closed=false;}
        var last=stages.LastOrDefault();var rank=Rank(e.To);
        if(last is not null&&rank<Rank(last.Phase))return;
        from=e.OutgoingAccountId??from;to=e.IncomingAccountId??to;
        RefreshAccounts();
        if(last?.Phase==e.To)
        {
            last.Ready|=e.Kind==HandshakeEventKind.InSyncReady;last.Readiness=readiness(e);Changed?.Invoke();return;
        }
        var at=clock.GetUtcNow();if(last is not null&&at<last.Starts.AddMilliseconds(Minimum(last.Phase)))at=last.Starts.AddMilliseconds(Minimum(last.Phase));
        stages.Add(new(e.To,at,readiness(e),e.Kind==HandshakeEventKind.InSyncReady));Changed?.Invoke();
    }
    private HandshakeStage? Pump()
    {
        var now=clock.GetUtcNow();HandshakeStage? shown=null;
        foreach(var stage in stages)
        {
            if(stage.Starts>now)break;shown=stage;
            if(!stage.Entered)
            {
                stage.Entered=true;
                if(SoundEnabled&&stage.Phase==HandshakePhase.Linking) {sounds.Add(LinkLow);sounds.Add(LinkHigh);}
            }
            if(stage.Ready&&stage.Readiness!=HandshakeReadiness.Deferred&&!stage.TickPlayed)
            {
                stage.TickPlayed=true;if(SoundEnabled)sounds.Add(SyncTick);
            }
        }
        return shown;
    }
    public IReadOnlyList<HandshakeTone> TakeSounds(){Pump();var result=sounds.ToArray();sounds.Clear();return result;}
    /// <summary>Call at account/usage changes, never from the painter frame loop. Handshake events refresh automatically.</summary>
    public void RefreshAccounts()
    {
        ThreadAccount? outgoing = null, incoming = null;
        Guid.TryParse(from, out var fromId); Guid.TryParse(to, out var toId);
        foreach (var row in accounts()) { if (row.Id == fromId) outgoing = row; if (row.Id == toId) incoming = row; }
        outgoingLeft = Left(outgoing); incomingLeft = Left(incoming);
        var name = incoming?.Name ?? ""; var old = outgoing?.Name ?? "";
        standbyText = "Standby · " + name + " warms up in the background";
        linkingText = "Linking · " + old + " briefs " + name;
        syncText = "In sync · " + name + " read the plan back; nth checked it";
        injectedText = "In sync · " + name;
        completeText = "Handshake complete · " + name + " is in sync";
        deferredText = "Continued on " + name + " · What was handed over";
    }
    private static double Left(ThreadAccount? a) => a is null || a.NoLimit ? 1 : Math.Clamp(1 - (a.NoFiveHour ? a.Usage?.Week : a.Usage?.FiveHour ?? a.Usage?.Week ?? 0).GetValueOrDefault(), 0, 1);
    /// <summary>Allocation-free steady frame sampling: no accounts callback, strings, LINQ, geometry or reference snapshot.</summary>
    public HandshakeFrameSnapshot ReadFrame(bool dark)
    {
        var stage = Pump(); var now = clock.GetUtcNow();
        var elapsed = stage is null ? 0 : Math.Max(0, (now - stage.Starts).TotalMilliseconds);
        var phase = stage?.Phase ?? HandshakePhase.Idle;
        var t = phase switch { HandshakePhase.Standby => 1000 + Math.Min(999, elapsed), HandshakePhase.Linking => 2000 + Math.Min(1000, elapsed), HandshakePhase.InSync => 3000 + Math.Min(1399, elapsed), HandshakePhase.Switch => 4400 + Math.Min(1300, elapsed), _ => 0 };
        var tick = false;
        foreach (var s in stages) if (s.Starts <= now && s.Ready && s.Readiness != HandshakeReadiness.Deferred) { tick = true; break; }
        tick &= !(phase == HandshakePhase.Switch && elapsed >= 1820);
        var status = stage?.Readiness == HandshakeReadiness.Deferred && phase is HandshakePhase.InSync or HandshakePhase.Switch ? deferredText : phase switch
        {
            HandshakePhase.Standby => standbyText,
            HandshakePhase.Linking => linkingText,
            HandshakePhase.InSync when elapsed >= 800 => PauseText,
            HandshakePhase.InSync => stage?.Readiness == HandshakeReadiness.Injected ? injectedText : syncText,
            HandshakePhase.Switch => elapsed >= 620 ? completeText : PauseText,
            _ => "",
        };
        return new(phase, t, Math.Clamp((t - 1000) / 4020, 0, 1), status, tick, reduce(), UsageLight.Color(outgoingLeft, dark), UsageLight.Color(incomingLeft, dark));
    }
    public HandshakeMotionPlan Plan(double width, double height, bool dark)
    {
        RefreshAccounts(); // compatible inspection/geometry API; the frame renderer uses ReadFrame instead.
        var f = ReadFrame(dark);
        return new(f.Phase, f.VirtualMilliseconds, f.Progress, f.Status, f.ShowTick, f.UseCrossfade, f.OutgoingColor, f.IncomingColor,
            HandshakeGeometry.Draw(width, height, f.VirtualMilliseconds, dark, f.UseCrossfade, f.OutgoingColor, f.IncomingColor));
    }
}

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
    private readonly List<HandshakeTone> sounds=[];
    private string? from;
    private string? to;
    private bool closed;
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
    private static double Minimum(HandshakePhase p)=>p switch {HandshakePhase.Standby=>600,HandshakePhase.Linking=>1000,HandshakePhase.InSync=>800,_=>620};
    private void Observe(object? sender,HandshakeEvent e)
    {
        if(e.ThreadId!=thread)return;
        if(e.To==HandshakePhase.Idle&&stages.LastOrDefault()?.Phase==HandshakePhase.Switch){closed=true;return;}
        if(Rank(e.To)==0){stages.Clear();sounds.Clear();closed=false;Changed?.Invoke();return;}
        if(e.To==HandshakePhase.Standby&&closed){stages.Clear();sounds.Clear();closed=false;}
        var last=stages.LastOrDefault();var rank=Rank(e.To);
        if(last is not null&&rank<Rank(last.Phase))return;
        from=e.OutgoingAccountId??from;to=e.IncomingAccountId??to;
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
                if(SoundEnabled&&stage.Phase==HandshakePhase.Linking) {sounds.Add(new(523.25,1.1,.03,0,.04,.0001,.05));sounds.Add(new(783.99,1.3,.022,.08,.04,.0001,.05));}
            }
            if(stage.Ready&&stage.Readiness!=HandshakeReadiness.Deferred&&!stage.TickPlayed)
            {
                stage.TickPlayed=true;if(SoundEnabled)sounds.Add(new(1318.5,.18,.025,0,.04,.0001,.05));
            }
        }
        return shown;
    }
    public IReadOnlyList<HandshakeTone> TakeSounds(){Pump();var result=sounds.ToArray();sounds.Clear();return result;}
    public HandshakeMotionPlan Plan(double width,double height,bool dark)
    {
        var stage=Pump();var elapsed=stage is null?0:Math.Max(0,(clock.GetUtcNow()-stage.Starts).TotalMilliseconds);
        var phase=stage?.Phase??HandshakePhase.Idle;
        var t=phase switch {HandshakePhase.Standby=>1000+Math.Min(999,elapsed),HandshakePhase.Linking=>2000+Math.Min(1000,elapsed),HandshakePhase.InSync=>3000+Math.Min(1399,elapsed),HandshakePhase.Switch=>4400+Math.Min(1300,elapsed),_=>0};
        var rows=accounts();var outgoing=rows.FirstOrDefault(a=>a.Id.ToString("D")==from);var incoming=rows.FirstOrDefault(a=>a.Id.ToString("D")==to);
        double Left(ThreadAccount? a)=>a is null||a.NoLimit?1:Math.Clamp(1-(a.NoFiveHour?a.Usage?.Week:a.Usage?.FiveHour??a.Usage?.Week??0).GetValueOrDefault(),0,1);
        var c1=UsageLight.Color(Left(outgoing),dark);var c2=UsageLight.Color(Left(incoming),dark);var name=incoming?.Name??"";var old=outgoing?.Name??"";
        var deferred=stage?.Readiness==HandshakeReadiness.Deferred;
        var tick=stages.Any(s=>s.Starts<=clock.GetUtcNow()&&s.Ready&&s.Readiness!=HandshakeReadiness.Deferred)&&!(phase==HandshakePhase.Switch&&elapsed>=1820);
        var status=deferred&&phase is HandshakePhase.InSync or HandshakePhase.Switch?"Continued on "+name+" · What was handed over":phase switch
        {
            HandshakePhase.Standby=>"Standby · "+name+" warms up in the background",
            HandshakePhase.Linking=>"Linking · "+old+" briefs "+name,
            HandshakePhase.InSync when elapsed>=800=>"Safe pause · the switch waits for the edit to finish",
            HandshakePhase.InSync=>"In sync · "+name+(stage?.Readiness==HandshakeReadiness.Injected?"":" read the plan back; Marrow checked it"),
            HandshakePhase.Switch=>elapsed>=620?"Handshake complete · "+name+" is in sync":"Safe pause · the switch waits for the edit to finish",
            _=>"",
        };
        return new(phase,t,Math.Clamp((t-1000)/4020,0,1),status,tick,reduce(),c1,c2,HandshakeGeometry.Draw(width,height,t,dark,reduce(),c1,c2));
    }
}

using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Handshake;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Testing;
using Marrow.Testing.MultiAccountSettings;
using Xunit;
namespace Marrow.Core.Tests.Port.MultiAccount.Handshake;
// PORT-ONLY: page :3039, :3053-3066, :3374-3394; OD-0116/0121/0122.
public sealed class HandshakeMotionModelTests
{
    [Fact, Trait("Rule","R-TEST-4")]
    public void Real_events_drive_phases_minimum_visibility_and_the_same_usage_light()
    {
        var rig=new AccountsRig();var a=rig.Connect(ProviderKind.Claude,"Max");var b=rig.ConnectAnother(ProviderKind.Claude,"Max");
        rig.Usage[a.Id]=new(.96,null,.5,null,false,false);rig.Usage[b.Id]=new(.12,null,.2,null,false,false);
        var accounts=new FakeThreadAccountPort(rig,ProviderKind.Claude);var port=new FakeCardPort();var clock=new ManualTimeProvider();var thread=Guid.NewGuid();
        using var model=new HandshakeMotionModel(thread,port,()=>accounts.Accounts,clock);
        HandshakeEvent Ev(HandshakePhase phase,HandshakeEventKind kind=HandshakeEventKind.PhaseChanged)=>new(thread,kind,HandshakePhase.Idle,phase,a.Id.ToString(),b.Id.ToString(),HandshakeReason.NearLimit,clock.GetUtcNow());
        Assert.Equal(HandshakePhase.Idle,model.Plan(420,160,true).Phase);
        port.Raise(Ev(HandshakePhase.Standby));var plan=model.Plan(420,160,true);
        Assert.Equal("Standby · Claude Max 2 warms up in the background",plan.Status);Assert.Equal(UsageLight.Color(.04,true),plan.OutgoingColor);Assert.Equal(UsageLight.Color(.88,true),plan.IncomingColor);
        clock.Advance(TimeSpan.FromSeconds(20));Assert.Equal(HandshakePhase.Standby,model.Plan(420,160,true).Phase);
        port.Raise(Ev(HandshakePhase.Linking));Assert.Equal("Linking · Claude Max 1 briefs Claude Max 2",model.Plan(420,160,true).Status);
        port.Raise(Ev(HandshakePhase.InSync,HandshakeEventKind.InSyncReady));port.Raise(Ev(HandshakePhase.Switch));
        clock.Advance(TimeSpan.FromMilliseconds(999));Assert.Equal(HandshakePhase.Linking,model.Plan(420,160,true).Phase);
        clock.Advance(TimeSpan.FromMilliseconds(1));Assert.Equal(HandshakePhase.InSync,model.Plan(420,160,true).Phase);
        Assert.Equal("In sync · Claude Max 2 read the plan back; nth checked it",model.Plan(420,160,true).Status);
        clock.Advance(TimeSpan.FromMilliseconds(800));
        // Retired the 800ms Assert.Equal(HandshakePhase.Switch,model.Plan(420,160,true).Phase): ED-512.
        // HTML3039 / MAUI-052 keeps In sync for 1400ms. Check the old following behavior at the actual boundary.
        clock.Advance(TimeSpan.FromMilliseconds(600));Assert.Equal(HandshakePhase.Switch,model.Plan(420,160,true).Phase);
        clock.Advance(TimeSpan.FromMilliseconds(620));Assert.Equal("Handshake complete · Claude Max 2 is in sync",model.Plan(420,160,true).Status);
        Assert.Equal(1,model.Plan(420,160,true).Progress);
        port.Raise(Ev(HandshakePhase.Linking));Assert.Equal(HandshakePhase.Switch,model.Plan(420,160,true).Phase);
    }
    [Theory, Trait("Rule","R-TEST-4")]
    [InlineData(HandshakeReadiness.Primed,true)] [InlineData(HandshakeReadiness.Injected,true)] [InlineData(HandshakeReadiness.Deferred,false)]
    public void Sound_recipe_and_ready_tick_are_once_per_real_phase_and_never_for_deferred_history(HandshakeReadiness readiness,bool tick)
    {
        var rig=new AccountsRig();var a=rig.Connect(ProviderKind.Claude);var b=rig.ConnectAnother(ProviderKind.Claude);var accounts=new FakeThreadAccountPort(rig,ProviderKind.Claude);
        var port=new FakeCardPort();var clock=new ManualTimeProvider();var thread=Guid.NewGuid();
        using var model=new HandshakeMotionModel(thread,port,()=>accounts.Accounts,clock,readiness:_=>readiness);model.SoundEnabled=true;
        HandshakeEvent Ev(HandshakePhase phase,HandshakeEventKind kind=HandshakeEventKind.PhaseChanged)=>new(thread,kind,HandshakePhase.Idle,phase,a.Id.ToString(),b.Id.ToString(),HandshakeReason.NearLimit,clock.GetUtcNow());
        port.Raise(Ev(HandshakePhase.Standby));port.Raise(Ev(HandshakePhase.Linking));Assert.Empty(model.TakeSounds());
        clock.Advance(TimeSpan.FromMilliseconds(600));var link=model.TakeSounds();
        // Retired Assert.Equal(2,link.Count) at600ms: ED-513/35/36, HTML3039 / MAUI-051.
        Assert.Empty(link); // No early tone; advance the remaining400ms before checking the unchanged sound recipe.
        clock.Advance(TimeSpan.FromMilliseconds(400));link=model.TakeSounds();Assert.Equal(2,link.Count);
        Assert.Equal(new HandshakeTone(523.25,1.1,.03,0,.04,.0001,.05),link[0]);Assert.Equal(new HandshakeTone(783.99,1.3,.022,.08,.04,.0001,.05),link[1]);
        port.Raise(Ev(HandshakePhase.InSync,HandshakeEventKind.InSyncReady));clock.Advance(TimeSpan.FromMilliseconds(1000));
        var tones=model.TakeSounds();Assert.Equal(tick?1:0,tones.Count);Assert.Equal(tick,model.Plan(420,160,true).ShowTick);
        if(tick)Assert.Equal(new HandshakeTone(1318.5,.18,.025,0,.04,.0001,.05),tones[0]);
        port.Raise(Ev(HandshakePhase.InSync,HandshakeEventKind.InSyncReady));Assert.Empty(model.TakeSounds());
        if(readiness==HandshakeReadiness.Injected) Assert.Equal("In sync · Claude 2",model.Plan(420,160,true).Status);
        if(readiness==HandshakeReadiness.Deferred) Assert.DoesNotContain("In sync",model.Plan(420,160,true).Status,StringComparison.Ordinal);
    }
    [Fact, Trait("Rule","R-TEST-4")]
    public void Reduce_Motion_crossfades_cancellation_clears_the_plan_and_disposal_stops_listening()
    {
        var rig=new AccountsRig();var a=rig.Connect(ProviderKind.Claude);var b=rig.ConnectAnother(ProviderKind.Claude);var accounts=new FakeThreadAccountPort(rig,ProviderKind.Claude);var port=new FakeCardPort();var clock=new ManualTimeProvider();var thread=Guid.NewGuid();
        var model=new HandshakeMotionModel(thread,port,()=>accounts.Accounts,clock,reduceMotion:()=>true);
        var ev=new HandshakeEvent(thread,HandshakeEventKind.PhaseChanged,HandshakePhase.Idle,HandshakePhase.Standby,a.Id.ToString(),b.Id.ToString(),HandshakeReason.NearLimit,clock.GetUtcNow());port.Raise(ev);Assert.True(model.Plan(420,160,true).UseCrossfade);Assert.False(model.SoundEnabled);
        port.Raise(ev with {To=HandshakePhase.Waiting});Assert.Equal(HandshakePhase.Idle,model.Plan(420,160,true).Phase);Assert.Empty(model.TakeSounds());
        model.Dispose();port.Raise(ev);Assert.Equal(HandshakePhase.Idle,model.Plan(420,160,true).Phase);
    }
}

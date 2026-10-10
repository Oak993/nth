using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Cards;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Testing.MultiAccountSettings;
using Xunit;
namespace Marrow.Core.Tests.Port.MultiAccount.Cards;
// PORT-ONLY: cards :3999-4100, :4224-4226, :4698; OD-0116 equal first choices.
public sealed class LimitCardModelTests
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task First_time_is_one_card_with_three_equal_unselected_choices_and_one_remember_line()
    {
        var rig=new AccountsRig(); var a=rig.Connect(ProviderKind.Claude,"Max"); var b=rig.ConnectAnother(ProviderKind.Claude,"Max");
        var accounts=new FakeThreadAccountPort(rig,ProviderKind.Claude); var port=new FakeCardPort();
        var request=new DecisionRequest(Guid.NewGuid(),[DecisionKind.OnLimit,DecisionKind.Bring,DecisionKind.MidReply],true,a.Id.ToString(),b.Id.ToString(),null);
        var ev=new HandshakeEvent(request.ThreadId,HandshakeEventKind.DecisionRequested,HandshakePhase.Idle,HandshakePhase.Asking,request.OutgoingAccountId,request.IncomingAccountId,HandshakeReason.HardLimit,DateTimeOffset.UnixEpoch,Request:request); port.Raise(ev);
        var model=new LimitCardModel(ev,accounts.Accounts,port);
        Assert.Equal(CardKind.FirstTime,model.Kind); Assert.Equal("Claude Max 1 ran out. How should nth handle this?",model.Title);
        Assert.Equal(new[]{"Keep me going","Ask me each time","Just wait"},model.Buttons.Select(b=>b.Text));
        Assert.All(model.Buttons,b=>{ Assert.False(b.Primary); Assert.False(b.Selected); });
        Assert.Null(model.SelectedBring); Assert.Empty(port.Answers); Assert.Equal(RememberScope.JustThisTime,model.Scope);
        Assert.Equal(new[]{"Just this time","This thread","All threads"},model.RememberOptions);
        Assert.True(model.ShowBring); Assert.Equal("Remember:",model.RememberLabel);
        await model.AnswerAsync("switch");
        Assert.Equal(new[]{new Choice(DecisionKind.OnLimit,DecisionValues.Switch)},port.Answers.Single().Choices);
        Assert.True(model.Answered); await model.AnswerAsync("wait"); Assert.Single(port.Answers);
        Assert.False(rig.Handshake.Enabled);
    }
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData("ask","ask")] [InlineData("wait","wait")]
    public async Task First_time_ask_each_and_wait_are_not_replaced_by_automatic_switching(string button,string value)
    {
        var rig=new AccountsRig(); var a=rig.Connect(ProviderKind.Claude); var port=new FakeCardPort(); var thread=Guid.NewGuid();
        var request=new DecisionRequest(thread,[DecisionKind.OnLimit,DecisionKind.Bring],true,a.Id.ToString(),null,null);
        var ev=new HandshakeEvent(thread,HandshakeEventKind.DecisionRequested,HandshakePhase.Idle,HandshakePhase.Asking,a.Id.ToString(),null,HandshakeReason.HardLimit,DateTimeOffset.UnixEpoch,Request:request);port.Raise(ev);
        var model=new LimitCardModel(ev,new FakeThreadAccountPort(rig,ProviderKind.Claude).Accounts,port);
        await model.AnswerAsync(button); Assert.Equal(value,port.Answers.Single().Choices.Single().Value);
    }
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(DecisionKind.OnLimit,CardKind.Limit,"Claude Max 1 reached its 5-hour limit · resets 3:40 PM","Switch to Claude Max 2","Wait")]
    [InlineData(DecisionKind.OtherService,CardKind.OtherService,"All your Claude accounts are out. The first is back at 3:40 PM.","Wait for Claude","Use Codex for now")]
    [InlineData(DecisionKind.MidReply,CardKind.MidReply,"Claude Max 1 reached its 5-hour limit while writing this reply · resets 3:40 PM","Finish now on Claude Max 2","Stop, use it from my next message")]
    [InlineData(DecisionKind.EarlierAccount,CardKind.EarlierAccount,"Claude Max 2 has reset · you are on Claude Max 1","Go back to Claude Max 2","Stay on Claude Max 1")]
    public void Card_titles_buttons_and_unresolved_bring_follow_the_page(DecisionKind kind,CardKind expected,string title,string first,string second)
    {
        var rig=new AccountsRig(); var a=rig.Connect(ProviderKind.Claude,"Max"); var b=rig.ConnectAnother(ProviderKind.Claude,"Max"); var x=rig.Connect(ProviderKind.Codex);
        var ca=new FakeThreadAccountPort(rig,ProviderKind.Claude); var cx=new FakeThreadAccountPort(rig,ProviderKind.Codex);var dest=kind==DecisionKind.OtherService?x.Id:b.Id;
        var request=new DecisionRequest(Guid.NewGuid(),[kind,DecisionKind.Bring],false,a.Id.ToString(),dest.ToString(),new(2026,10,7,15,40,0,TimeSpan.Zero));
        var ev=new HandshakeEvent(request.ThreadId,HandshakeEventKind.DecisionRequested,HandshakePhase.Idle,HandshakePhase.Asking,request.OutgoingAccountId,request.IncomingAccountId,kind==DecisionKind.EarlierAccount?HandshakeReason.EarlierAccountBack:HandshakeReason.HardLimit,DateTimeOffset.UnixEpoch,Request:request);
        var model=new LimitCardModel(ev,[..ca.Accounts,..cx.Accounts],new FakeCardPort(),TimeZoneInfo.Utc);
        Assert.Equal(expected,model.Kind);Assert.Equal(title,model.Title); Assert.Equal(new[]{first,second},model.Buttons.Select(b=>b.Text));Assert.True(model.ShowBring);
        Assert.Equal(Bring.MarrowSummary,model.SelectedBring);
        if(kind==DecisionKind.OtherService) Assert.Equal("Codex is another service: it will see this conversation.",model.Explanation);
        if(kind==DecisionKind.MidReply) Assert.Equal("Either way, the next account reads the whole conversation once before going on, which uses a little of its limit. Finish now picks up from the last finished step (a half-done step may run again). Stop waits for your next message.",model.Explanation);
    }
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(RememberScope.JustThisTime)] [InlineData(RememberScope.ThisThread)] [InlineData(RememberScope.AllThreads)]
    public async Task Bring_scope_and_notes_map_to_the_card_seam_once(RememberScope scope)
    {
        var rig=new AccountsRig();var a=rig.Connect(ProviderKind.Claude,"Max");var b=rig.ConnectAnother(ProviderKind.Claude,"Max");var ca=new FakeThreadAccountPort(rig,ProviderKind.Claude);var port=new FakeCardPort();
        var req=new DecisionRequest(Guid.NewGuid(),[DecisionKind.OnLimit,DecisionKind.Bring],false,a.Id.ToString(),b.Id.ToString(),new(2026,10,7,15,40,0,TimeSpan.Zero));
        var ev=new HandshakeEvent(req.ThreadId,HandshakeEventKind.DecisionRequested,HandshakePhase.Idle,HandshakePhase.Asking,req.OutgoingAccountId,req.IncomingAccountId,HandshakeReason.HardLimit,DateTimeOffset.UnixEpoch,Request:req);port.Raise(ev);
        var model=new LimitCardModel(ev,ca.Accounts,port,TimeZoneInfo.Utc);model.Scope=scope;model.Select(DecisionKind.Bring,DecisionValues.Full);
        await model.AnswerAsync("switch");Assert.Equal(scope,port.Answers.Single().Scope);Assert.Contains(new Choice(DecisionKind.Bring,DecisionValues.Full),port.Answers.Single().Choices);
        Assert.Equal("Continued on Claude Max 2 · Full conversation"+(scope==RememberScope.JustThisTime?"":scope==RememberScope.ThisThread?" (this thread’s choice)":" (your Settings)")+" · What was handed over",model.AfterNote);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Concurrent_clicks_cannot_answer_twice_and_wait_has_the_page_note()
    {
        var rig=new AccountsRig();var a=rig.Connect(ProviderKind.Claude);var ca=new FakeThreadAccountPort(rig,ProviderKind.Claude);var port=new FakeCardPort();
        var req=new DecisionRequest(Guid.NewGuid(),[DecisionKind.OnLimit],false,a.Id.ToString(),null,new(2026,10,7,15,40,0,TimeSpan.Zero));
        var ev=new HandshakeEvent(req.ThreadId,HandshakeEventKind.DecisionRequested,HandshakePhase.Idle,HandshakePhase.Asking,req.OutgoingAccountId,null,HandshakeReason.HardLimit,DateTimeOffset.UnixEpoch,Request:req);port.Raise(ev);
        var hold=new TaskCompletionSource();port.AnswerHold=hold.Task;var model=new LimitCardModel(ev,ca.Accounts,port,TimeZoneInfo.Utc);
        var first=model.AnswerAsync("wait");await model.AnswerAsync("wait");Assert.True(model.Answering);Assert.Empty(port.Answers);hold.SetResult();await first;Assert.Single(port.Answers);
        Assert.Equal("Waiting for Claude · resets 3:40 PM · your message sends then · Switch now",model.AfterNote);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Every_nonempty_unresolved_kind_subset_has_only_its_unresolved_option_groups()
    {
        var rig=new AccountsRig();var a=rig.Connect(ProviderKind.Claude);var b=rig.ConnectAnother(ProviderKind.Claude);var ca=new FakeThreadAccountPort(rig,ProviderKind.Claude);
        for(var mask=1;mask<32;mask++) foreach(var first in new[]{false,true})
        {
            var kinds=Enum.GetValues<DecisionKind>().Where(k=>(mask&(1<<(int)k))!=0).ToArray();var req=new DecisionRequest(Guid.NewGuid(),kinds,first,a.Id.ToString(),b.Id.ToString(),null);
            var ev=new HandshakeEvent(req.ThreadId,HandshakeEventKind.DecisionRequested,HandshakePhase.Idle,HandshakePhase.Asking,req.OutgoingAccountId,req.IncomingAccountId,HandshakeReason.HardLimit,DateTimeOffset.UnixEpoch,Request:req);
            var model=new LimitCardModel(ev,ca.Accounts,new FakeCardPort());Assert.Equal(kinds.Contains(DecisionKind.Bring),model.ShowBring);
            Assert.All(model.Groups,g=>Assert.Contains(g.Kind,kinds)); Assert.False(model.Answered);
        }
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task An_unanswered_permission_or_stale_card_never_reaches_the_answer_port()
    {
        var rig=new AccountsRig();var a=rig.Connect(ProviderKind.Claude);var b=rig.ConnectAnother(ProviderKind.Claude);var ca=new FakeThreadAccountPort(rig,ProviderKind.Claude);var port=new FakeCardPort();
        var req=new DecisionRequest(Guid.NewGuid(),[DecisionKind.OnLimit,DecisionKind.MidReply],false,a.Id.ToString(),b.Id.ToString(),null);
        var ev=new HandshakeEvent(req.ThreadId,HandshakeEventKind.DecisionRequested,HandshakePhase.Idle,HandshakePhase.Asking,req.OutgoingAccountId,req.IncomingAccountId,HandshakeReason.HardLimit,DateTimeOffset.UnixEpoch,Request:req);port.Raise(ev);
        var model=new LimitCardModel(ev,ca.Accounts,port);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>model.AnswerAsync("finish"));Assert.Empty(port.Answers);
        model.Select(DecisionKind.OnLimit,DecisionValues.Switch);
        port.Raise(ev with { Request=req with { Kinds=[DecisionKind.OnLimit] } });
        await Assert.ThrowsAsync<InvalidOperationException>(()=>model.AnswerAsync("finish"));Assert.Empty(port.Answers);
    }
}

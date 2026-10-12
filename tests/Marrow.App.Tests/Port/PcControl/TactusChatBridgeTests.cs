using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus, the chat mirror (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.1 "Resume after the turn ended", 10.2 path 6,
// 10.4 "Resolution", 10.7). Rows go to their chat; a resume continues the chat only when the chat's own four conditions
// hold, else it says "Resumed. Ask to continue." or shows the resume row while the turn runs; the chat's asks follow the
// broker, a badge's answer and a sheet's answer resolve the same entry from the chat; the chat's Stop and the user writing
// reach the engine; a chat with Tactus in use hands its provider's asks to the broker and gets the pill's answer back.
[Trait("Decision", "ED-NEW-pcctl-27")]
public sealed class TactusChatBridgeTests
{
    private const string Excel = "exe:c:\\office\\excel.exe";

    private static TactusChatRig Make(bool continues = true)
    {
        var clock = new PcManualClock();
        var control = new PcRunControl();
        var pause = new PcPauseControl(clock, control);
        var stop = new PcStopControl(control);
        var broker = new PcAskBroker(clock, control);
        var feed = new PcChatFeed(pause, stop, broker, clock);
        var chats = new TactusFakeChats();
        var bridge = new TactusChatBridge(feed, broker, new PillAskBridge(broker), chats, () => continues);
        return new TactusChatRig(clock, control, pause, stop, broker, feed, chats, bridge);
    }

    [Fact]
    public void A_resume_continues_only_when_the_chat_says_so_and_rows_go_to_their_chat()
    {
        using var rig = Make();
        var chat = rig.Chats.Add("chat-1");
        var other = rig.Chats.Add("chat-2");
        using var joined = rig.Pause.Join("chat-1", Excel, new PcWaitState());

        rig.Pause.Pause(PcScope.App(Excel), PcPauseSource.Row);
        Assert.Equal(new[] { "Paused Excel (from the pill)" }, chat.Rows);
        Assert.Empty(other.Rows);

        // the turn still runs: the resume row
        rig.Pause.Resume(PcScope.App(Excel), PcPauseSource.Chat);
        Assert.Equal("Resumed (from the chat)", chat.Rows[^1]);
        Assert.True(chat.AskedToContinue);

        // the turn ended and all four hold: the fixed message as a chip, no row
        rig.Pause.Pause(PcScope.App(Excel), PcPauseSource.Row);
        chat.Outcome = PcResumeOutcome.Continue;
        var rows = chat.Rows.Count;
        rig.Pause.Resume(PcScope.All, PcPauseSource.Tray);
        Assert.Equal(new[] { ("Tactus was resumed in Excel. Continue the task.", "Resumed from the tray") }, chat.Continued);
        Assert.Equal(rows, chat.Rows.Count);

        // one condition fails: the chat asks the user to continue
        rig.Pause.Pause(PcScope.App(Excel), PcPauseSource.Row);
        chat.Outcome = PcResumeOutcome.AskToContinue;
        rig.Pause.Resume(PcScope.App(Excel), PcPauseSource.Row);
        Assert.Equal("Resumed. Ask to continue.", chat.Rows[^1]);
        Assert.Single(chat.Continued);
    }

    [Fact]
    public async Task The_chats_asks_follow_the_broker_and_an_answer_in_the_chat_resolves_the_same_entry()
    {
        using var rig = Make();
        var chat = rig.Chats.Add("chat-1");
        var approval = rig.Broker.Add(new PcAskDraft(PcAskKind.Approval, "chat-1", new PcApprovalAsk(PcCapability.Act, false, true)) { App = Excel, AppName = "Excel" });
        var question = rig.Broker.Add(new PcAskDraft(PcAskKind.Question, "chat-1", new PcAskRequest("Which sheet?", ["Q1", "Q2"], true, null)));
        Assert.Equal(new[] { approval.Id, question.Id }, chat.Shown.Select(a => a.Id));

        // the badge's popover answers with the pill's own body, from the chat
        Assert.IsType<PillApprovalBody>(rig.Bridge.Body(approval.Id));
        Assert.Equal(PcResolve.Done, rig.Bridge.Answer(new PillCardAnswer(approval.Id, 0, PillCardChoice.AllowChat)));
        var outcome = (await approval.Outcome);
        Assert.Equal(PcAnswerSource.Chat, outcome.Source);
        Assert.Equal("You allowed Excel for this chat", chat.Rows[^1]);
        Assert.Equal(new[] { question.Id }, chat.Shown.Select(a => a.Id));

        // the question sheet's answer resolves it; an answer for another chat's ask does nothing
        rig.Chats.Answer("chat-2", question.Id, PcAnswer.Chose("Q1"));
        Assert.NotNull(rig.Broker.Find(question.Id));
        rig.Chats.Answer("chat-1", question.Id, PcAnswer.Chose("Q2"));
        Assert.Equal(PcAnswer.Chose("Q2"), (await question.Outcome).Answer);
        Assert.Equal("You answered in the chat: Q2", chat.Rows[^1]);
        Assert.Empty(chat.Shown);
    }

    [Fact]
    public async Task The_chats_Stop_and_the_user_writing_reach_the_engine()
    {
        using var rig = Make();
        var chat = rig.Chats.Add("chat-1");
        using var joined = rig.Pause.Join("chat-1", Excel, new PcWaitState());
        var question = rig.Broker.Add(new PcAskDraft(PcAskKind.Question, "chat-1", new PcAskRequest("Which sheet?", [], true, null)));

        rig.Chats.Write("chat-1");
        Assert.Equal(PcWithdrawal.MovedOn, (await question.Outcome).Reason);

        using var watch = rig.Control.Watch(CancellationToken.None, "chat-1", Excel, 1);
        rig.Chats.Interrupt("chat-1");
        Assert.True(watch.Token.IsCancellationRequested);
        Assert.Equal("Stopped (from the chat)", chat.Rows[^1]);
    }

    [Fact]
    public async Task A_chat_with_tactus_in_use_hands_its_providers_asks_to_the_broker_and_gets_the_pills_answer_back()
    {
        using var rig = Make();
        var chat = rig.Chats.Add("chat-1");
        chat.Questions.Add(new QuestionRequest("p1", new ValueList<QuestionRequest.Question>([
            new QuestionRequest.Question("q", "Branch", "Which branch?", new ValueList<QuestionRequest.Choice>([new("main"), new("dev")]), false, true, false)])));
        chat.Approvals.Add(new ApprovalRequest("r1", ApprovalRequest.Kind.Command, "npm test", ValueList<ApprovalRequest.Option>.Empty));
        rig.Bridge.Refresh();
        // no Tactus in use: nothing goes to the broker
        Assert.Empty(rig.Broker.Pending);

        using var joined = rig.Pause.Join("chat-1", Excel, new PcWaitState());
        var asks = rig.Broker.Pending;
        Assert.Equal(new[] { PcAskKind.ProviderQuestion, PcAskKind.ProviderApproval }, asks.Select(a => a.Kind));
        Assert.False(asks[0].ChatOnly);
        Assert.True(asks[1].ChatOnly);
        Assert.Equal(new[] { "main", "dev" }, ((PcProviderAsk)asks[0].Payload).Choices);

        // the pill answers the question: it goes back through the runtime's answer path
        Assert.Equal(PcResolve.Done, rig.Broker.Resolve(asks[0].Id, 0, PcAnswer.Chose("dev"), PcAnswerSource.Pill));
        Assert.Equal(new[] { ("p1", PcAnswer.Chose("dev")) }, chat.Answered);

        // the approval answered in the chat: its broker entry ends, answered from the chat, with no second row
        var rows = chat.Rows.Count;
        chat.Approvals.Clear();
        chat.Changed();
        Assert.Equal(PcAnswerSource.Chat, (await asks[1].Outcome).Source);
        Assert.Empty(rig.Broker.Pending);
        Assert.Equal(rows, chat.Rows.Count);
    }
}

using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, the chat mirror (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.1, 10.2, 10.4 "Resolution", 10.7). A Pause
// or Resume reaches each chat that used one of its apps and no other; a stop reaches each chat its scope covers that had
// Tactus in use, and the chat's own Stop right after adds no second row; an answer reaches the chat that asked; the strip
// shows the chat's apps and their states, pauses and resumes that chat's apps, and counts its Tactus asks.
[Trait("Decision", "ED-NEW-pcctl-27")]
public sealed class PcChatFeedTests
{
    private const string Excel = "exe:c:\\office\\excel.exe";
    private const string Outlook = "exe:c:\\office\\outlook.exe";

    [Fact]
    public void A_pause_and_a_resume_reach_each_chat_that_used_the_app_and_no_other()
    {
        var clock = new PcFakeClock();
        var control = new PcRunControl();
        using var pause = new PcPauseControl(clock, control);
        using var asks = new PcAskBroker(clock, control);
        using var feed = new PcChatFeed(pause, new PcStopControl(control), asks, clock, pauseChord: () => "Ctrl+Alt+Space");
        var rows = new List<PcChatRow>();
        var resumes = new List<PcChatResume>();
        feed.Row += rows.Add;
        feed.Resumed += resumes.Add;
        using var one = pause.Join("chat-1", Excel, new PcWaitState());
        using var two = pause.Join("chat-1", Outlook, new PcWaitState());
        using var other = pause.Join("chat-2", Outlook, new PcWaitState());

        pause.Pause(PcScope.App(Excel), PcPauseSource.Row);
        Assert.Equal(new[] { new PcChatRow("chat-1", PcChatRowKind.Paused, "Paused Excel (from the pill)") }, rows);

        rows.Clear();
        pause.Pause(PcScope.All, PcPauseSource.Chord);
        // Pause all: Excel was paused already, so only Outlook moves, in both chats that use it
        Assert.Equal(new[]
        {
            new PcChatRow("chat-1", PcChatRowKind.Paused, "Paused Outlook (Ctrl+Alt+Space)"),
            new PcChatRow("chat-2", PcChatRowKind.Paused, "Paused Outlook (Ctrl+Alt+Space)"),
        }, rows.OrderBy(r => r.Thread, StringComparer.Ordinal));

        pause.Resume(PcScope.All, PcPauseSource.Chord);
        Assert.Equal(new[] { "chat-1", "chat-2" }, resumes.Select(r => r.Thread).Order());
        var first = resumes.Single(r => r.Thread == "chat-1");
        Assert.Equal("Resumed (Ctrl+Alt+Space)", first.Row);
        Assert.Equal("Resumed with Ctrl+Alt+Space", first.Chip);
        Assert.Equal("Tactus was resumed in Excel and Outlook. Continue the task.", first.Message);
        Assert.Equal("Tactus was resumed in Outlook. Continue the task.", resumes.Single(r => r.Thread == "chat-2").Message);
    }

    [Fact]
    public void A_stop_writes_one_row_in_each_chat_it_reached()
    {
        var clock = new PcFakeClock { NowMs = 1_000 };
        var control = new PcRunControl();
        using var pause = new PcPauseControl(clock, control);
        using var asks = new PcAskBroker(clock, control);
        var stop = new PcStopControl(control);
        using var feed = new PcChatFeed(pause, stop, asks, clock, stopChord: () => "Ctrl+Alt+Esc");
        var rows = new List<PcChatRow>();
        feed.Row += rows.Add;
        using var one = pause.Join("chat-1", Excel, new PcWaitState());
        using var other = pause.Join("chat-2", Outlook, new PcWaitState());

        // the pill's Stop reaches both chats; the turns it interrupts come back as each chat's Stop: no second row
        stop.PillStop();
        feed.ChatStop("chat-1");
        Assert.Equal(new[] { ("chat-1", "Stopped (from the pill)"), ("chat-2", "Stopped (from the pill)") }, rows.Select(r => (r.Thread, r.Text)).Order());
        Assert.All(rows, r => Assert.Equal(PcChatRowKind.Stopped, r.Kind));

        // later, the chat's own Stop has its row; a chat with no Tactus in use has none
        rows.Clear();
        clock.NowMs += PcChatFeed.StopEchoMs;
        feed.ChatStop("chat-1");
        feed.ChatStop("chat-3");
        Assert.Equal(new[] { new PcChatRow("chat-1", PcChatRowKind.Stopped, "Stopped (from the chat)") }, rows);

        rows.Clear();
        feed.StopApp(Outlook);
        stop.StopKey();
        Assert.Equal(new[] { "Stopped Outlook (from the chat)", "Stopped (Ctrl+Alt+Esc)", "Stopped (Ctrl+Alt+Esc)" }, rows.Select(r => r.Text));
    }

    [Fact]
    public void An_answer_reaches_the_chat_that_asked()
    {
        var clock = new PcManualClock(); // asks expire on a clock that waits
        var control = new PcRunControl();
        using var pause = new PcPauseControl(clock, control);
        using var asks = new PcAskBroker(clock, control);
        using var feed = new PcChatFeed(pause, new PcStopControl(control), asks, clock);
        var rows = new List<PcChatRow>();
        var changes = 0;
        feed.Row += rows.Add;
        feed.Changed += () => changes++;

        var approval = asks.Add(new PcAskDraft(PcAskKind.Approval, "chat-2", new PcApprovalAsk(PcCapability.Act, false, true)) { App = Outlook, AppName = "Outlook" });
        var question = asks.Add(new PcAskDraft(PcAskKind.Question, "chat-1", new PcAskRequest("Which sheet?", ["Q1", "Q2"], true, null)));
        Assert.Equal(2, changes);
        Assert.Equal(PcResolve.Done, asks.Resolve(approval.Id, 0, new PcApprovalReply(PcApproval.Chat, true), PcAnswerSource.Pill));
        Assert.True(asks.Dismiss(question.Id));

        Assert.Equal(new[]
        {
            new PcChatRow("chat-2", PcChatRowKind.Answered, "You allowed Outlook for this chat (from the pill)"),
            new PcChatRow("chat-1", PcChatRowKind.Answered, "You dismissed the question (from the pill)"),
        }, rows);
        Assert.Equal(4, changes);
    }

    [Fact]
    public void The_strip_shows_the_chats_apps_pauses_its_own_and_counts_its_tactus_asks()
    {
        var clock = new PcManualClock();
        var control = new PcRunControl();
        using var pause = new PcPauseControl(clock, control);
        using var asks = new PcAskBroker(clock, control);
        using var feed = new PcChatFeed(pause, new PcStopControl(control), asks, clock);
        Assert.Null(feed.Strip("chat-1"));

        using var one = pause.Join("chat-1", Excel, new PcWaitState());
        using var two = pause.Join("chat-1", Outlook, new PcWaitState());
        using var other = pause.Join("chat-2", Outlook, new PcWaitState());
        asks.Add(new PcAskDraft(PcAskKind.ShowWindow, "chat-1", new PcShowAsk("Book1 - Excel")) { App = Excel, AppName = "Excel" });
        asks.Add(new PcAskDraft(PcAskKind.ProviderApproval, "chat-1", new PcProviderAsk("Run a command", "npm test")));
        var strip = feed.Strip("chat-1")!;
        Assert.Equal(new[] { "Excel", "Outlook" }, strip.Apps.Select(a => a.Name).Order());
        Assert.Equal(1, strip.Asks);
        Assert.Equal("Pause", strip.Action);
        Assert.False(strip.Paused);

        // the strip's Pause holds that chat's apps; Outlook is paused for chat-2 as well (pause is per app)
        feed.Pause("chat-1");
        strip = feed.Strip("chat-1")!;
        Assert.True(strip.Paused);
        Assert.Equal("Resume", strip.Action);
        Assert.All(strip.Apps, a => Assert.Equal(PcStripAppState.Paused, a.State));
        Assert.True(pause.IsPaused(Outlook));
        using (pause.Acting(Excel))
        {
            Assert.Equal(PcStripAppState.Pausing, feed.Strip("chat-1")!.Apps.Single(a => a.Key == Excel).State);
        }

        feed.Resume("chat-1");
        Assert.False(pause.IsPaused(Excel));
        Assert.False(pause.IsPaused(Outlook));
        Assert.Equal("Tactus. Using Outlook", feed.Strip("chat-2")!.Line);

        // the turn ended: a chat with nothing paused has no strip
        pause.EndTurn("chat-2");
        Assert.Null(feed.Strip("chat-2"));
    }
}

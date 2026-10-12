using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-26; DESIGN.md 10.4 "Confirm body": "Reply instead" is "Cancel, and
// tell the model what to change"). The pill's confirm answer carries the user's note with Cancel; the model reads it once
// in the needs_confirmation result; the chat's bare answer still works.
[Trait("Decision", "ED-NEW-pcctl-26")]
public sealed class PcConfirmNoteTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";
    private const string ClickSend = "{\"window\":\"w:1a\",\"element\":3}";

    private static async Task<(PcEngineHarness Harness, PcControlEngine Engine)> Ready()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        app.Root.Children[1].Name = "Send";
        app.Root.Children[1].Shortcut = null;
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        harness.Confirmer.Answer = false;
        return (harness, engine);
    }

    [Fact]
    public async Task A_cancel_with_a_note_gives_the_model_the_note_once()
    {
        var (harness, engine) = await Ready();
        harness.Confirmer.Said = "Change the subject first";

        var click = await PcEngineHarness.Call(engine, "pc_click", ClickSend);

        Assert.Equal(PcErrorCode.NeedsConfirmation, click.Error);
        Assert.Equal("the user did not confirm " + PcWindowLine.Quote("Send") + ". The user said: Change the subject first", click.Reason);
        var again = await PcEngineHarness.Call(engine, "pc_click", ClickSend);
        Assert.Equal("the user did not confirm " + PcWindowLine.Quote("Send"), again.Reason);
    }

    [Fact]
    public async Task The_pills_reply_resolves_the_card_and_keeps_the_note_for_its_chat()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var confirmer = new PcBrokerConfirmer(broker);

        var asking = confirmer.Confirm(new PcAskSource("chat-1", "Outlook"), "send", "Send", "Outlook", CancellationToken.None);
        var ask = Assert.Single(broker.Pending);
        var reply = new PcConfirmReply(false, "Change the subject first");
        Assert.Equal("You cancelled Send in Outlook (from the pill)", AskLifetimes.Logged(ask, reply, PcAnswerSource.Pill));
        Assert.Equal(PcResolve.Done, broker.Resolve(ask.Id, 0, reply, PcAnswerSource.Pill));

        Assert.False(await asking.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(confirmer.Note("chat-2"));
        Assert.Equal("Change the subject first", confirmer.Note("chat-1"));
        Assert.Null(confirmer.Note("chat-1"));

        var confirming = confirmer.Confirm(new PcAskSource("chat-1", "Outlook"), "send", "Send", "Outlook", CancellationToken.None);
        Assert.Equal(PcResolve.Done, broker.Resolve(Assert.Single(broker.Pending).Id, 0, new PcConfirmReply(true), PcAnswerSource.Pill));
        Assert.True(await confirming.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task The_chats_bare_answer_still_works()
    {
        using var broker = new PcAskBroker(new PcManualClock());
        var confirmer = new PcBrokerConfirmer(broker);

        var asking = confirmer.Confirm(new PcAskSource("chat-1", "Outlook"), "send", "Send", "Outlook", CancellationToken.None);
        Assert.Equal(PcResolve.Done, broker.Resolve(Assert.Single(broker.Pending).Id, 0, true, PcAnswerSource.Chat));

        Assert.True(await asking.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(confirmer.Note("chat-1"));
    }
}

using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.3 "Talking to one directly"; ED-NEW-dextra-as16-6): "@Channel A, start the next video" goes straight to Channel A's home thread as
// the owner's own words; a name that fits two Asturs raises a "Which one?" question and the message goes to the one he picks; two names stay with Dextra; the
// Astur's home thread is made when it has none.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-6")]
public sealed class AsturTalkTests
{
    private static async Task<(AsturRig Rig, AssistantProfile A, AssistantProfile B, ChatThread Home, ChatThread HomeA, ChatThread HomeB)> Team()
    {
        var rig = await AsturRig.OpenAsync();
        var a = await rig.MakeAsync("Channel A", AsturShape.Squircle);
        var b = await rig.MakeAsync("Channel B", AsturShape.Hexagon);
        var homeA = rig.AddHomeChat(a);
        var homeB = rig.AddHomeChat(b);
        await rig.Team.SetHomeThreadAsync(a.Id, homeA.Id);
        await rig.Team.SetHomeThreadAsync(b.Id, homeB.Id);
        return (rig, rig.Team.Get(a.Id)!, rig.Team.Get(b.Id)!, rig.DextraHome(), homeA, homeB);
    }

    [Fact]
    public async Task A_typed_mention_sends_the_rest_to_that_Asturs_home_thread_as_the_owners_words()
    {
        var (rig, a, _, home, homeA, homeB) = await Team();
        using var disposeRig = rig;

        var talk = rig.Controller.Talk(home.Id, "@Channel A, start the next video");

        Assert.Equal(TalkKind.Sent, talk.Kind);
        Assert.Equal((a.Id, homeA.Id, "start the next video"), (talk.Target!.Id, talk.Thread, talk.Message));
        Assert.Equal(["start the next video"], rig.SentTo(homeA.Id));
        Assert.Empty(rig.SentTo(homeB.Id));
        Assert.Empty(rig.SentTo(home.Id));
    }

    [Fact]
    public async Task The_owners_words_start_a_fresh_untainted_turn_in_the_Asturs_thread()
    {
        var (rig, _, _, home, homeA, _) = await Team();
        using var disposeRig = rig;
        rig.Controller.AbsorbHome(homeA.Id, Taint.From(ContentOrigin.Web, "https://x.example"));
        Assert.True(rig.Controller.HomeTaint(homeA.Id).IsTainted);

        rig.Controller.Talk(home.Id, "@Channel A hello");

        Assert.False(rig.Controller.HomeTaint(homeA.Id).IsTainted);
    }

    [Fact]
    public async Task A_name_alone_opens_the_thread_and_sends_nothing()
    {
        var (rig, _, _, home, homeA, _) = await Team();
        using var disposeRig = rig;
        var talk = rig.Controller.Talk(home.Id, "@Channel A");
        Assert.Equal((TalkKind.Sent, homeA.Id, ""), (talk.Kind, talk.Thread, talk.Message));
        Assert.Empty(rig.SentTo(homeA.Id));
    }

    [Fact]
    public async Task A_spoken_name_first_routes_and_a_plain_sentence_does_not()
    {
        var (rig, _, _, home, homeA, _) = await Team();
        using var disposeRig = rig;
        Assert.Equal(TalkKind.Sent, rig.Controller.Talk(home.Id, "Hey Channel A, start the next video", spoken: true).Kind);
        Assert.Equal(["start the next video"], rig.SentTo(homeA.Id));
        var plain = rig.Controller.Talk(home.Id, "Start the next video on Channel A", spoken: true);
        Assert.Equal((TalkKind.Same, "Start the next video on Channel A"), (plain.Kind, plain.Message));
        var typed = rig.Controller.Talk(home.Id, "Channel A, start the next video");
        Assert.Equal(TalkKind.Same, typed.Kind);                                                // typed text needs the @
    }

    [Fact]
    public async Task A_name_that_fits_two_Asturs_asks_which_one_and_the_answer_sends_the_message_to_that_one()
    {
        var (rig, _, b, home, homeA, homeB) = await Team();
        using var disposeRig = rig;

        var talk = rig.Controller.Talk(home.Id, "@Channel start the next video");

        Assert.Equal(TalkKind.Asked, talk.Kind);
        var card = rig.Controller.Queue.Waiting.Single();
        Assert.Equal(card.Id, talk.Card);
        Assert.Equal(("Which one: Channel A or Channel B?", DecisionKind.Question), (card.Title, card.Kind));
        Assert.Equal(["Channel A", "Channel B"], card.Options);
        Assert.Equal([new DecisionInput("Your message", "start the next video")], card.Inputs);
        Assert.Equal([DecisionAnswerKind.Pick, DecisionAnswerKind.Discuss], card.Answers);
        Assert.Empty(rig.SentTo(homeA.Id));                                                     // nothing is guessed
        Assert.Empty(rig.SentTo(homeB.Id));

        Assert.Null(rig.Controller.Queue.Answer(card.Id, DecisionAnswer.Pick("Channel B")));

        Assert.Equal(["start the next video"], rig.SentTo(homeB.Id));
        Assert.Empty(rig.SentTo(homeA.Id));
        Assert.NotNull(b);
    }

    [Fact]
    public async Task Talking_instead_of_picking_sends_nothing_and_no_more_than_three_questions_wait_at_once()
    {
        var (rig, _, _, home, homeA, homeB) = await Team();
        using var disposeRig = rig;
        var first = rig.Controller.Talk(home.Id, "@Channel one");
        Assert.Null(rig.Controller.Queue.Answer(first.Card!.Value, DecisionAnswer.Discuss("never mind")));
        Assert.Empty(rig.SentTo(homeA.Id));
        Assert.Empty(rig.SentTo(homeB.Id));

        for (var n = 0; n < AssistantController.OpenWhichCards; n++) Assert.Equal(TalkKind.Asked, rig.Controller.Talk(home.Id, "@Channel " + n).Kind);
        var over = rig.Controller.Talk(home.Id, "@Channel again");
        Assert.Equal((TalkKind.Same, "Answer the open questions about which one first."), (over.Kind, over.Note));
        Assert.Equal(AssistantController.OpenWhichCards, rig.Controller.Queue.Waiting.Count);
    }

    [Fact]
    public async Task A_message_that_names_two_Asturs_stays_with_Dextra_which_sends_one_piece_to_each()
    {
        var (rig, _, _, home, homeA, homeB) = await Team();
        using var disposeRig = rig;
        var talk = rig.Controller.Talk(home.Id, "@Channel A and @Channel B: bring me the stats of both");
        Assert.Equal((TalkKind.Same, "@Channel A and @Channel B: bring me the stats of both"), (talk.Kind, talk.Message));
        Assert.Empty(rig.SentTo(homeA.Id));
        Assert.Empty(rig.SentTo(homeB.Id));
    }

    [Fact]
    public async Task An_Astur_with_no_home_thread_yet_gets_one_made_and_remembered_before_the_message_goes()
    {
        using var rig = await AsturRig.OpenAsync();
        var a = await rig.MakeAsync("Channel A", AsturShape.Squircle);
        var home = rig.DextraHome();
        Assert.Null(rig.Team.Get(a.Id)!.HomeThread);

        var talk = rig.Controller.Talk(home.Id, "@Channel A start");
        await rig.Controller.Settled();

        var thread = Assert.IsType<Guid>(talk.Thread);
        Assert.Equal(TalkKind.Sent, talk.Kind);
        Assert.Equal([a.Id], rig.Homes.Opened);
        Assert.Equal(thread, rig.Team.Get(a.Id)!.HomeThread);
        Assert.Equal(["start"], rig.SentTo(thread));
    }

    [Fact]
    public async Task When_no_home_thread_can_be_made_nothing_is_sent_and_the_composer_is_told()
    {
        using var rig = await AsturRig.OpenAsync();
        await rig.MakeAsync("Channel A", AsturShape.Squircle);
        var home = rig.DextraHome();
        var faults = new List<string>();
        rig.Controller.Runner.Faulted += faults.Add;
        rig.Homes.Fails = true;

        var talk = rig.Controller.Talk(home.Id, "@Channel A start");

        Assert.Equal(TalkKind.NoHome, talk.Kind);
        Assert.Equal("Channel A has no thread to talk in yet.", talk.Note);
        Assert.Contains(faults, f => f.Contains("home thread could not be made", StringComparison.Ordinal));
        Assert.Empty(rig.SentTo(home.Id));
    }

    [Fact]
    public async Task Dextra_is_reached_by_name_from_an_Astur_thread_and_stays_put_in_its_own()
    {
        var (rig, _, _, home, homeA, _) = await Team();
        using var disposeRig = rig;
        await rig.Team.SetHomeThreadAsync(rig.Dextra.Id, home.Id);

        var fromAstur = rig.Controller.Talk(homeA.Id, "@Dextra what is going on");
        Assert.Equal((TalkKind.Sent, home.Id, "what is going on"), (fromAstur.Kind, fromAstur.Thread, fromAstur.Message));
        Assert.Equal(["what is going on"], rig.SentTo(home.Id));

        var inOwn = rig.Controller.Talk(home.Id, "@Dextra what is going on");
        Assert.Equal((TalkKind.Same, "what is going on"), (inOwn.Kind, inOwn.Message));
    }

    [Fact]
    public async Task A_retired_Astur_is_not_talked_to_and_ordinary_messages_pass_untouched()
    {
        var (rig, a, _, home, homeA, _) = await Team();
        using var disposeRig = rig;
        await rig.Team.RetireAsync(a.Id);
        var gone = rig.Controller.Talk(home.Id, "@Channel A hello");
        Assert.Equal(TalkKind.Asked, gone.Kind);                                                 // "Channel A" is gone: it is not guessed to be "Channel B"
        Assert.Equal("Do you mean Channel B?", rig.Controller.Queue.Waiting.Single().Title);
        Assert.Empty(rig.SentTo(homeA.Id));
        var plain = rig.Controller.Talk(home.Id, "what did the digest say?");
        Assert.Equal((TalkKind.Same, "what did the digest say?"), (plain.Kind, plain.Message));
    }
}

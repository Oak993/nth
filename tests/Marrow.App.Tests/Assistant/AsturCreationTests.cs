using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.2 "Creating one", 12.4, 15.5; ED-NEW-dextra-as16-3, -4, -11): creation always raises a card. create_assistant drafts the
// profile on a card and makes nothing until the owner answers it, whatever the rules and the switches; the profile sheet edits the draft and its Create answers
// the card; the owner can also make one himself from a template. The new Astur introduces itself in its own thread and does nothing outward until he answers.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-11")]
public sealed class AsturCreationTests
{
    private const string Draft = """{"name":"Channel C","area":"Channel","purpose":"My third channel: scripts, voiceovers and its numbers.","shape":"star"}""";

    private static async Task<DecisionItem> Ask(AsturRig rig, AssistantToolRunner runner, string json = Draft)
    {
        var before = rig.Controller.Queue.Waiting.Select(c => c.Id).ToHashSet();
        var result = await rig.Call(runner, "create_assistant", json);
        Assert.False(result.IsError, result.Text);
        Assert.StartsWith("Waiting for the owner (card ", result.Text, StringComparison.Ordinal);
        return rig.Controller.Queue.Waiting.Single(c => !before.Contains(c.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Creation_always_raises_a_card_whether_or_not_nth_answers_for_the_owner(bool autoAnswer)
    {
        using var rig = await AsturRig.OpenAsync(autoAnswer);
        var runner = rig.Runner(rig.DextraHome());
        Assert.Equal(autoAnswer, rig.Controller.AutoAnswering);

        var card = await Ask(rig, runner);

        Assert.Equal("New Astur: Channel C", card.Title);
        Assert.Equal(DecisionKind.ToolCall, card.Kind);
        Assert.Equal([DecisionAnswerKind.AllowOnce, DecisionAnswerKind.Deny], card.Answers);                  // no grant can cover it
        Assert.Equal("ConfigurationCard", card.Rule);
        Assert.Equal(rig.Dextra.Id, card.AssistantId);
        Assert.Equal("Name Look Area Standing duties Connections Budget On its own Asks you", string.Join(' ', card.Inputs.Select(i => i.Name)));
        Assert.Equal("Star", card.Inputs.Single(i => i.Name == "Look").Value);
        Assert.Equal("My third channel: scripts, voiceovers and its numbers.", card.Inputs.Single(i => i.Name == "Area").Value);
        Assert.Contains("Watch the numbers, weekdays 09:00", card.Inputs.Single(i => i.Name == "Standing duties").Value, StringComparison.Ordinal);
        Assert.Contains("you sign in", card.Inputs.Single(i => i.Name == "Connections").Value, StringComparison.Ordinal);
        Assert.Equal("Up to 15% of any account a day.", card.Inputs.Single(i => i.Name == "Budget").Value);
        // Nothing is made: no profile, no folder, no slice, no home thread.
        Assert.Single(rig.Team.Profiles);
        Assert.Empty(rig.Budgets.Caps);
        Assert.Empty(rig.Homes.Opened);
        Assert.False(Directory.Exists(Path.Combine(rig.Store.Root, "assistants", "channel-c")));
        Assert.Equal(card.Id, Assert.Single(rig.Controller.Queue.Waiting).Id);
    }

    [Fact]
    public async Task A_denied_card_makes_nothing_and_the_chat_is_told_and_the_log_says_so()
    {
        using var rig = await AsturRig.OpenAsync();
        var home = rig.DextraHome();
        var card = await Ask(rig, rig.Runner(home));

        Assert.Null(rig.Controller.Queue.Answer(card.Id, DecisionAnswer.Deny("Not this month.")));
        await rig.Controller.Settled();

        Assert.Single(rig.Team.Profiles);
        Assert.Empty(rig.Budgets.Caps);
        Assert.Null(rig.Controller.DraftOf(card.Id));
        Assert.Equal(["The owner did not create the Astur. Not this month."], rig.SentTo(home.Id));
        Assert.Contains(rig.Logged(), e => e.Tool == "nth-dextra/create_assistant" && e.Result == "denied by the owner");
    }

    [Fact]
    public async Task An_allowed_card_makes_the_Astur_its_folders_and_slice_and_it_introduces_itself_in_its_own_thread()
    {
        using var rig = await AsturRig.OpenAsync();
        var home = rig.DextraHome();
        var created = new List<AssistantProfile>();
        rig.Controller.AsturCreated += created.Add;
        var card = await Ask(rig, rig.Runner(home));

        Assert.Null(rig.Controller.Queue.Answer(card.Id, DecisionAnswer.AllowOnce));
        await rig.Controller.Settled();

        var astur = Assert.Single(rig.Team.Active, p => !p.IsRoot);
        Assert.Equal(("Channel C", AsturShape.Star, rig.Dextra.Id, "channel"), (astur.Name, astur.Shape, astur.ParentId, astur.Template));
        Assert.Equal("My third channel: scripts, voiceovers and its numbers.", astur.Area);
        Assert.Equal(3, astur.Duties.Count);
        Assert.Equal(astur.Id, Assert.Single(created).Id);
        Assert.True(File.Exists(Path.Combine(rig.Store.Root, "assistants", astur.Id, "profile.json")));
        Assert.Equal((AssistantTeam.BudgetCapId(astur.Id), astur.Id, 15.0), (Assert.Single(rig.Budgets.Caps).Id, rig.Budgets.Caps[0].AssistantId, rig.Budgets.Caps[0].Percent));

        // It has its own thread, recorded on its profile, and its first message asks for nothing outward.
        var thread = Assert.Single(rig.Homes.Opened);
        Assert.Equal(astur.Id, thread);
        var homeThread = rig.Chats.Values.Single(c => c.AssistantID == Guid.Parse(astur.Id)).Id;
        Assert.Equal(homeThread, rig.Team.Get(astur.Id)!.HomeThread);
        var intro = Assert.Single(rig.SentTo(homeThread));
        Assert.StartsWith("You are Channel C, a new Astur of Dextra.", intro, StringComparison.Ordinal);
        Assert.Contains("do nothing outward", intro, StringComparison.Ordinal);
        Assert.Contains("They are proposals only: nothing runs on a schedule until the owner turns it on", intro, StringComparison.Ordinal);
        // The asking chat is told, with the id it can delegate to.
        Assert.Contains(rig.SentTo(home.Id), t => t.Contains("created the Astur “Channel C”", StringComparison.Ordinal) && t.Contains(astur.Id, StringComparison.Ordinal));
        // The log has the owner's allow, then what was made.
        var lines = rig.Logged();
        Assert.Contains(lines, e => e.Tool == "nth-dextra/create_assistant" && e.Result == "allowed by the owner: AllowOnce" && e.AllowedBy == "card:" + card.Id.ToString("D"));
        Assert.Contains(lines, e => e.Tool == "team/create" && e.Target == "Channel C" && e.Result == "made" && e.AllowedBy == "card:" + card.Id.ToString("D"));
    }

    [Fact]
    public async Task The_profile_sheets_Create_answers_the_card_with_the_owners_edits_and_a_bad_edit_leaves_the_card_waiting()
    {
        using var rig = await AsturRig.OpenAsync();
        var card = await Ask(rig, rig.Runner(rig.DextraHome()));
        var drafted = rig.Controller.DraftOf(card.Id)!;
        Assert.Equal("Channel C", drafted.Name);

        // Problems come back to the sheet and nothing moves.
        var bad = rig.Controller.CreateFromCard(card.Id, drafted with { Name = "Dextra" });
        Assert.Contains("\"Dextra\" is a word nth listens for; pick another name.", bad);
        Assert.Contains(rig.Controller.Queue.Waiting, c => c.Id == card.Id);
        Assert.Single(rig.Team.Profiles);

        var edited = drafted with
        {
            Name = "Channel Three",
            Budget = new(25, 4m),
            Duties = new ValueList<DutyDraft>([drafted.Duties[0], drafted.Duties[1] with { Keep = false }, drafted.Duties[2]]),
            Style = "short and plain",
        };
        Assert.Empty(rig.Controller.CreateFromCard(card.Id, edited));
        await rig.Controller.Settled();

        var astur = Assert.Single(rig.Team.Active, p => !p.IsRoot);
        Assert.Equal(("Channel Three", "short and plain", new AsturBudget(25, 4m)), (astur.Name, astur.Style, astur.Budget));
        Assert.Equal([true, false, true], astur.Duties.Select(d => d.Keep));
        Assert.DoesNotContain(rig.Controller.Queue.Waiting, c => c.Id == card.Id);
        Assert.Contains(rig.Logged(), e => e.Tool == "team/create" && e.Target == "Channel Three");
        // The card is spent: a second Create finds nothing waiting.
        Assert.Equal(["That card is not waiting any more."], rig.Controller.CreateFromCard(card.Id, edited with { Name = "Another" }));
        Assert.Null(rig.Controller.DraftOf(card.Id));
    }

    [Fact]
    public async Task A_chat_that_read_untrusted_content_cannot_ask_and_the_card_it_offers_makes_nothing_until_the_owner_raises_it()
    {
        using var rig = await AsturRig.OpenAsync();
        var home = rig.DextraHome();
        var runner = rig.Runner(home);
        rig.Controller.AbsorbHome(home.Id, Taint.From(ContentOrigin.Web, "https://news.example"));

        var refused = await rig.Call(runner, "create_assistant", Draft);

        Assert.True(refused.IsError);
        Assert.Contains("needs an untainted request", refused.Text, StringComparison.Ordinal);
        var offered = Assert.Single(rig.Controller.Queue.Waiting);
        Assert.StartsWith("This chat read untrusted content, so it cannot ask for this itself.", offered.Why, StringComparison.Ordinal);
        Assert.Single(rig.Team.Profiles);
        Assert.Empty(rig.Homes.Opened);

        // He raises it himself: that is his card.
        Assert.Null(rig.Controller.Queue.Answer(offered.Id, DecisionAnswer.AllowOnce));
        await rig.Controller.Settled();
        Assert.Equal("Channel C", Assert.Single(rig.Team.Active, p => !p.IsRoot).Name);
    }

    [Fact]
    public async Task A_missions_agent_is_not_offered_the_tool_at_all()
    {
        using var rig = await AsturRig.OpenAsync();
        var start = rig.Controller.Runner.Start(new MissionRequest("Fix it", "Fix it.", rig.Dextra.Id, rig.Project.Id, rig.Project.Path, Marrow.Core.Models.ProviderKind.Claude));
        var lead = rig.Lead(rig.Started(start.Record));
        var result = await rig.Call(rig.Runner(lead), "create_assistant", Draft);
        Assert.True(result.IsError);
        Assert.Contains("is not an nth-dextra tool this chat can use", result.Text, StringComparison.Ordinal);
        Assert.Empty(rig.Controller.Queue.Waiting);
        Assert.Single(rig.Team.Profiles);
    }

    [Fact]
    public async Task A_call_that_somehow_reaches_the_host_as_an_allow_still_makes_nothing()
    {
        using var rig = await AsturRig.OpenAsync();
        var home = rig.DextraHome();
        var chat = new AssistantChat(home.Id, rig.Dextra.Id, null, AssistantChatRole.Home, Marrow.Core.Models.ProviderKind.Claude, "model-a");
        var call = new AssistantToolCall(chat, AssistantToolCatalog.Find("create_assistant")!, Marrow.Core.Support.JSONValue.Parse(Draft)!,
            new Classification(ActionClass.WriteElsewhere, "changes the assistant's standing configuration"),
            new PolicyDecision(Verdict.Allow, PolicyRule.Alone, "Does on its own"), AssistantRoute.Mcp);

        var result = await rig.Controller.Run(call, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Not run: an Astur is only made from a card the owner answers.", result.Text);
        Assert.Single(rig.Team.Profiles);
        Assert.Empty(rig.Controller.Queue.Waiting);
    }

    [Fact]
    public async Task A_chat_that_keeps_drafting_cannot_bury_the_owner_in_cards()
    {
        using var rig = await AsturRig.OpenAsync();
        var runner = rig.Runner(rig.DextraHome());
        for (var n = 0; n < AssistantController.MaxOpenAsturCards; n++) await Ask(rig, runner, "{\"name\":\"Draft " + n + "\",\"area\":\"Channel\",\"purpose\":\"One more channel.\"}");
        var over = await rig.Call(runner, "create_assistant", Draft);
        Assert.True(over.IsError);
        Assert.Equal("Not run: the owner's card could not be raised.", over.Text);
        Assert.Equal(AssistantController.MaxOpenAsturCards, rig.Controller.Queue.Waiting.Count(c => c.Title.StartsWith("New Astur", StringComparison.Ordinal)));

        // Answering one makes room for the next.
        Assert.Null(rig.Controller.Queue.Answer(rig.Controller.Queue.Waiting[0].Id, DecisionAnswer.Deny()));
        Assert.False((await rig.Call(runner, "create_assistant", Draft)).IsError);
    }

    [Fact]
    public async Task A_seventh_card_is_raised_but_cannot_be_made_and_the_chat_is_told_why()
    {
        using var rig = await AsturRig.OpenAsync();
        for (var n = 0; n < AsturShapes.MostAsturs; n++) await rig.MakeAsync("Astur " + n, AsturShapes.All[n]);
        var home = rig.DextraHome();
        var card = await Ask(rig, rig.Runner(home), """{"name":"Channel C","area":"Channel","purpose":"My third channel."}""");
        Assert.Null(rig.Controller.DraftOf(card.Id)!.Shape);                                                      // no shape is free

        Assert.Null(rig.Controller.Queue.Answer(card.Id, DecisionAnswer.AllowOnce));
        await rig.Controller.Settled();

        Assert.Equal(AsturShapes.MostAsturs, rig.Team.Active.Count(p => !p.IsRoot));
        Assert.Contains(rig.SentTo(home.Id), t => t.Contains("The Astur was not made: There can be at most 6 Asturs. Retire one first.", StringComparison.Ordinal));
        Assert.Contains(rig.Logged(), e => e.Tool == "team/create" && e.Result!.StartsWith("not made: There can be at most 6", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_owner_can_make_one_himself_from_a_template_with_no_model_in_it_and_the_home_thread_failing_does_not_undo_it()
    {
        using var rig = await AsturRig.OpenAsync();
        var faults = new List<string>();
        rig.Controller.Runner.Faulted += faults.Add;
        var draft = AsturDrafter.FromTemplate("inbox", rig.Team.Profiles);

        rig.Homes.Fails = true;
        var made = await rig.Controller.CreateByOwner(draft);
        await rig.Controller.Settled();

        Assert.True(made.Ok);
        Assert.Equal("Inbox", made.Profile!.Name);
        Assert.Equal(ClassDefault.Never, made.Profile.ClassRules[ActionClass.Outward]);                           // drafts only: it never sends
        Assert.Empty(rig.Controller.Queue.Waiting);                                                               // the sheet was his card
        Assert.Contains(faults, f => f.Contains("home thread could not be made", StringComparison.Ordinal));
        Assert.Null(rig.Team.Get(made.Profile.Id)!.HomeThread);
        Assert.Contains(rig.Logged(), e => e.Tool == "team/create" && e.Result == "made" && e.AllowedBy is null);

        var refused = await rig.Controller.CreateByOwner(draft);                                                  // the same name and look again
        Assert.False(refused.Ok);
    }

    [Fact]
    public async Task Editing_and_retiring_are_the_owners_and_are_logged_and_a_retirement_waits_for_the_Asturs_missions_to_be_stopped()
    {
        using var rig = await AsturRig.OpenAsync();
        var a = await rig.MakeAsync("Channel A", AsturShape.Squircle);
        var edited = await rig.Controller.UpdateAstur(a.Id, AsturTeamRules.ToDraft(a) with { Style = "terse" });
        Assert.True(edited.Ok);
        Assert.Equal("terse", rig.Team.Get(a.Id)!.Style);
        Assert.Contains(rig.Logged(), e => e.Tool == "team/update" && e.Result == "saved" && e.AllowedBy == "owner");

        var mission = rig.Controller.Runner.Start(new MissionRequest("Work", "Do it.", a.Id, rig.Project.Id, rig.Project.Path, Marrow.Core.Models.ProviderKind.Claude)).Record;
        var refused = await rig.Controller.RetireAstur(a.Id);
        Assert.Equal("It has missions at work. Stop them first.", refused.Problem);
        Assert.False(rig.Team.Get(a.Id)!.Retired);

        rig.Started(mission);
        rig.StopMission(mission);
        var client = rig.Web.For(a.Id);
        Assert.True((await rig.Controller.RetireAstur(a.Id)).Ok);
        Assert.NotSame(client, rig.Web.For(a.Id));                                                               // its web client was let go
        Assert.Contains(rig.Logged(), e => e.Tool == "team/retire" && e.Result == "retired");
        Assert.Empty(rig.Budgets.Caps);
    }

    [Fact]
    public async Task Without_a_team_the_two_tools_say_they_are_not_part_of_this_build_as_before()
    {
        using var rig = await AsturRig.OpenAsync(withTeam: false);
        Assert.Null(rig.Controller.Team);
        var home = rig.DextraHome();
        var runner = rig.Runner(home);
        var result = await rig.Call(runner, "create_assistant", Draft);
        Assert.False(result.IsError);                                                                             // a generic card, as before
        var card = Assert.Single(rig.Controller.Queue.Waiting);
        Assert.Equal("Draft an Astur", card.Title);
        Assert.Null(rig.Controller.Queue.Answer(card.Id, DecisionAnswer.AllowOnce));
        Assert.Contains(rig.SentTo(home.Id), t => t.Contains("is not part of this build of Dextra yet", StringComparison.Ordinal));
        Assert.True((await rig.Controller.CreateByOwner(new AsturDraft())).Problem!.Contains("not part of this build", StringComparison.Ordinal));
        Assert.Contains("not part of this build", Assert.Single(rig.Controller.CreateFromCard(card.Id, new AsturDraft())), StringComparison.Ordinal);
    }
}

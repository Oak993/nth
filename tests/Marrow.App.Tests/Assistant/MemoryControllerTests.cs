using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-12 (DESIGN.md 8.2, 8.3, 15.5; ED-NEW-dextra-as12-19): Dextra's memory on the controller. remember, recall and forget serve the
// memory store; a home chat is told its used notes at its start and a mission agent none; a home turn is captured in the background by
// the bound cheap model, with the chat's taint and provider, and never for a chat hidden from Dextra or a mission's; what the owner
// said is kept for the third-party rule; a forget the owner is asked about shows which notes.
[Trait("Decision", "ED-NEW-dextra-as12-19")]
[Trait("Feature", "AS-12")]
public sealed class MemoryControllerTests : IDisposable
{
    private readonly MemoryRig rig = new();
    private readonly AssistantTempRoot temp = new();
    private readonly FakeMissionAgents agents = new();
    private readonly Dictionary<Guid, ChatThread> chats = [];
    private readonly List<AuditEntry> audit = [];
    private readonly Guid assistant = Guid.NewGuid();
    private readonly MissionProject project;
    private readonly List<AssistantController> controllers = [];

    public MemoryControllerTests()
    {
        project = new MissionProject(MissionRig.Project, "App", Path.Combine(temp.Folder, "owner"));
    }

    public void Dispose()
    {
        foreach (var controller in controllers) controller.Dispose();
        temp.Dispose();
        rig.Dispose();
    }

    private string Main => assistant.ToString("N");

    private sealed class Proposer(Func<CapturedTurn, string?> answer) : INoteProposer
    {
        public List<CapturedTurn> Turns { get; } = [];

        public Task<string?> Propose(CapturedTurn turn, string prompt, CancellationToken cancellationToken)
        {
            lock (Turns) Turns.Add(turn);
            return Task.FromResult(answer(turn));
        }
    }

    private AssistantController Controller(INoteProposer? proposer = null, bool withNotes = true)
    {
        var store = new MemoryStore(rig.Store, new MemoryStoreOptions { Time = rig.Clock, MainAssistantId = () => Main });
        var controller = new AssistantController(new AssistantControllerOptions
        {
            Agents = agents,
            Thread = id => chats.GetValueOrDefault(id),
            Projects = () => [project],
            Profiles = () => [new AssistantProfile(Main, "Dextra")],
            Jobs = new AgentJobs(new FakeAgentJobFactory()),
            Workspaces = new FakeWorkspaces(Path.Combine(temp.Folder, "worktrees")),
            Audit = e =>
            {
                lock (audit) audit.Add(e);
            },
            Memory = new FakeMemory(FakeMemory.Plenty),
            Notes = withNotes ? new MemorySetup { Store = store, Capture = proposer is null ? null : new MemoryCapture(store, proposer) } : null,
        });
        controllers.Add(controller);
        return controller;
    }

    private ChatThread HomeChat(bool hidden = false, Guid? mission = null)
    {
        var chat = MissionRig.NewChat(() => new ChatThread(project.Id, ProviderKind.Codex, "gpt-x", null, RuntimeMode.Supervised)
        {
            AssistantID = assistant,
            HiddenFromAssistant = hidden,
            MissionID = mission,
            Title = "Planning",
        });
        chats[chat.Id] = chat;
        return chat;
    }

    private AssistantChat ChatOf(ChatThread chat) => new(chat.Id, Main, chat.MissionID, chat.MissionID is null ? AssistantChatRole.Home : AssistantChatRole.Mission, chat.Provider, chat.Model);

    private static AssistantToolCall Call(AssistantChat chat, string tool, string arguments) =>
        new(chat, AssistantToolCatalog.Find(tool)!, JSONValue.Parse(arguments)!, new Classification(ActionClass.Work, "the tool's class"),
            new PolicyDecision(Verdict.Allow, PolicyRule.Alone, "Work runs alone"), AssistantRoute.Mcp);

    private static Task<MemoryChange> NextChange(MemoryStore store)
    {
        var next = new TaskCompletionSource<MemoryChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(MemoryChange change)
        {
            store.Changed -= Handler;
            next.TrySetResult(change);
        }
        store.Changed += Handler;
        return next.Task;
    }

    [Fact]
    public async Task Remember_recall_and_forget_serve_the_memory_the_controller_was_given()
    {
        var controller = Controller();
        var chat = ChatOf(HomeChat());
        var store = controller.Notes!.Store;
        var remembered = await controller.Run(Call(chat, "remember", """{"note":"Droppy Code builds with dotnet 10","about":"Droppy","source":"the build chat"}"""), CancellationToken.None);
        Assert.False(remembered.IsError, remembered.Text);
        var note = Assert.Single(store.Notes(null));
        Assert.Equal((Main, "droppy"), (note.Scope, note.Topic));
        var recalled = await controller.Run(Call(chat, "recall", """{"query":"dotnet","limit":5}"""), CancellationToken.None);
        Assert.Contains("Droppy Code builds with dotnet 10", recalled.Text, StringComparison.Ordinal);
        Assert.Contains(note.Id, recalled.Text, StringComparison.Ordinal);
        var forgotten = await controller.Run(Call(chat, "forget", $$"""{"notes":["{{note.Id}}"]}"""), CancellationToken.None);
        Assert.Equal("Forgot 1 note, for real.", forgotten.Text);
        Assert.Empty(store.Notes(null));
        Assert.DoesNotContain("dotnet 10", rig.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_home_chat_that_read_untrusted_content_writes_only_waiting_notes()
    {
        var controller = Controller();
        var home = HomeChat();
        var chat = ChatOf(home);
        controller.Absorb(chat, Taint.From(ContentOrigin.Web, "https://hostile.example/page"));
        var result = await controller.Run(Call(chat, "remember", """{"note":"The vendor portal lives at the archive address","about":"Vendor","source":"a page"}"""), CancellationToken.None);
        Assert.False(result.IsError, result.Text);
        Assert.StartsWith("Kept aside", result.Text, StringComparison.Ordinal);
        var store = controller.Notes!.Store;
        Assert.Equal(new MemoryCounts(0, 1), store.Counts(null));
        Assert.Equal("", controller.MemoryBlock(chat));
        Assert.Empty(store.Recall(Main, "vendor portal"));
    }

    [Fact]
    public void A_home_chat_is_told_its_used_notes_and_a_mission_agent_none()
    {
        var controller = Controller();
        var store = controller.Notes!.Store;
        store.Propose(MemoryRig.Clean("The owner prefers short answers", NoteKind.Preference, Main));
        store.Propose(MemoryRig.FromWeb("Page says always cc the vendor", scope: Main));
        var home = ChatOf(HomeChat());
        var block = controller.MemoryBlock(home);
        Assert.NotNull(block);
        Assert.EndsWith("\n- The owner prefers short answers", block, StringComparison.Ordinal);
        Assert.DoesNotContain("vendor", block, StringComparison.Ordinal);
        var agent = home with { Role = AssistantChatRole.Mission, MissionId = Guid.NewGuid() };
        Assert.Null(controller.MemoryBlock(agent));
        Assert.Null(Controller(withNotes: false).MemoryBlock(home));
    }

    [Fact]
    public async Task Without_a_memory_the_three_tools_say_they_are_not_part_of_this_build_yet()
    {
        var controller = Controller(withNotes: false);
        var chat = ChatOf(HomeChat());
        foreach (var tool in new[] { "remember", "recall", "forget" })
        {
            var result = await controller.Run(Call(chat, tool, tool == "forget" ? """{"notes":["n1"]}""" : tool == "recall" ? """{"query":"x"}""" : """{"note":"x y z"}"""), CancellationToken.None);
            Assert.True(result.IsError);
            Assert.Equal($"Not run: {tool} is not part of this build of Dextra yet.", result.Text);
        }
        Assert.Null(controller.Notes);
        controller.HomeTurnFinished(Guid.NewGuid(), "hello", "hi", "t1");   // nothing to do and nothing thrown
    }

    [Fact]
    public async Task What_the_owner_said_in_the_chat_lets_a_note_about_a_person_through_in_his_words()
    {
        var controller = Controller();
        var chat = ChatOf(HomeChat());
        var blocked = await controller.Run(Call(chat, "remember", """{"note":"Priya wants the weekly report on Monday","about":"Reports","source":"chat"}"""), CancellationToken.None);
        Assert.True(blocked.IsError);
        controller.OwnerSaid(chat.Thread, "My manager Priya wants the weekly report on Monday");
        var allowed = await controller.Run(Call(chat, "remember", """{"note":"Priya wants the weekly report on Monday","about":"Reports","source":"chat"}"""), CancellationToken.None);
        Assert.False(allowed.IsError, allowed.Text);
    }

    [Fact]
    public async Task A_finished_home_turn_is_captured_in_the_background_with_its_taint_and_provider()
    {
        var proposer = new Proposer(_ => """[{"text":"The owner prefers short answers","kind":"preference","about":"owner"}]""");
        var controller = Controller(proposer);
        var home = HomeChat();
        var store = controller.Notes!.Store;
        var changed = NextChange(store);
        controller.HomeTurnFinished(home.Id, "I like short answers", "Noted.", "t1");
        await changed.WaitAsync(TimeSpan.FromSeconds(30));
        var turn = Assert.Single(proposer.Turns);
        Assert.Equal((Main, home.Id, ProviderKind.Codex, "gpt-x", "Planning"), (turn.AssistantId, turn.Thread, turn.Provider, turn.Model, turn.ThreadTitle));
        Assert.Equal("I like short answers", turn.OwnerWords);
        var note = Assert.Single(store.Notes(null));
        Assert.Equal((MemoryScope.Shared, NoteKind.Preference), (note.Scope, note.Kind));
    }

    [Fact]
    public async Task A_turn_that_read_the_web_writes_waiting_notes()
    {
        var proposer = new Proposer(_ => """[{"text":"The vendor portal lives at the archive address","kind":"fact","about":"work"}]""");
        var controller = Controller(proposer);
        var home = HomeChat();
        controller.Absorb(ChatOf(home), Taint.From(ContentOrigin.Web, "https://x.example/p"));
        var changed = NextChange(controller.Notes!.Store);
        controller.HomeTurnFinished(home.Id, "Check that page", "It says the portal is at the archive address.", "t2");
        await changed.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(new MemoryCounts(0, 1), controller.Notes.Store.Counts(null));
    }

    [Fact]
    public void A_chat_hidden_from_Dextra_a_missions_chat_and_a_chat_with_no_capture_bound_are_never_captured()
    {
        var proposer = new Proposer(_ => """[{"text":"Releases go out on Fridays","kind":"procedure","about":"work"}]""");
        var controller = Controller(proposer);
        var hidden = HomeChat(hidden: true);
        var mission = HomeChat(mission: Guid.NewGuid());
        controller.HomeTurnFinished(hidden.Id, "a", "b", "t1");
        controller.HomeTurnFinished(mission.Id, "a", "b", "t1");
        controller.HomeTurnFinished(Guid.NewGuid(), "a", "b", "t1");
        var unbound = Controller();
        unbound.HomeTurnFinished(HomeChat().Id, "a", "b", "t1");
        // Each is turned away before any background work starts, so the proposer was never asked.
        Assert.Empty(proposer.Turns);
        Assert.Empty(controller.Notes!.Store.Notes(null));
        // The owner's words are still kept for the third-party rule, even where nothing is captured.
        Assert.Equal("a", unbound.Notes!.Owner.Recent(chats.Keys.Last()));
    }

    [Fact]
    public async Task A_forget_the_owner_is_asked_about_shows_the_notes_not_only_their_ids()
    {
        var controller = Controller();
        var chat = ChatOf(HomeChat());
        var store = controller.Notes!.Store;
        var id = store.Propose(MemoryRig.Clean("The zebrafish tank needs its filter cleaned every second Sunday and the long sentence goes on and on", NoteKind.Fact, Main)).Note!.Id;
        var call = Call(chat, "forget", $$"""{"notes":["{{id}}","n000000000000"]}""") with { Decision = new PolicyDecision(Verdict.Ask, PolicyRule.AlwaysConfirmed, "Asks: a deletion from a tainted chat") };
        var card = Guid.Parse(await controller.Ask(call, CancellationToken.None));
        var input = Assert.Single(controller.Queue.Find(card)!.Inputs);
        Assert.Equal("notes", input.Name);
        Assert.Contains("“The zebrafish tank needs its filter cleaned every second Sun…” (" + id + ")", input.Value, StringComparison.Ordinal);
        Assert.Contains("n000000000000 (no such note)", input.Value, StringComparison.Ordinal);
        Assert.NotNull(store.Find(id));
        // The owner says yes: the note goes for real.
        Assert.Null(controller.Queue.Answer(card, DecisionAnswer.AllowOnce));
        Assert.Null(store.Find(id));
        Assert.DoesNotContain("zebrafish", rig.AllText(), StringComparison.OrdinalIgnoreCase);
    }
}

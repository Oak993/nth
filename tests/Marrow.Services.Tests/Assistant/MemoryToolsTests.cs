using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-12 (DESIGN.md 8.2, 8.3, 15.5, 12.4): remember, recall and forget on the memory store, run through the real tool runner and
// the policy engine with a stand-in host. A tainted chat's note is quarantined, its forget asks; the audit log never holds a note's
// words; recall never returns a note that waits; forget removes for real.
[Trait("Decision", "ED-NEW-dextra-as12-17")]
[Trait("Feature", "AS-12")]
public sealed class MemoryToolsTests : IDisposable
{
    private readonly MemoryRig rig = new();
    private readonly OwnerWordsLog owner = new();

    public void Dispose() => rig.Dispose();

    private (DextraFakeHost Host, AssistantToolRunner Runner, Guid Thread) Chat(bool tainted = false, AssistantChatRole role = AssistantChatRole.Home)
    {
        var host = new DextraFakeHost { Tainted = tainted };
        var thread = host.Add(role, role == AssistantChatRole.Mission ? Guid.NewGuid() : null);
        var tools = new MemoryTools(rig.Memory, owner);
        host.Answer = call => call.Tool.Name switch
        {
            "remember" => tools.Remember(call, host.PolicyContext(call.Chat).TaintContext),
            "recall" => tools.Recall(call),
            "forget" => tools.Forget(call),
            _ => AssistantToolResult.Failed("not under test"),
        };
        using var bound = AssistantMcpBridge.Host.Override(host);
        var registration = AssistantMcpBridge.Register(BuiltInServerFixtures.Config(ProviderKind.Claude, thread))!;
        return (host, new AssistantToolRunner(registration, null), thread);
    }

    private static Task<AssistantToolResult> Call(AssistantToolRunner runner, string tool, params (string, JSONValue)[] args) =>
        runner.Call(tool, JSONValue.FromDictionaryLiteral(args), AssistantRoute.Mcp, CancellationToken.None);

    [Fact]
    public async Task Remember_writes_a_note_with_its_source_and_a_preference_goes_to_the_shared_set()
    {
        var (host, runner, thread) = Chat();
        var fact = await Call(runner, "remember", ("note", "Droppy Code builds with dotnet 10"), ("about", "Droppy"), ("source", "the build chat"));
        var preference = await Call(runner, "remember", ("note", "Always reply in English"), ("about", "Language"), ("source", "the owner"));
        Assert.False(fact.IsError, fact.Text);
        Assert.Matches(@"^Kept\. \(note n[0-9a-f]{12}\)$", fact.Text);
        var notes = rig.Memory.Notes(null);
        var built = Assert.Single(notes, n => n.Text.StartsWith("Droppy Code", StringComparison.Ordinal));
        Assert.Equal((MemoryRig.Main, "droppy", NoteKind.Fact, NoteAuthor.Assistant), (built.Scope, built.Topic, built.Kind, built.Author));
        Assert.Equal(new NoteSource(NoteSourceKind.Chat, "chat:" + thread.ToString("N"), Detail: "the build chat"), built.Sources.Single());
        Assert.Equal(MemoryScope.Shared, Assert.Single(notes, n => n.Kind == NoteKind.Preference).Scope);
        Assert.Equal(2, host.Runs.Count);
    }

    [Fact]
    public async Task Remember_from_a_tainted_chat_is_quarantined_and_the_model_is_told_so()
    {
        var (host, runner, _) = Chat(tainted: true);
        var result = await Call(runner, "remember", ("note", "The vendor portal lives at the archive address"), ("about", "Vendor"), ("source", "a web page"));
        Assert.False(result.IsError, result.Text);
        Assert.StartsWith("Kept aside: it came from untrusted content, so it is not used until the owner keeps it.", result.Text, StringComparison.Ordinal);
        var note = Assert.Single(rig.Memory.Notes(null));
        Assert.True(note.Quarantined);
        Assert.True(Assert.Single(host.Runs).Quarantine);
        Assert.Equal(new MemoryCounts(0, 1), rig.Memory.Counts(null));
        Assert.Empty(rig.Memory.Recall(MemoryRig.Main, "vendor portal"));
    }

    [Fact]
    public async Task Remember_refuses_a_secret_and_a_stranger_and_says_why()
    {
        var (_, runner, thread) = Chat();
        var secret = await Call(runner, "remember", ("note", "The staging password is hunter2horse"), ("about", "Staging"), ("source", "chat"));
        Assert.True(secret.IsError);
        Assert.Contains("Secrets are never stored", secret.Text, StringComparison.Ordinal);
        var person = await Call(runner, "remember", ("note", "Priya wants the weekly report on Monday"), ("about", "Reports"), ("source", "chat"));
        Assert.True(person.IsError);
        Assert.Contains("Notes about people are kept only when the owner states them", person.Text, StringComparison.Ordinal);
        // Once the owner has said it in this chat, in those words, it is kept.
        owner.Said(thread, "Note that my manager Priya wants the weekly report on Monday");
        var stated = await Call(runner, "remember", ("note", "Priya wants the weekly report on Monday"), ("about", "Reports"), ("source", "chat"));
        Assert.False(stated.IsError, stated.Text);
        Assert.DoesNotContain("hunter2horse", rig.AllText(), StringComparison.Ordinal);
        Assert.Single(rig.Memory.Notes(null));
    }

    [Fact]
    public async Task The_activity_log_holds_the_length_of_a_note_and_never_its_words()
    {
        var (host, runner, _) = Chat();
        await Call(runner, "remember", ("note", "SENTINELNOTE4711 is the codename of the shelf project"), ("about", "SENTINELTOPIC4712"), ("source", "chat"));
        await Call(runner, "remember", ("note", "The staging password is SENTINELSECRET4713"), ("about", "Staging"), ("source", "chat"));
        Assert.NotEmpty(host.Audits);
        var logged = string.Join('\n', host.Audits.SelectMany(a => a.Inputs.Select(i => i.Name + "=" + i.Text + "/" + i.TypedLength)));
        Assert.DoesNotContain("SENTINELNOTE4711", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINELSECRET4713", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINELTOPIC4712", logged, StringComparison.Ordinal);
        Assert.Contains(host.Audits.SelectMany(a => a.Inputs), i => i.Name == "note" && i.TypedLength == "SENTINELNOTE4711 is the codename of the shelf project".Length);
        Assert.DoesNotContain("SENTINELSECRET4713", rig.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recall_lists_used_notes_with_their_ids_and_sources_and_never_a_waiting_one()
    {
        var (_, runner, _) = Chat();
        rig.Memory.Propose(MemoryRig.Clean("The owner lives in Pune", NoteKind.Fact) with { MayChange = true });
        rig.Memory.Propose(MemoryRig.Clean("The owner prefers short answers", NoteKind.Preference));
        rig.Memory.Propose(MemoryRig.FromWeb("Page says the owner lives on the moon"));
        var all = await Call(runner, "recall", ("query", "owner"), ("limit", 10));
        Assert.False(all.IsError);
        Assert.StartsWith("2 notes:", all.Text, StringComparison.Ordinal);
        Assert.Matches(@"- \[n[0-9a-f]{12}\] The owner lives in Pune \(Fact · may have changed: check before relying on it · from your chat “Planning”\)", all.Text);
        Assert.Contains("The owner prefers short answers (Preference · from your chat “Planning”)", all.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("moon", all.Text, StringComparison.Ordinal);
        Assert.EndsWith("What the owner says now beats a note.", all.Text, StringComparison.Ordinal);
        Assert.StartsWith("1 note:", (await Call(runner, "recall", ("query", "owner"), ("limit", 1))).Text, StringComparison.Ordinal);
        Assert.Equal("Nothing is remembered about that.", (await Call(runner, "recall", ("query", "zebrafish"), ("limit", 5))).Text);
    }

    [Fact]
    public async Task Forget_deletes_for_real_and_says_what_it_could_not_find()
    {
        var (_, runner, _) = Chat();
        var a = rig.Memory.Propose(MemoryRig.Clean("The zebrafish tank needs its filter cleaned")).Note!.Id;
        var b = rig.Memory.Propose(MemoryRig.Clean("Releases go out on Fridays")).Note!.Id;
        var result = await Call(runner, "forget", ("notes", JSONValue.FromArrayLiteral(a, "n000000000000")));
        Assert.False(result.IsError, result.Text);
        Assert.Equal("Forgot 1 note, for real. 1 ids matched no note you can read.", result.Text);
        Assert.Null(rig.Memory.Find(a));
        Assert.NotNull(rig.Memory.Find(b));
        Assert.DoesNotContain("zebrafish", rig.AllText(), StringComparison.OrdinalIgnoreCase);
        var none = await Call(runner, "forget", ("notes", JSONValue.FromArrayLiteral("n000000000000")));
        Assert.Equal("No note with those ids is one you can read.", none.Text);
    }

    [Fact]
    public async Task Forget_from_a_tainted_chat_asks_the_owner_instead_of_deleting()
    {
        var (host, runner, _) = Chat(tainted: true);
        var id = rig.Memory.Propose(MemoryRig.Clean("The owner prefers short answers", NoteKind.Preference)).Note!.Id;
        var result = await Call(runner, "forget", ("notes", JSONValue.FromArrayLiteral(id)));
        Assert.False(result.IsError, result.Text);
        Assert.Empty(host.Runs);
        Assert.Single(host.Asks);
        Assert.NotNull(rig.Memory.Find(id));
    }

    [Fact]
    public async Task A_mission_agents_note_goes_to_its_assistants_memory_with_the_mission_as_its_source()
    {
        var (host, runner, _) = Chat(role: AssistantChatRole.Mission);
        var result = await Call(runner, "remember", ("note", "The shelf project uses blue boxes"), ("about", "Shelf"), ("source", "the shelf mission"));
        Assert.False(result.IsError, result.Text);
        var note = Assert.Single(rig.Memory.Notes(null));
        Assert.Equal(MemoryRig.Main, note.Scope);
        Assert.Equal(NoteSourceKind.Mission, note.Sources.Single().Kind);
        Assert.StartsWith("mission:", note.Sources.Single().Target, StringComparison.Ordinal);
        Assert.Equal(host.Chats.Values.Single().MissionId!.Value.ToString("N"), note.Sources.Single().Target["mission:".Length..]);
    }

    [Fact]
    public void What_the_owner_said_recently_is_kept_in_memory_only_a_few_messages_per_chat()
    {
        var log = new OwnerWordsLog();
        var chat = Guid.NewGuid();
        Assert.Null(log.Recent(chat));
        log.Said(chat, "  ");
        log.Said(chat, null);
        Assert.Null(log.Recent(chat));
        for (var i = 0; i < OwnerWordsLog.MessagesPerChat + 3; i++) log.Said(chat, $"message {i}");
        var lines = log.Recent(chat)!.Split('\n');
        Assert.Equal(OwnerWordsLog.MessagesPerChat, lines.Length);
        Assert.Equal("message 3", lines[0]);
        log.Said(chat, new string('x', OwnerWordsLog.MaxMessageChars + 50));
        Assert.Equal(OwnerWordsLog.MaxMessageChars, log.Recent(chat)!.Split('\n')[^1].Length);
        for (var i = 0; i < OwnerWordsLog.MaxChats + 5; i++) log.Said(Guid.NewGuid(), "hello there");
        Assert.Null(log.Recent(chat));                                      // the oldest chat was let go
    }
}

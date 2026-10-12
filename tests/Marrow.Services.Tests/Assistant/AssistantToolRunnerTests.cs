using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-4 (DESIGN.md rev 5, 12.3, 12.4, 12.5, 15.5; ED-NEW-dextra-as4-4): one call of an nth-dextra tool, the same on
// every route. The policy engine is asked before every tool acts; a refusal never reaches the host; a tainted context is
// refused the tools 12.4 lists, its notes are quarantined and its delegations pass data only; until auto-answering is on
// an Allow becomes a card except for reads, stops and words to the owner or the parent; and everything fails closed.
public sealed class AssistantToolRunnerTests
{
    private static (DextraFakeHost Host, AssistantToolRunner Runner) Rig(AssistantChatRole role = AssistantChatRole.Home,
        Func<Classification, PolicyContext, PolicyDecision>? inner = null, bool recording = true)
    {
        var host = new DextraFakeHost();
        var thread = host.Add(role, role == AssistantChatRole.Mission ? Guid.NewGuid() : null);
        using var bound = AssistantMcpBridge.Host.Override(host);
        var registration = AssistantMcpBridge.Register(BuiltInServerFixtures.Config(ProviderKind.Claude, thread))!;
        return (host, new AssistantToolRunner(registration, recording ? host.RecordingPolicy(inner) : inner));
    }

    private static Task<AssistantToolResult> Call(AssistantToolRunner runner, string name, JSONValue? args = null) =>
        runner.Call(name, args ?? AssistantToolCatalogFixture.Minimal(name), AssistantRoute.Mcp, CancellationToken.None);

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-4")]
    public async Task A_policy_call_precedes_every_tool_and_its_audit_line_comes_before_anything_acts()
    {
        var (host, runner) = Rig();
        foreach (var tool in AssistantToolCatalog.All)
        {
            host.Log.Clear();
            var result = await Call(runner, tool.Name);
            var policy = host.Log.IndexOf("policy:" + tool.Name);
            var acted = host.Log.FindIndex(l => l == "run:" + tool.Name || l == "ask:" + tool.Name);
            var decided = host.Log.FindIndex(l => l.StartsWith("audit:nth-dextra/" + tool.Name + ":", StringComparison.Ordinal));
            Assert.True(policy == 0, $"{tool.Name}: the policy was not asked first ({string.Join(" | ", host.Log)})");
            Assert.True(acted > decided && decided > policy, $"{tool.Name}: {string.Join(" | ", host.Log)}");
            Assert.Single(host.Log, l => l.StartsWith("policy:", StringComparison.Ordinal));
            Assert.False(result.IsError, tool.Name + ": " + result.Text);
        }
        // On the engine's own answers with auto-answering on: configuration and deletions ask, the rest run.
        Assert.Equal(new[] { "schedule_duty", "watch_folder", "watch_page", "create_assistant", "propose_capability" }, host.Asks.Select(c => c.Tool.Name));
        Assert.Equal(19, host.Runs.Count);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-4")]
    public async Task A_refusal_never_reaches_the_host_and_says_why()
    {
        var (host, runner) = Rig(inner: (leaf, _) => new PolicyDecision(Verdict.Deny, PolicyRule.OwnerNever, "You chose never for this") { Leaf = leaf });
        foreach (var tool in AssistantToolCatalog.All)
        {
            var result = await Call(runner, tool.Name);
            Assert.True(result.IsError);
            Assert.Equal("Refused: You chose never for this.", result.Text);
        }
        Assert.Empty(host.Runs);
        Assert.Empty(host.Asks);
        Assert.Empty(host.Offers);
        Assert.Equal(24, host.Log.Count(l => l.StartsWith("policy:", StringComparison.Ordinal)));
        Assert.All(host.Audits, a => Assert.Equal("refused: OwnerNever", a.Result));
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-4")]
    public async Task Arguments_that_do_not_fit_and_tools_not_offered_reach_neither_the_rules_nor_the_host()
    {
        var (host, runner) = Rig();
        var bad = await Call(runner, "start_mission", JSONValue.FromDictionaryLiteral(("title", "t"), ("brief", "b"), ("owner_approved", true)));
        var missing = await Call(runner, "remember", JSONValue.FromDictionaryLiteral());
        var unknown = await Call(runner, "grant_everything", JSONValue.FromDictionaryLiteral());
        Assert.Equal("Not run: start_mission takes no argument \"owner_approved\".", bad.Text);
        Assert.StartsWith("Not run: remember needs \"note\"", missing.Text, StringComparison.Ordinal);
        Assert.Equal("Not run: \"grant_everything\" is not an nth-dextra tool this chat can use.", unknown.Text);
        Assert.True(bad.IsError && missing.IsError && unknown.IsError);
        Assert.DoesNotContain(host.Log, l => l.StartsWith("policy:", StringComparison.Ordinal) || l.StartsWith("run:", StringComparison.Ordinal));
        Assert.Equal(new[] { "refused: invalid arguments", "refused: invalid arguments", "refused: unknown tool" }, host.Audits.Select(a => a.Result));
        Assert.All(host.Audits, a => Assert.Equal(new[] { "route" }, a.Inputs.Select(i => i.Name)));

        var (agentHost, agent) = Rig(AssistantChatRole.Mission);
        var notOffered = await Call(agent, "start_mission");
        Assert.True(notOffered.IsError);
        Assert.Empty(agentHost.Runs);
        Assert.False((await Call(agent, "report")).IsError);
        Assert.Single(agentHost.Runs);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-4")]
    public async Task A_tainted_context_is_refused_the_listed_tools_quarantines_notes_and_delegates_data_only()
    {
        var (host, runner) = Rig();
        host.Tainted = true;
        foreach (var name in new[] { "schedule_duty", "watch_folder", "watch_page", "create_assistant", "propose_capability", "suggest" })
        {
            var result = await Call(runner, name);
            Assert.True(result.IsError, name);
            Assert.StartsWith("Refused: needs an untainted request.", result.Text, StringComparison.Ordinal);
        }
        Assert.Empty(host.Runs);
        Assert.Empty(host.Asks);
        Assert.Equal(new[] { "schedule_duty", "watch_folder", "watch_page", "create_assistant", "propose_capability", "suggest" }, host.Offers.Select(c => c.Tool.Name));

        Assert.False((await Call(runner, "remember")).IsError);
        var note = Assert.Single(host.Runs);
        Assert.True(note.Quarantine);
        Assert.False(note.DataOnly);

        foreach (var name in new[] { "delegate_to", "start_mission", "spawn_chat", "launch_hydra_team", "message_agent" })
        {
            host.Runs.Clear();
            await Call(runner, name);
            var delegated = Assert.Single(host.Runs);
            Assert.True(delegated.DataOnly && delegated.ChildStartsTainted, name);
        }
        Assert.Equal("Waiting for the owner", (await Call(runner, "forget")).Text[..21]);
        Assert.Equal("forget", host.Asks.Last().Tool.Name);

        // The same calls from a clean context: configuration is a card, never refused, and nothing is quarantined.
        var (clean, cleanRunner) = Rig();
        await Call(cleanRunner, "schedule_duty");
        await Call(cleanRunner, "remember");
        await Call(cleanRunner, "delegate_to");
        Assert.Equal("schedule_duty", Assert.Single(clean.Asks).Tool.Name);
        Assert.Empty(clean.Offers);
        Assert.All(clean.Runs, c => Assert.False(c.Quarantine || c.DataOnly || c.ChildStartsTainted));
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-4")]
    public async Task Until_auto_answering_is_on_an_allow_runs_only_for_reads_stops_and_words_to_the_owner_or_the_parent()
    {
        var (host, runner) = Rig();
        host.AutoAnswering = false;
        foreach (var tool in AssistantToolCatalog.All) await Call(runner, tool.Name);
        Assert.Equal(new[] { "list_sessions", "read_session", "stop_agent", "ask_owner", "recall", "usage_status", "plan_budget", "report" },
            host.Runs.Select(c => c.Tool.Name));
        Assert.Equal(16, host.Asks.Count);
        Assert.All(host.Asks.Where(c => c.Decision.Rule == PolicyRule.AutoAnswerOff), c => Assert.Empty(c.Decision.GrantsUsed));
        Assert.Equal(11, host.Asks.Count(c => c.Decision.Rule == PolicyRule.AutoAnswerOff));
        var card = host.Audits.Single(a => a.Tool == "nth-dextra/start_mission");
        Assert.Equal("asked: AutoAnswerOff", card.Result);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-4")]
    public async Task Rules_that_cannot_be_read_a_log_that_cannot_be_written_or_a_card_that_cannot_be_raised_run_nothing()
    {
        var (host, runner) = Rig();
        host.PolicyThrows = true;
        Assert.Equal("Not run: nth could not check its rules for this call.", (await Call(runner, "recall")).Text);
        host.PolicyThrows = false;
        host.AuditThrows = true;
        Assert.Equal("Not run: the activity log could not be written.", (await Call(runner, "recall")).Text);
        host.AuditThrows = false;
        host.AskThrows = true;
        Assert.Equal("Not run: the owner's card could not be raised.", (await Call(runner, "schedule_duty")).Text);
        Assert.Empty(host.Runs);

        // A host that throws while running is a failed call, not a crash.
        var (failing, failingRunner) = Rig();
        failing.Answer = _ => throw new InvalidOperationException("no mission store");
        var failed = await Call(failingRunner, "recall");
        Assert.Equal((true, "Failed: no mission store"), (failed.IsError, failed.Text));
        Assert.Equal("failed", failing.Audits.Last().Result);

        // A revoked chat runs nothing.
        var (revoked, revokedRunner) = Rig();
        AssistantMcpBridge.Forget(revokedRunner.Registration.Chat.Thread);
        Assert.Equal("Not run: this chat's nth-dextra tools are turned off.", (await Call(revokedRunner, "recall")).Text);
        Assert.Empty(revoked.Log);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-4")]
    public async Task The_audit_line_names_the_chat_tool_class_and_rule_and_keeps_only_the_length_of_what_the_agent_wrote()
    {
        var (host, runner) = Rig();
        const string brief = "Collect every invoice from the shared drive and total them by vendor.";
        await Call(runner, "start_mission", JSONValue.FromDictionaryLiteral(("title", "Invoices"), ("brief", brief), ("priority", "high"), ("budget_usd", 3.5)));
        var chat = runner.Registration.Chat;
        Assert.Equal(2, host.Audits.Count);
        var (decided, done) = (host.Audits[0], host.Audits[1]);
        Assert.Equal(("dextra", (string?)null, chat.Thread.ToString("D"), "nth-dextra/start_mission", "Agents", "start_mission", "allowed", "rule:Alone"),
            (decided.Assistant, decided.Mission, decided.Agent, decided.Tool, decided.ActionClass, decided.Target, decided.Result, decided.AllowedBy));
        Assert.Equal("done", done.Result);
        var inputs = decided.Inputs.ToDictionary(i => i.Name);
        Assert.Equal(new[] { "call", "title", "brief", "priority", "budget_usd", "route" }, decided.Inputs.Select(i => i.Name));
        Assert.Equal((AuditInputKind.Typed, brief.Length), (inputs["brief"].Kind, inputs["brief"].TypedLength));
        Assert.Equal((AuditInputKind.Typed, 8), (inputs["title"].Kind, inputs["title"].TypedLength));
        Assert.Equal((AuditInputKind.Text, "high"), (inputs["priority"].Kind, inputs["priority"].Text));
        Assert.Equal((AuditInputKind.Number, 3.5), (inputs["budget_usd"].Kind, inputs["budget_usd"].Number));
        Assert.Equal("Mcp", inputs["route"].Text);
        Assert.Equal(decided.Inputs[0].Text, done.Inputs[0].Text);
        Assert.DoesNotContain("invoice", JsonSerializer.Serialize(host.Audits), StringComparison.OrdinalIgnoreCase);
    }
}

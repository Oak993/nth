using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// How the runtime takes provider events and closes a turn around them: ThreadRuntime.swift:2185-2210 (B3 numbering; the
// records cite the B2 lines :2185-2205 and :2242-2247) and finishTurn at :2313-2475. The events go through the runtime's
// real handle path (Rehearse) while a turn runs over an offline scripted session.
public sealed class ThreadRuntimeProviderEventTests
{
    private const string PatchOne = "diff --git a/f.txt b/f.txt\n--- a/f.txt\n+++ b/f.txt\n@@ -1 +1 @@\n-a\n+b\n";
    private const string PatchTwo = "diff --git a/f.txt b/f.txt\n--- a/f.txt\n+++ b/f.txt\n@@ -1 +1 @@\n-a\n+c\n";

    private static void Emit(ThreadRuntimeTurnRig rig, ProviderEvent value) => rig.H.Main.OnMain(() => rig.Runtime.Rehearse(value));

    /// <summary>Runs <paramref name="late"/> inside finishTurn, right after it cleared the approvals: currentTurnID is nil by
    /// then and closingTurnID is set (ThreadRuntime.swift:2335-2339 and :2297-2298).</summary>
    private static void InTheClosingWindow(ThreadRuntimeTurnRig rig, Action late)
    {
        Emit(rig, new ProviderEvent.Approval(new ApprovalRequest("req-1", ApprovalRequest.Kind.Tool, "Allow?", ValueList<ApprovalRequest.Option>.Empty)));
        var fired = false;
        rig.Runtime.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != "Approvals" || fired) return;
            fired = true;
            late();
        };
    }

    // ---- .diff (PR-RUNTIME-094) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-094")] // :2185-2186 last one wins while a turn runs; finishTurn writes it onto the turn
    public void The_latest_provider_diff_of_the_running_turn_is_recorded_on_it()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.Send("go");

        Emit(rig, new ProviderEvent.Diff(PatchOne));
        Emit(rig, new ProviderEvent.Diff(PatchTwo));
        rig.Complete();

        Assert.Equal(PatchTwo, Assert.Single(rig.Runtime.Turns).ProviderDiff);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-094")] // :2186 `if currentTurnID != nil`: outside a turn it is dropped, and the next turn does not inherit it
    public void A_provider_diff_arriving_outside_a_turn_is_dropped()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Emit(rig, new ProviderEvent.Diff(PatchOne));

        rig.Send("go");
        rig.Complete();

        Assert.Null(Assert.Single(rig.Runtime.Turns).ProviderDiff);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-094")] // :2186 tests currentTurnID, not turnIDForNewRows: a diff in finishTurn's closing window is dropped
    [Trait("Req", "PR-RUNTIME-101")] // :2205 the same drop window for the resume anchor
    public void A_diff_and_an_anchor_arriving_while_the_turn_is_closing_are_dropped()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.Send("go");
        Emit(rig, new ProviderEvent.Diff(PatchOne));
        Emit(rig, new ProviderEvent.AssistantMessageID("anchor-in-turn"));
        InTheClosingWindow(rig, () =>
        {
            rig.Runtime.Rehearse(new ProviderEvent.Diff(PatchTwo));
            rig.Runtime.Rehearse(new ProviderEvent.AssistantMessageID("anchor-late"));
        });

        rig.Complete();

        var turn = Assert.Single(rig.Runtime.Turns);
        Assert.Equal(PatchOne, turn.ProviderDiff);
        Assert.Equal("anchor-in-turn", turn.ProviderAnchor);
    }

    // ---- .assistantMessageID (PR-RUNTIME-101) and the resume anchor (PR-RUNTIME-118) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-101")] // :2204-2205 last one wins; consumed by finishTurn onto TurnRecord.providerAnchor
    public void The_latest_anchor_of_the_running_turn_is_recorded_on_it_and_an_anchor_outside_a_turn_is_dropped()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Emit(rig, new ProviderEvent.AssistantMessageID("before-any-turn"));
        rig.Send("go");
        Emit(rig, new ProviderEvent.AssistantMessageID("anchor-1"));
        Emit(rig, new ProviderEvent.AssistantMessageID("anchor-2"));
        rig.Complete();

        Assert.Equal("anchor-2", Assert.Single(rig.Runtime.Turns).ProviderAnchor);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-118")] // :2338-2340 an existing providerResumeAt moves forward to the turn's anchor
    public void A_thread_that_already_has_a_resume_anchor_has_it_advanced_by_the_turn()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with { ProviderResumeAt = "old-anchor" });
        rig.Send("go");
        Emit(rig, new ProviderEvent.AssistantMessageID("new-anchor"));
        rig.Complete();

        Assert.Equal("new-anchor", rig.Thread.ProviderResumeAt);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-118")] // :2339 `thread?.providerResumeAt != nil`: never created here
    public void A_thread_that_was_never_rewound_stays_without_a_resume_anchor()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.Send("go");
        Emit(rig, new ProviderEvent.AssistantMessageID("new-anchor"));
        rig.Complete();

        Assert.Null(rig.Thread.ProviderResumeAt);
        Assert.Equal("new-anchor", Assert.Single(rig.Runtime.Turns).ProviderAnchor);
    }

    // ---- the closing window (PR-RUNTIME-113) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-113")] // :2297-2298 rows created while the turn is closing are filed under it (turnIDForNewRows)
    public void A_notice_that_arrives_while_the_turn_is_closing_belongs_to_that_turn()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.Send("go");
        InTheClosingWindow(rig, () => rig.Runtime.Rehearse(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "late note" })));

        rig.Complete();

        var turn = Assert.Single(rig.Runtime.Turns);
        var late = Assert.Single(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.Notice { Value.Message: "late note" });
        Assert.Equal(turn.Id, late.Item.TurnID);
    }

    // ---- .models and .commands (PR-RUNTIME-098, PR-RUNTIME-099) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-098")] // :2193-2194 the list goes to the registry; the current-model payload is discarded
    public void A_model_list_updates_the_providers_catalog_and_never_changes_the_threads_model()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Codex);
        var list = new ValueList<ModelOption>([new ModelOption("m-one", "One"), new ModelOption("m-two", "Two")]);

        Emit(rig, new ProviderEvent.Models(list, "m-two"));

        Assert.Equal(list, rig.H.Main.OnMain(() => rig.App.Providers.Models(ProviderKind.Codex)));
        Assert.Equal("offline-model", rig.Thread.Model);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-099")] // :2195-2198 stored against the provider and the project path
    public void A_command_list_is_stored_for_the_providers_project_directory()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        var list = new ValueList<SlashCommand>([new SlashCommand { Name = "review", Detail = "Review" }]);

        Emit(rig, new ProviderEvent.Commands(list));

        var commands = rig.H.Main.OnMain(() => rig.App.Providers.Commands);
        Assert.Equal(list, commands[ProviderRegistry.CommandKey(ProviderKind.Claude, rig.Project.Path)]);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-099")] // :2197 `thread.worktreePath ?? project.path`
    public void A_command_list_is_stored_for_the_threads_worktree_when_it_has_one()
    {
        var worktree = Path.Combine(Path.GetTempPath(), "tf-runtime-events-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(worktree);
        try
        {
            using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with { WorktreePath = worktree });
            var list = new ValueList<SlashCommand>([new SlashCommand { Name = "review", Detail = "Review" }]);

            Emit(rig, new ProviderEvent.Commands(list));

            var commands = rig.H.Main.OnMain(() => rig.App.Providers.Commands);
            Assert.Equal(list, commands[ProviderRegistry.CommandKey(ProviderKind.Claude, worktree)]);
            Assert.False(commands.ContainsKey(ProviderRegistry.CommandKey(ProviderKind.Claude, rig.Project.Path)));
        }
        finally
        {
            try { Directory.Delete(worktree, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // ---- .agentProgress (PR-RUNTIME-106) ----

    private static (ThreadRuntimeTurnRig Rig, ThreadRuntime Head) RigWithNativeHead()
    {
        var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, hydraOn: true, settings: app => app.Settings.HydraIsolateHeads = false);
        Emit(rig, new ProviderEvent.AgentStarted(new AgentSpawn { Id = "agent-1", Description = "Read the packet", Prompt = "brief", IsBackground = true }));
        var head = Assert.Single(rig.H.Main.OnMain(() => rig.App.HydraHeads(rig.InitialThread.Id).ToArray()));
        return (rig, rig.H.Main.OnMain(() => rig.App.Runtime(head.Id)));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-106")] // :2242-2247 a non-empty summary wins; an empty or missing one falls through to the last tool
    public void A_non_empty_summary_names_the_heads_activity_and_otherwise_its_last_tool()
    {
        var (rig, head) = RigWithNativeHead();
        using var _ = rig;

        Emit(rig, new ProviderEvent.AgentProgress("agent-1", "Reading the packet", "Read", null, null));
        Assert.Equal("Reading the packet", head.HydraActivity);
        Emit(rig, new ProviderEvent.AgentProgress("agent-1", "", "Grep", null, null));
        Assert.Equal("Grep", head.HydraActivity);
        Emit(rig, new ProviderEvent.AgentProgress("agent-1", "Writing", "Edit", null, null));
        Assert.Equal("Writing", head.HydraActivity);
        Emit(rig, new ProviderEvent.AgentProgress("agent-1", null, "Bash", null, null));
        Assert.Equal("Bash", head.HydraActivity);
        Emit(rig, new ProviderEvent.AgentProgress("agent-1", null, null, null, null));
        Assert.Equal("Bash", head.HydraActivity);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-106")] // :2245-2247 tokens and toolCalls are plain assignments, not maxima; a nil leaves the value; the clock is stamped
    public void Token_and_tool_counts_are_assigned_as_reported_even_when_they_go_down()
    {
        var (rig, head) = RigWithNativeHead();
        using var _ = rig;

        Emit(rig, new ProviderEvent.AgentProgress("agent-1", null, null, 900, 9));
        Assert.Equal((900L, 9L), (head.HydraTokens, head.HydraToolCalls));
        Emit(rig, new ProviderEvent.AgentProgress("agent-1", null, null, 400, 3));
        Assert.Equal((400L, 3L), (head.HydraTokens, head.HydraToolCalls));
        rig.H.Main.Advance(TimeSpan.FromSeconds(5));
        Emit(rig, new ProviderEvent.AgentProgress("agent-1", null, null, null, null));
        Assert.Equal((400L, 3L), (head.HydraTokens, head.HydraToolCalls));
        Assert.Equal(SwiftDate.Now, head.HydraLastEventAt);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-106")] // :2242 `guard let headID = hydraNativeHeads[agentID]`: progress for an unknown agent changes nothing
    public void Progress_for_an_agent_that_is_not_a_head_changes_nothing()
    {
        var (rig, head) = RigWithNativeHead();
        using var _ = rig;

        Emit(rig, new ProviderEvent.AgentProgress("agent-unknown", "Reading", "Read", 5, 5));

        Assert.Null(head.HydraActivity);
        Assert.Equal(0L, head.HydraTokens);
    }
}

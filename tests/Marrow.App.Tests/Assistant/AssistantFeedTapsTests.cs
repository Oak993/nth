using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Assistant;

// AS-2 (DESIGN.md 5.1 table, 5.4): the one-line taps at ThreadRuntime.Handle, AppModel.TurnFinished and ThreadNeedsAttention,
// ThreadRuntime.HydraHeadFinished and MultiAccountRouting put the right lines on the bus, and the privacy rules decide
// what words they carry. Provider events come from a scripted offline session. Expected lines are written out here.
public sealed class AssistantFeedTapsTests
{
    private const string Title = "Fix the parser";

    private static ThreadRuntimeTurnRig Rig(ProviderKind provider = ProviderKind.Claude, bool hidden = false, Guid? mission = null, Guid? assistant = null) =>
        new(provider, thread => thread with { Title = Title, HasCustomTitle = true, HiddenFromAssistant = hidden, MissionID = mission, AssistantID = assistant },
            settings: app =>
            {
                app.Settings.ChimeWhenFinished = false;
                app.Settings.NotifyWhenFinished = false;
            });

    private static void Emit(ThreadRuntimeTurnRig rig, params ProviderEvent[] events) =>
        rig.H.Main.OnMain(() =>
        {
            foreach (var @event in events) rig.Session.Emit(@event);
        });

    private static async Task<IReadOnlyList<FeedEvent>> Flushed(ThreadRuntimeTurnRig rig, FeedCollector feed, int expected)
    {
        await rig.App.AssistantEvents.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        return await feed.UntilAsync(lines => lines.Count >= expected);
    }

    private static ApprovalRequest Approval(string title) =>
        new("approve-1", ApprovalRequest.Kind.Tool, title, new([new ApprovalRequest.Option("once", "Approve once", ApprovalRequest.Option.Role.Approve)]));

    private static QuestionRequest Question(string prompt, bool secret) =>
        new("question-1", new([new QuestionRequest.Question("q", "Header", prompt, new([new QuestionRequest.Choice("one")]), false, false, secret)]));

    private static ToolCall Tool(ToolCall.Kind kind, string title) => new() { ToolCallKind = kind, Title = title };

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-8")]
    public async Task A_tool_start_names_what_kind_of_tool_and_never_its_command_line()
    {
        using var rig = Rig();
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude);
        using var feed = new FeedCollector(rig.App.AssistantEvents);

        Emit(rig,
            new ProviderEvent.ToolStarted("t1", Tool(ToolCall.Kind.Read, "secrets/passwords.txt")),
            new ProviderEvent.ToolStarted("t2", Tool(ToolCall.Kind.Mcp, "github.create_issue")),
            new ProviderEvent.ToolStarted("t3", Tool(ToolCall.Kind.Mcp, "Create an issue about the leak")));
        rig.H.Main.OnMain(() => rig.App.AssistantTaps.TapProvider(rig.InitialThread.Id,
            new ProviderEvent.ToolStarted("t4", Tool(ToolCall.Kind.Command, "git push --force origin main"))));
        var lines = await Flushed(rig, feed, 4);

        Assert.Equal([$"{Title}: reading a file", $"{Title}: using the tool github.create_issue", $"{Title}: using a connector tool", $"{Title}: running a command"],
            lines.Select(line => line.Summary));
        Assert.All(lines, line => Assert.Equal((FeedEventKind.ToolStarted, FeedSeverity.Quiet, rig.InitialThread.Id), (line.Kind, line.Severity, line.ThreadId)));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-8")]
    public async Task An_approval_is_one_needs_you_line_with_its_title_and_the_attention_call_that_follows_adds_none()
    {
        using var rig = Rig();
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude);
        using var feed = new FeedCollector(rig.App.AssistantEvents);

        Emit(rig, new ProviderEvent.Approval(Approval("Only Config.cs")));
        var first = await Flushed(rig, feed, 1);
        Assert.Single(first);
        Assert.Equal((FeedEventKind.Approval, FeedSeverity.NeedsYou, $"{Title}: waiting for approval. Only Config.cs"), (first[0].Kind, first[0].Severity, first[0].Summary));

        rig.H.Main.OnMain(() => rig.App.ThreadNeedsAttention(rig.InitialThread.Id));
        var second = await Flushed(rig, feed, 2);
        Assert.Equal((FeedEventKind.NeedsAttention, FeedSeverity.NeedsYou, $"{Title}: waiting for your decision"), (second[1].Kind, second[1].Severity, second[1].Summary));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-8")]
    public async Task A_question_carries_its_prompt_unless_the_answer_is_a_secret()
    {
        using var rig = Rig();
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude);
        using var feed = new FeedCollector(rig.App.AssistantEvents);

        Emit(rig, new ProviderEvent.Question(Question("Which database?", false)));
        Emit(rig, new ProviderEvent.Question(Question("Paste your API key", true)));
        var lines = await Flushed(rig, feed, 2);

        Assert.Equal([$"{Title}: asked a question. Which database?", $"{Title}: asked a question"], lines.Select(line => line.Summary));
        Assert.All(lines, line => Assert.Equal(FeedSeverity.NeedsYou, line.Severity));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public async Task Token_spend_events_fold_into_one_line_with_the_cost_delta()
    {
        using var rig = Rig();
        using var feed = new FeedCollector(rig.App.AssistantEvents);

        Emit(rig,
            new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Claude, 100)),
            new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Claude, 200)),
            new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Claude, 300)));
        var lines = await Flushed(rig, feed, 1);

        var line = Assert.Single(lines);
        Assert.Equal((FeedEventKind.TokenSpend, FeedSeverity.Quiet, $"{Title}: used 600 tokens", 600L, 3), (line.Kind, line.Severity, line.Summary, line.CostDelta, line.Count));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-8")]
    public async Task A_helper_is_reported_when_it_starts_works_and_finishes()
    {
        using var rig = Rig();
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude);
        using var feed = new FeedCollector(rig.App.AssistantEvents);

        Emit(rig,
            new ProviderEvent.AgentStarted(new AgentSpawn { Id = "a1", Description = "Review the diff" }),
            new ProviderEvent.AgentProgress("a1", "reading files", null, null, null),
            new ProviderEvent.AgentProgress("a1", null, "Read", null, null),
            new ProviderEvent.AgentFinished("a1", TurnStatus.Completed, "Found two issues"),
            new ProviderEvent.AgentFinished("a2", TurnStatus.Failed, "Ran out of budget"));
        var lines = await Flushed(rig, feed, 4);

        Assert.Equal([$"{Title}: started a helper. Review the diff", $"{Title}: helper working. Read", $"{Title}: helper finished. Found two issues", $"{Title}: helper failed. Ran out of budget"],
            lines.Select(line => line.Summary));
        Assert.Equal([FeedSeverity.Quiet, FeedSeverity.Quiet, FeedSeverity.Info, FeedSeverity.Error], lines.Select(line => line.Severity));
        Assert.Equal(2, lines[1].Count);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-8")]
    public async Task A_failed_turn_gives_an_error_line_with_the_error_and_then_the_turn_line()
    {
        using var rig = Rig();
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude);
        rig.Send("Fix it.");
        using var feed = new FeedCollector(rig.App.AssistantEvents);

        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Failed, "Connection reset by peer")));
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        var lines = await Flushed(rig, feed, 2);

        Assert.Equal([$"{Title}: hit an error. Connection reset by peer", $"{Title}: stopped with an error"], lines.Select(line => line.Summary));
        Assert.Equal([FeedEventKind.Error, FeedEventKind.TurnFinished], lines.Select(line => line.Kind));
        Assert.All(lines, line => Assert.Equal(FeedSeverity.Error, line.Severity));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-8")]
    public async Task A_finished_turn_gives_its_status_and_the_first_words_of_the_last_reply()
    {
        using var rig = Rig();
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude);
        rig.Send("Fix it.");
        using var feed = new FeedCollector(rig.App.AssistantEvents);

        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.MessageCompleted("reply", "Replaced the parser.\nAll 14 tests pass. Key is sk-proj-AbCdEfGhIjKlMnOpQrStUv1234.")));
        rig.Complete();
        var lines = await Flushed(rig, feed, 1);

        var line = Assert.Single(lines);
        Assert.Equal($"{Title}: finished. Replaced the parser. All 14 tests pass. Key is [secret].", line.Summary);
        Assert.Equal((FeedEventKind.TurnFinished, FeedSeverity.Info, FeedTextAccess.Full), (line.Kind, line.Severity, line.Text));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-8")]
    public async Task A_turn_with_another_one_coming_is_quiet_and_a_stopped_turn_says_stopped()
    {
        using var rig = Rig();
        using var feed = new FeedCollector(rig.App.AssistantEvents);

        rig.H.Main.OnMain(() =>
        {
            rig.App.TurnFinished(rig.InitialThread.Id, TurnStatus.Completed, true);
            rig.App.TurnFinished(rig.InitialThread.Id, TurnStatus.Interrupted, false);
        });
        var lines = await Flushed(rig, feed, 2);

        Assert.Equal([$"{Title}: finished, more to come", $"{Title}: stopped"], lines.Select(line => line.Summary));
        Assert.Equal([FeedSeverity.Quiet, FeedSeverity.Info], lines.Select(line => line.Severity));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-8")]
    public async Task A_heads_own_turn_gives_no_line_because_its_report_comes_through_the_head_tap()
    {
        using var rig = new AppModelNotifyRig(project =>
        {
            var lead = AppModelSamples.Thread(project) with { Title = "Lead" };
            var head = AppModelSamples.Thread(project) with
            {
                Title = "Head",
                ParentThreadID = lead.Id,
                IsInPanel = true,
                Hydra = new HydraHeadInfo(0, "task", HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Delegated),
            };
            return [lead, head];
        });
        rig.Configure(chime: false, notify: false);
        using var feed = new FeedCollector(rig.Model.AssistantEvents);

        rig.Finish(rig.Threads[1].Id, TurnStatus.Completed);
        rig.Finish(rig.Threads[0].Id, TurnStatus.Completed);
        await rig.Model.AssistantEvents.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var lines = await feed.UntilAsync(seen => seen.Count >= 1);

        Assert.Equal(["Lead: finished"], lines.Select(line => line.Summary));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-8")]
    public async Task A_finished_head_reports_its_name_and_summary()
    {
        using var rig = Rig();
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude);
        using var feed = new FeedCollector(rig.App.AssistantEvents);
        var info = new HydraHeadInfo(0, "Write the subtitles", HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Delegated);

        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.HydraHeadFinished(Guid.NewGuid(), info, HydraHeadInfo.Status.Completed, "Subtitles done in 3 files");
            rig.Runtime.HydraHeadFinished(Guid.NewGuid(), info, HydraHeadInfo.Status.Failed, null);
        });
        var lines = await Flushed(rig, feed, 2);

        Assert.Equal([$"{Title}: Hank finished. Subtitles done in 3 files", $"{Title}: Hank failed. Write the subtitles"], lines.Select(line => line.Summary));
        Assert.Equal([FeedSeverity.Info, FeedSeverity.Error], lines.Select(line => line.Severity));
        Assert.All(lines, line => Assert.Equal(FeedEventKind.HeadFinished, line.Kind));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public async Task A_hidden_chat_puts_only_two_fixed_lines_on_the_bus_and_none_of_its_words()
    {
        using var rig = Rig(hidden: true);
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude, AlsoOtherProviders: true);
        rig.Send("Fix it.");
        using var feed = new FeedCollector(rig.App.AssistantEvents);

        Emit(rig,
            new ProviderEvent.ToolStarted("t1", Tool(ToolCall.Kind.Read, "plans/secret.txt")),
            new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Claude, 100)),
            new ProviderEvent.Approval(Approval("Delete plans/secret.txt")),
            new ProviderEvent.MessageCompleted("reply", "The plan is in plans/secret.txt"));
        rig.Complete();
        var lines = await Flushed(rig, feed, 2);

        Assert.Equal(["A hidden chat needs you", "A hidden chat finished a turn"], lines.Select(line => line.Summary));
        Assert.All(lines, line => Assert.Null(line.ThreadId));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public async Task A_chat_on_another_provider_gives_metadata_only_until_the_owner_allows_it()
    {
        using var rig = Rig(ProviderKind.Codex);
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude);
        using var feed = new FeedCollector(rig.App.AssistantEvents);

        Emit(rig, new ProviderEvent.Approval(Approval("Only Config.cs")));
        await Flushed(rig, feed, 1);
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude, AlsoOtherProviders: true);
        Emit(rig, new ProviderEvent.Approval(Approval("Only Config.cs")));
        var lines = await Flushed(rig, feed, 2);

        Assert.Equal([$"{Title}: waiting for approval", $"{Title}: waiting for approval. Only Config.cs"], lines.Select(line => line.Summary));
        Assert.Equal([FeedTextAccess.MetadataOnly, FeedTextAccess.Full], lines.Select(line => line.Text));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-3")]
    public async Task A_chat_on_a_local_model_is_never_summarised_to_a_cloud_brain()
    {
        using var rig = Rig(ProviderKind.Local);
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude, AlsoOtherProviders: true);
        using var feed = new FeedCollector(rig.App.AssistantEvents);

        Emit(rig, new ProviderEvent.Approval(Approval("Only Config.cs")));
        await Flushed(rig, feed, 1);
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Local, RunsLocally: true);
        Emit(rig, new ProviderEvent.Approval(Approval("Only Config.cs")));
        var lines = await Flushed(rig, feed, 2);

        Assert.Equal([$"{Title}: waiting for approval", $"{Title}: waiting for approval. Only Config.cs"], lines.Select(line => line.Summary));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public async Task A_mission_chat_keeps_its_ids_and_cannot_be_hidden()
    {
        var mission = Guid.Parse("22222222-0000-0000-0000-000000000002");
        var assistant = Guid.Parse("33333333-0000-0000-0000-000000000003");
        using var rig = Rig(hidden: true, mission: mission, assistant: assistant);
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude);
        using var feed = new FeedCollector(rig.App.AssistantEvents);

        Emit(rig, new ProviderEvent.Approval(Approval("Only Config.cs")));
        var lines = await Flushed(rig, feed, 1);

        var line = Assert.Single(lines);
        Assert.Equal(($"{Title}: waiting for approval. Only Config.cs", mission, assistant, rig.InitialThread.Id), (line.Summary, line.MissionId, line.AssistantId, line.ThreadId));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-7")]
    public async Task Account_polls_give_percent_changes_and_a_limit_line_and_a_provider_report_of_the_same_limit_adds_none()
    {
        using var rig = Rig();
        using var feed = new FeedCollector(rig.App.AssistantEvents);
        var key = UsageAccountKey.Default(ProviderKind.Claude);
        var now = DateTimeOffset.UtcNow;
        var resets = now.AddMinutes(125);
        AccountPlanLimitsSnapshot Poll(double percent, int minutes) => new(key,
            new PlanLimits { Windows = new ValueList<PlanLimits.Window>([new PlanLimits.Window { Id = "five_hour", Title = "5-hour window", Percent = percent, ResetsAt = resets }]) },
            now.AddMinutes(minutes));

        rig.H.Main.OnMain(() =>
        {
            rig.App.AssistantTaps.TapAccountSnapshot(Poll(40, 0));
            rig.App.AssistantTaps.TapAccountSnapshot(Poll(40.4, 1));
            rig.App.AssistantTaps.TapAccountSnapshot(Poll(44, 2));
            rig.App.AssistantTaps.TapAccountSnapshot(Poll(100, 3));
            rig.App.AccountRouting.NoteLimit(rig.Runtime, null, resets);
        });
        var lines = await Flushed(rig, feed, 2);

        Assert.Equal(["Claude: 5-hour window at 44%, up 4%", "Claude: usage limit reached, resets in 2 h 5 min"], lines.Select(line => line.Summary));
        Assert.Equal([FeedEventKind.AccountWindow, FeedEventKind.AccountLimitReached], lines.Select(line => line.Kind));
        Assert.All(lines, line => Assert.Null(line.ThreadId));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-8")]
    public async Task A_usage_limit_in_a_chat_says_when_it_resets()
    {
        using var rig = Rig();
        using var feed = new FeedCollector(rig.App.AssistantEvents);

        rig.H.Main.OnMain(() => rig.App.AssistantTaps.TapProvider(rig.InitialThread.Id, new ProviderEvent.UsageLimit(DateTimeOffset.UtcNow.AddMinutes(45))));
        var lines = await Flushed(rig, feed, 1);

        var line = Assert.Single(lines);
        Assert.Equal(($"{Title}: hit a usage limit, resets in 45 min", FeedSeverity.Info), (line.Summary, line.Severity));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-6")]
    public void With_nobody_listening_the_taps_queue_nothing_and_an_unknown_chat_is_ignored()
    {
        using var rig = Rig();

        Emit(rig,
            new ProviderEvent.TurnStarted("turn-1"),
            new ProviderEvent.Approval(Approval("Only Config.cs")),
            new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Claude, 100)));
        rig.H.Main.OnMain(() =>
        {
            rig.App.TurnFinished(rig.InitialThread.Id, TurnStatus.Completed, false);
            rig.App.AssistantTaps.TapProvider(Guid.NewGuid(), new ProviderEvent.TurnStarted("turn-2"));
        });

        Assert.Equal(0, rig.App.AssistantEvents.Pending);
        Assert.Equal(0, rig.App.AssistantEvents.FaultCount);
    }
}

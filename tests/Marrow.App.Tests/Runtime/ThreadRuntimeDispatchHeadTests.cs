using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.App.Tests.Runtime;

// ThreadRuntime.dispatchHead (App/Runtime/ThreadRuntime.swift:1027-1039), the one body both head-dispatch paths share.
// Before Job STUBS the port's DispatchHead returned false after its first two gates, under a BLOCKED comment that
// said AppModel had no hydraLaunch, runningDroppyHeads, hydraChatContext or spawnDroppyHead; all four exist.
// Every test goes through the private DispatchHead by reflection (Marrow.App has no InternalsVisibleTo, ED-119)
// on a real ThreadRuntime of a real AppModel, and reads the head that spawned from the model, as the Hydra tests do.
public sealed class ThreadRuntimeDispatchHeadTests
{
    private const string Prompt = "Rename the helper in Parser.cs and update every one of its callers across the whole solution";

    private sealed class Rig(AppModelHarness harness, AppModel model, ThreadRuntime runtime, ChatThread lead) : IDisposable
    {
        public AppModelHarness Harness { get; } = harness;
        public AppModel Model { get; } = model;
        public ThreadRuntime Runtime { get; } = runtime;
        public ChatThread Lead { get; } = lead;
        public void Dispose() => Harness.Dispose();

        public bool Dispatch(FollowUpPrompt prompt, HydraHeadInfo.Origin origin) =>
            Harness.Main.OnMain(() => (bool)typeof(ThreadRuntime)
                .GetMethod("DispatchHead", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Runtime, [prompt, origin])!);

        public IReadOnlyList<ChatThread> Heads => Harness.Main.OnMain(() => Model.HydraHeads(Lead.Id).ToList());

        public void SetPhase(RuntimePhase phase) => Harness.Main.OnMain(() =>
            typeof(ThreadRuntime).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Runtime, phase));

        // The text of the brief the spawned head was sent, once its own start has run.
        public string Brief(ChatThread head)
        {
            string? text = null;
            Harness.Main.Loop.RunUntil(() =>
            {
                text = Model.ExistingRuntime(head.Id)?.Entries.Select(entry => entry.Item.TimelineItemContent)
                    .OfType<TimelineItem.Content.User>().Select(user => user.Value).FirstOrDefault(message => message.HydraBrief == true)?.Text;
                return text is not null;
            }, AppModelHarness.Bound);
            return text!;
        }
    }

    private static Rig MakeRig(Action<AppModel>? configure = null, Func<ChatThread, ChatThread>? shape = null,
        IReadOnlyList<TimelineItem>? timeline = null, IReadOnlyList<TurnRecord>? turns = null)
    {
        var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Project", harness.NewFolder("project"));
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true };
        if (shape is not null) lead = shape(lead);
        harness.WriteLibrary([project], [lead]);
        if (timeline is not null)
        {
            DiskWriter.Shared.WriteSynchronously(
                new ThreadDocument(lead.Id)
                {
                    Items = new ValueList<TimelineItem>(timeline),
                    Turns = new ValueList<TurnRecord>(turns ?? []),
                },
                Storage.ThreadURL(lead.Id));
        }
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraIsolateHeads = false;
            configure?.Invoke(model);
        });
        harness.PrepareBootstrap(model);
        var runtime = harness.Main.OnMain(() => model.Runtime(lead.Id));
        harness.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return new Rig(harness, model, runtime, lead);
    }

    private static FollowUpPrompt Follow(string text, params Attachment[] attachments) =>
        new() { Text = text, Attachments = new ValueList<Attachment>(attachments) };

    [Fact]
    [Trait("Req", "PR-RUNTIME-237")] // ThreadRuntime.swift:1029-1031 guard: thread.isArchived
    public void An_archived_thread_never_sends_heads()
    {
        using var rig = MakeRig(shape: lead => lead with { IsArchived = true });
        Assert.False(rig.Dispatch(Follow(Prompt), HydraHeadInfo.Origin.Sent));
        Assert.Empty(rig.Heads);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-237")] // ThreadRuntime.swift:1029-1031 guard: app.hydraLaunch(for: thread)
    public void A_thread_with_no_Hydra_launch_sends_no_head()
    {
        using var rig = MakeRig(configure: model => model.Settings.HydraEnabled = false);
        Assert.False(rig.Dispatch(Follow(Prompt), HydraHeadInfo.Origin.Sent));
        Assert.Empty(rig.Heads);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-237")] // ThreadRuntime.swift:1029-1031 guard: launch.hasRoom(running: app.runningDroppyHeads(of: threadID))
    public void A_pair_with_no_room_returns_false_and_sends_no_second_head()
    {
        using var rig = MakeRig(configure: model => model.Settings.HydraMaxHeads = 1);
        Assert.True(rig.Dispatch(Follow(Prompt), HydraHeadInfo.Origin.Sent)); // one head fits
        Assert.False(rig.Dispatch(Follow("A second task"), HydraHeadInfo.Origin.Sent)); // it is running, the cap is one
        Assert.Single(rig.Heads);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-237")] // ThreadRuntime.swift:1032, :1035-1037: task is singleLine(text, limit: 60); origin and no batch/index/project/profile
    public void The_head_goes_out_titled_by_the_first_sixty_characters_with_the_passed_origin_and_no_batch()
    {
        using var rig = MakeRig();
        var text = string.Join(" ", Enumerable.Repeat("refactor", 20)); // 179 characters, one line
        Assert.True(rig.Dispatch(Follow(text), HydraHeadInfo.Origin.Queued));
        var head = Assert.Single(rig.Heads);
        Assert.Equal(TextCleanup.SingleLine(text, 60), head.Hydra!.Task); // :1032 the 60-character title...
        Assert.True(head.Hydra.Task.Length < text.Length);
        Assert.Equal(HydraHeadInfo.Origin.Queued, head.Hydra.HydraHeadInfoOrigin); // :1035 origin is passed through
        Assert.Null(head.Hydra.BatchID); // ...spawned with no batchID, so its report is flushable at once
        Assert.Equal(rig.Lead.ProjectID, head.ProjectID); // no project name given: the head works in the lead's project
        Assert.Null(head.Hydra.Profile);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-237")] // ThreadRuntime.swift:1033, :1036-1037: the brief carries the full text, the chat as it stands, and an idle lead's wording
    public void An_idle_lead_hands_the_head_the_whole_text_and_the_chat_so_far()
    {
        var turn = Guid.NewGuid();
        using var rig = MakeRig(
            timeline:
            [
                new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = "Make the parser faster" })),
                new TimelineItem(turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "I profiled it and found the hot loop." })),
            ],
            turns: [new TurnRecord { Id = turn, Index = 1, Status = TurnStatus.Completed, CompletedAt = SwiftDate.Now }]);
        Assert.True(rig.Dispatch(Follow(Prompt), HydraHeadInfo.Origin.Sent));
        var brief = rig.Brief(Assert.Single(rig.Heads));
        Assert.Contains("The user sent this task straight to you; the lead is idle and will hear your report.", brief, StringComparison.Ordinal); // :1290
        Assert.Contains("- The user last asked the lead: ", brief, StringComparison.Ordinal);
        Assert.Contains("Make the parser faster", brief, StringComparison.Ordinal);
        Assert.Contains("- The lead's latest reply: ", brief, StringComparison.Ordinal); // idle: no "so far"
        Assert.Contains("I profiled it and found the hot loop.", brief, StringComparison.Ordinal);
        Assert.Contains("## Your task\n" + Prompt + "\n", brief, StringComparison.Ordinal); // the FULL text, not the title
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-237")] // ThreadRuntime.swift:1034: leadIsWorking is phase != .idle, taken at dispatch
    public void A_working_lead_is_named_in_the_brief_as_still_at_work()
    {
        var turn = Guid.NewGuid();
        using var rig = MakeRig(
            timeline: [new TimelineItem(turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "Halfway through the edit." }))],
            turns: [new TurnRecord { Id = turn, Index = 1, Status = TurnStatus.Completed, CompletedAt = SwiftDate.Now }]);
        rig.SetPhase(RuntimePhase.Running);
        Assert.True(rig.Dispatch(Follow(Prompt), HydraHeadInfo.Origin.Queued));
        var brief = rig.Brief(Assert.Single(rig.Heads));
        Assert.Contains("The user queued this task for you while the lead works on something else.", brief, StringComparison.Ordinal); // :1289
        Assert.Contains("- The lead's latest reply so far: ", brief, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-237")] // ThreadRuntime.swift:1035-1036: the prompt's attachments go with the head
    public void The_prompts_attachments_are_sent_with_the_head()
    {
        using var rig = MakeRig();
        var file = Path.Combine(rig.Harness.Scratch, "notes.txt");
        File.WriteAllText(file, "reference");
        var attachment = new Attachment { Name = "notes.txt", Path = file, MimeType = "text/plain" };
        Assert.True(rig.Dispatch(Follow(Prompt, attachment), HydraHeadInfo.Origin.Sent));
        var head = Assert.Single(rig.Heads);
        rig.Brief(head);
        var brief = rig.Model.ExistingRuntime(head.Id)!.Entries.Select(entry => entry.Item.TimelineItemContent)
            .OfType<TimelineItem.Content.User>().Select(user => user.Value).First(message => message.HydraBrief == true);
        Assert.Contains(attachment, brief.Attachments);
    }
}

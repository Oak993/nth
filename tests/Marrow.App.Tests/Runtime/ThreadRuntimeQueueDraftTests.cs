using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Xunit;

namespace Marrow.App.Tests.Runtime;

// Source: DroppyCode/App/Runtime/ThreadRuntime.swift (B3 52bc792), :656, :664-666, :743-755, :767-800, :991-1007,
// :1056-1071, :1116-1137, :1376-1380.
// The chat box's way into the follow-up queue. canQueue (:771-773) is `isRunning || hasWorkingHeads`: a running turn,
// or heads out with the lead waiting on them; it reads neither the draft nor a revert. queueDraftAsFollowUp (:780-788)
// returns on a revert or an empty draft (:781), sends the draft when there is nothing to queue behind (:782-785; "the
// queue chord is never a dead key", :778-779), and otherwise queues the draft's outgoing text and attachments and
// clears the box (:786-787). enqueueFollowUp then ends with flushFollowUpsIfIdle (:1003-1007): "the queue only ever
// waits behind a running turn, never behind an idle lead", so on an idle lead a Task (:1056-1061) drains the queue as
// after a completed turn (:1066-1071, :1116-1137), unless the user stopped the last turn (:1058) or a flush is already
// on its way (:793-796, :1057, :1059). Every expectation is transcribed from those lines; none comes from running the
// port.
// Heads are Droppy-run head threads of the lead, in its panel; a loaded library stops a running head
// (AppModel.swift:132-136), so a test puts a head to work after the model is made.
public sealed class ThreadRuntimeQueueDraftTests
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-031")] // App/Runtime/ThreadRuntime.swift:656, :664-666, :771-773
    public void Can_queue_is_a_running_turn_or_heads_at_work_and_reads_neither_the_draft_nor_a_revert()
    {
        using var h = new AppModelHarness();
        var (model, lead, head) = LeadWithHead(h);
        var runtime = Loaded(h, model, lead);

        h.Main.OnMain(() =>
        {
            // Idle, no head at work: nothing to queue behind, whatever the draft holds.
            runtime.Draft = new ComposerDraft { Text = "next step" };
            Assert.False(runtime.IsRunning);
            Assert.False(runtime.HasWorkingHeads);
            Assert.False(runtime.CanQueue);

            // :664-666, :772 a head at work on an idle lead: the chat box can queue. What it queues does not wait for
            // the head's report, since an idle lead takes it at once (:1003-1007; tested below).
            PutToWork(model, head, HydraHeadInfo.Status.Running);
            Assert.False(runtime.IsRunning);
            Assert.True(runtime.HasWorkingHeads);
            Assert.True(runtime.CanQueue);
            runtime.Draft = new ComposerDraft();
            Assert.True(runtime.CanQueue); // :772 the draft is not read

            PutToWork(model, head, HydraHeadInfo.Status.Completed);
            Assert.False(runtime.HasWorkingHeads);
            Assert.False(runtime.CanQueue);

            // :656, :772 a running turn, the draft still empty.
            runtime.RehearseTurn("working");
            Assert.True(runtime.IsRunning);
            Assert.True(runtime.CanQueue);

            // :772 a revert under way is not read either (queueDraftAsFollowUp guards it on its own, :781).
            SetReverting(runtime);
            Assert.True(runtime.IsReverting);
            Assert.True(runtime.CanQueue);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-105")] // App/Runtime/ThreadRuntime.swift:743-755, :775-785; App/AppCommands.swift:71-74
    public void With_nothing_to_queue_behind_the_draft_simply_goes_out_as_a_sent_message()
    {
        using var h = new AppModelHarness();
        var (model, lead, _) = LeadWithHead(h);
        h.PrepareBootstrap(model); // R-TEST-10: the turn's CLI is a path that does not exist, so no real CLI runs.
        var runtime = Loaded(h, model, lead);

        h.Main.OnMain(() =>
        {
            runtime.Draft = new ComposerDraft { Text = "next step" };
            Assert.False(runtime.CanQueue);

            runtime.QueueDraftAsFollowUp();

            Assert.True(runtime.Draft.IsEmpty); // :751 send() empties the box
            Assert.Empty(runtime.FollowUps); // :782-785 nothing is queued
        });
        // :754 the turn starts on "next step" (the missing CLI then ends it).
        h.Main.Loop.RunUntil(() => runtime.Phase == RuntimePhase.Idle && runtime.Turns.Count == 1, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            Assert.Equal(["next step"], runtime.SentPrompts);
            Assert.Empty(runtime.FollowUps);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-031")] // App/Runtime/ThreadRuntime.swift:664-666, :771-773, :786-787, :991-1000
    public void On_an_idle_lead_with_heads_at_work_the_draft_is_queued_and_the_box_cleared()
    {
        using var h = new AppModelHarness();
        var (model, lead, head) = LeadWithHead(h);
        h.PrepareBootstrap(model); // R-TEST-10, in case anything starts a turn
        var runtime = Loaded(h, model, lead);
        var attachment = new Attachment { Name = "notes.txt", Path = Path.Combine(h.Scratch, "notes.txt"), MimeType = "text/plain" };

        h.Main.OnMain(() =>
        {
            PutToWork(model, head, HydraHeadInfo.Status.Running);
            runtime.Draft = new ComposerDraft { Text = "  after the heads report\n", Attachments = new ValueList<Attachment>([attachment]) };

            runtime.QueueDraftAsFollowUp();

            // :786 enqueueFollowUp(text: draft.outgoingText, attachments: draft.attachments), which trims the text
            // (:993) and appends the prompt (:1000); :787 then clears the box.
            var queued = Assert.Single(runtime.FollowUps);
            Assert.Equal("after the heads report", queued.Text);
            Assert.Equal(attachment, Assert.Single(queued.Attachments));
            Assert.True(runtime.Draft.IsEmpty);
            Assert.Empty(runtime.SentPrompts); // not sent by queueDraftAsFollowUp itself
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-197")] // App/Runtime/ThreadRuntime.swift:1003-1007, :1056-1071, :1116-1137
    [Trait("Req", "PR-COMPOSER-031")] // App/Runtime/ThreadRuntime.swift:771-773, :786-787
    public void A_draft_queued_on_an_idle_lead_with_heads_at_work_goes_out_at_once_as_a_turn_that_started_on_its_own()
    {
        using var h = new AppModelHarness();
        var (model, lead, head) = LeadWithHead(h);
        h.PrepareBootstrap(model); // R-TEST-10: the turn's CLI is a path that does not exist, so no real CLI runs.
        var runtime = Loaded(h, model, lead);

        h.Main.OnMain(() =>
        {
            PutToWork(model, head, HydraHeadInfo.Status.Running);
            runtime.Draft = new ComposerDraft { Text = "after the heads report" };
            Assert.True(runtime.CanQueue); // :772

            runtime.QueueDraftAsFollowUp();

            // :1000 queued, :787 the box cleared; the flush is a Task (:1060), so nothing has gone out yet.
            Assert.Equal(["after the heads report"], runtime.FollowUps.Select(prompt => prompt.Text).ToArray());
            Assert.True(runtime.Draft.IsEmpty);
            Assert.Empty(runtime.SentPrompts);
            Assert.Equal(0L, runtime.AutoStartedTurnToken); // :226
        });
        // :1066-1071 the Task drains the queue as after a completed turn: :1117-1118 takes the prompt out, :1134-1135
        // counts the turn as one that started on its own and :1136 starts it on the prompt's text, with the head still
        // at work. The missing CLI then ends it as failed (:1376-1380).
        h.Main.Loop.RunUntil(() => runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            Assert.Single(runtime.Turns);
            Assert.Equal(["after the heads report"], runtime.SentPrompts);
            Assert.Empty(runtime.FollowUps);
            Assert.Equal(1L, runtime.AutoStartedTurnToken);
            Assert.True(runtime.HasWorkingHeads);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-197")] // App/Runtime/ThreadRuntime.swift:793-796, :1056-1071, :1116-1137, :933-943
    public void Two_prompts_queued_on_an_idle_lead_in_one_pass_start_one_turn_and_the_second_waits_for_its_end()
    {
        using var h = new AppModelHarness();
        var (model, lead, _) = LeadWithHead(h);
        h.PrepareBootstrap(model); // R-TEST-10: the turn's CLI is a path that does not exist, so no real CLI runs.
        var runtime = Loaded(h, model, lead);

        h.Main.OnMain(() =>
        {
            Assert.False(runtime.IsRunning);
            Assert.False(runtime.HasWorkingHeads);

            // :1003-1007 the first schedules the flush (:1059-1060); the second finds it on its way (:1057) and waits
            // its turn rather than starting one of its own, "so two never double-send" (:793-796).
            runtime.EnqueueFollowUp("first step", []);
            runtime.EnqueueFollowUp("second step", []);

            Assert.Equal(["first step", "second step"], runtime.FollowUps.Select(prompt => prompt.Text).ToArray());
            Assert.Empty(runtime.SentPrompts);
        });
        // :1066-1071, :1116-1137 the one Task takes the first prompt (no bundle: :940-941) and starts its turn. The
        // missing CLI ends that turn as failed (:1376-1380), and the queue drains only after a completed turn (:1117),
        // so the second prompt stays queued.
        h.Main.Loop.RunUntil(() => runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            Assert.Single(runtime.Turns);
            Assert.Equal(["first step"], runtime.SentPrompts);
            Assert.Equal(["second step"], runtime.FollowUps.Select(prompt => prompt.Text).ToArray());
            Assert.Equal(1L, runtime.AutoStartedTurnToken); // :1134-1135, once
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-197")] // App/Runtime/ThreadRuntime.swift:1054-1057
    public void A_prompt_queued_behind_a_running_turn_is_not_flushed_and_waits_for_that_turn()
    {
        using var h = new AppModelHarness();
        var (model, lead, _) = LeadWithHead(h);
        h.PrepareBootstrap(model); // R-TEST-10, in case anything starts a turn
        var runtime = Loaded(h, model, lead);

        // :1057 `phase == .idle`: behind a running turn the flush does nothing; that turn's end drains the queue.
        h.Main.OnMain(() =>
        {
            model.Settings.HydraQueueHeads = false; // plain follow-up queue: with Hydra's default queue-heads switch on, ThreadRuntime.swift:999 hands a prompt queued behind a running turn to a Droppy-run head instead (PR-RUNTIME-237)
            runtime.RehearseTurn("working");
            Assert.True(runtime.IsRunning);
            runtime.EnqueueFollowUp("next step", []);
        });
        h.Main.OnMain(() =>
        {
            Assert.Equal(["next step"], runtime.FollowUps.Select(prompt => prompt.Text).ToArray());
            Assert.Equal(["working"], runtime.SentPrompts);
            Assert.Equal(0L, runtime.AutoStartedTurnToken);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-197")] // App/Runtime/ThreadRuntime.swift:1054-1058
    public void On_an_idle_lead_whose_last_turn_the_user_stopped_a_queued_prompt_waits_for_them()
    {
        using var h = new AppModelHarness();
        var (model, lead, _) = LeadWithHead(h);
        h.PrepareBootstrap(model); // R-TEST-10, in case anything starts a turn
        var runtime = Loaded(h, model, lead);

        // :1055, :1058 `thread?.lastStatus != .interrupted`: "A turn the user stopped waits for them instead".
        h.Main.OnMain(() =>
        {
            model.UpdateThread(lead.Id, thread => thread with { LastStatus = TurnStatus.Interrupted });
            Assert.False(runtime.IsRunning);
            runtime.EnqueueFollowUp("next step", []);
        });
        h.Main.OnMain(() =>
        {
            Assert.Equal(["next step"], runtime.FollowUps.Select(prompt => prompt.Text).ToArray());
            Assert.Empty(runtime.SentPrompts);
            Assert.Empty(runtime.Turns);
            Assert.Equal(0L, runtime.AutoStartedTurnToken);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-105")] // App/Runtime/ThreadRuntime.swift:780-781
    public void A_revert_under_way_or_an_empty_draft_leaves_the_box_and_the_queue_as_they_were()
    {
        using var h = new AppModelHarness();
        var (model, lead, _) = LeadWithHead(h);
        h.PrepareBootstrap(model); // R-TEST-10, in case anything starts a turn
        var runtime = Loaded(h, model, lead);

        h.Main.OnMain(() =>
        {
            // :781 an empty draft (whitespace only, no attachment, quote or command) while a turn runs.
            runtime.RehearseTurn("working");
            runtime.Draft = new ComposerDraft { Text = " \n " };
            runtime.QueueDraftAsFollowUp();
            Assert.Equal(" \n ", runtime.Draft.Text);
            Assert.Empty(runtime.FollowUps);

            // :781 a revert under way: neither queued nor sent.
            SetReverting(runtime);
            runtime.Draft = new ComposerDraft { Text = "next step" };
            runtime.QueueDraftAsFollowUp();
            Assert.Equal("next step", runtime.Draft.Text);
            Assert.Empty(runtime.FollowUps);
            Assert.Equal(["working"], runtime.SentPrompts);
        });
    }

    // A lead and one Droppy-run head of it in its panel (AppModel.swift:414-416; AppModel+Hydra.swift:273-275), both
    // on disk before the model is made; the head is stopped as a loaded library leaves it.
    private static (AppModel Model, ChatThread Lead, ChatThread Head) LeadWithHead(AppModelHarness h)
    {
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var lead = AppModelSamples.Thread(project);
        var head = new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised)
        {
            Title = "head",
            ParentThreadID = lead.Id,
            IsInPanel = true,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Stopped),
        };
        h.WriteLibrary([project], [lead, head]);
        AppModelHarness.WriteDocument(lead.Id);
        AppModelHarness.WriteDocument(head.Id);
        return (h.MakeModel(), lead, head);
    }

    private static ThreadRuntime Loaded(AppModelHarness h, AppModel model, ChatThread lead)
    {
        var runtime = h.Main.OnMain(() => model.Runtime(lead.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return runtime;
    }

    private static void PutToWork(AppModel model, ChatThread head, HydraHeadInfo.Status status) =>
        model.UpdateThread(head.Id, thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = status } });

    // isReverting is private(set) in the Swift (ThreadRuntime.swift:189); only revert(to:restoreFiles:) sets it.
    private static void SetReverting(ThreadRuntime runtime)
    {
        var field = typeof(ThreadRuntime).GetField("isReverting", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(runtime, true);
    }
}

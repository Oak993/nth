using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Xunit;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimePart3Tests
{
    [Fact]
    [Trait("Req", "PR-RUNTIME-039")] // App/Runtime/ThreadRuntime.swift:1610-1617
    public void Session_ready_holds_a_first_id_and_replaces_an_existing_id_immediately()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var first = AppModelSamples.Thread(project);
        var resumed = AppModelSamples.Thread(project) with { ProviderSessionID = "stored-session" };
        h.WriteLibrary([project], [first, resumed]);
        AppModelHarness.WriteDocument(first.Id);
        AppModelHarness.WriteDocument(resumed.Id);
        var model = h.MakeModel();
        var firstRuntime = h.Main.OnMain(() => model.Runtime(first.Id));
        var resumedRuntime = h.Main.OnMain(() => model.Runtime(resumed.Id));
        h.Main.Loop.Run(() => firstRuntime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.Loop.Run(() => resumedRuntime.EnsureLoaded(), AppModelHarness.Bound);
        var firstSession = new ThreadRuntimeSessionProbe();
        var resumedSession = new ThreadRuntimeSessionProbe();

        h.Main.OnMain(() =>
        {
            firstRuntime.Observe(firstSession);
            firstSession.Emit(new ProviderEvent.SessionReady("first-session"));
            Assert.Null(model.Thread(first.Id)!.ProviderSessionID);

            resumedRuntime.Observe(resumedSession);
            resumedSession.Emit(new ProviderEvent.SessionReady("replacement-session"));
            Assert.Equal("replacement-session", model.Thread(resumed.Id)!.ProviderSessionID);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-043")] // App/Runtime/ThreadRuntime.swift:2070-2077; :437-441
    public void Observe_ignores_events_from_a_session_with_an_old_epoch()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project) with { ProviderSessionID = "original-session" };
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        var oldSession = new ThreadRuntimeSessionProbe();
        var currentSession = new ThreadRuntimeSessionProbe();

        h.Main.OnMain(() =>
        {
            runtime.Observe(oldSession);
            runtime.Observe(currentSession);
            oldSession.Emit(new ProviderEvent.SessionReady("stale-session"));
            Assert.Equal("original-session", model.Thread(thread.Id)!.ProviderSessionID);

            currentSession.Emit(new ProviderEvent.SessionReady("current-session"));
            Assert.Equal("current-session", model.Thread(thread.Id)!.ProviderSessionID);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-042")] // App/Runtime/ThreadRuntime.swift:2082-2092
    public void Stopping_an_idle_runtime_discards_a_session_id_that_has_not_been_sent()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        var session = new ThreadRuntimeSessionProbe();

        h.Main.OnMain(() =>
        {
            runtime.Observe(session);
            session.Emit(new ProviderEvent.SessionReady("never-sent-session"));
            runtime.StopSession();
            Assert.Null(model.Thread(thread.Id)!.ProviderSessionID);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-045")] // App/Runtime/ThreadRuntime.swift:2098-2115
    public void StopSession_finishes_a_running_turn_as_interrupted()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("stop this turn");
            Assert.True(runtime.IsRunning);
            runtime.StopSession();
        });
        h.Main.Loop.RunUntil(
            () => runtime.Phase == RuntimePhase.Idle && runtime.Turns.Count == 1 && runtime.Turns[0].Status == TurnStatus.Interrupted,
            AppModelHarness.Bound);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-051")] // App/Runtime/ThreadRuntime.swift:1181
    public void Send_drops_a_message_when_the_thread_cannot_be_resolved()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], []);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => new ThreadRuntime(Guid.NewGuid(), model));

        h.Main.OnMain(() => runtime.SendHydraBrief("this message is lost", Array.Empty<Attachment>()));

        h.Main.OnMain(() =>
        {
            Assert.Empty(runtime.FollowUps);
            Assert.Empty(runtime.Entries);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-050")] // App/Runtime/ThreadRuntime.swift:1177-1180; :3700-3723 (running-turn setup)
    public void A_hydra_brief_queued_while_busy_becomes_an_ordinary_follow_up()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        var entryCount = h.Main.OnMain(() =>
        {
            model.Settings.HydraQueueHeads = false; // Plain follow-up queue: with Hydra's default queue-heads switch on, ThreadRuntime.swift:999 hands a prompt queued behind a running turn to a head instead (PR-RUNTIME-237).
            runtime.RehearseTurn("in-progress turn");
            Assert.True(runtime.IsRunning);
            return runtime.Entries.Count;
        });
        h.Main.OnMain(() => runtime.SendHydraBrief("deferred brief", Array.Empty<Attachment>()));
        h.Main.Loop.RunUntil(() => runtime.FollowUps.Count != 0, AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            Assert.Equal(entryCount, runtime.Entries.Count);
            var followUp = Assert.Single(runtime.FollowUps);
            Assert.Equal("deferred brief", followUp.Text);
            Assert.Empty(followUp.Attachments);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-052")] // App/Runtime/ThreadRuntime.swift:1182-1185
    public void Send_queues_a_message_for_an_archived_thread_without_a_timeline_row()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project) with { IsArchived = true };
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        var attachment = new Attachment { Name = "brief.png", Path = Path.Combine(h.Scratch, "brief.png"), MimeType = "image/png" };

        // The deferral is enqueueFollowUp (:1181-1184), which ends with flushFollowUpsIfIdle (:1003-1007). On this idle
        // lead the flush's Task drains the prompt straight back into startTurn (:1056-1071, :1116-1137), which defers
        // it again, so the main actor never goes idle: pumping to idle cannot end. The loop is pumped until the
        // prompt is queued, then through three more rounds of that cycle, none of which adds a turn or a row.
        h.Main.Loop.Enqueue(() => runtime.SendHydraBrief("archived brief", [attachment]));
        h.Main.Loop.RunUntil(() => runtime.FollowUps.Count == 1, AppModelHarness.Bound);

        var followUp = Assert.Single(runtime.FollowUps);
        Assert.Equal("archived brief", followUp.Text);
        Assert.Equal(attachment, Assert.Single(followUp.Attachments));
        Assert.Empty(runtime.Entries);

        // :1134-1135 each round counts one turn that starts on its own; :1181-1184 none of them starts.
        h.Main.Loop.RunUntil(() => runtime.AutoStartedTurnToken == 3 && runtime.FollowUps.Count == 1, AppModelHarness.Bound);
        var again = Assert.Single(runtime.FollowUps);
        Assert.Equal("archived brief", again.Text);
        Assert.Equal(attachment, Assert.Single(again.Attachments));
        Assert.Empty(runtime.Entries);
        Assert.Empty(runtime.Turns);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-053")] // App/Runtime/ThreadRuntime.swift:1186-1195
    public void A_hydra_brief_turn_starts_at_zero_and_links_its_user_row()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        h.Main.OnMain(() => runtime.SendHydraBrief("review the change", Array.Empty<Attachment>()));
        h.Main.Loop.RunUntil(
            () => runtime.Turns.Count == 1 && runtime.Phase == RuntimePhase.Idle,
            AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var turn = Assert.Single(runtime.Turns);
            Assert.Equal(0, turn.Index);
            Assert.NotNull(turn.UserItemID);
            var user = Assert.Single(runtime.Entries, entry => entry.Item.Id == turn.UserItemID);
            Assert.Equal(turn.Id, user.Item.TurnID);
            var content = Assert.IsType<TimelineItem.Content.User>(user.Item.TimelineItemContent);
            Assert.Equal("review the change", content.Value.Text);
            Assert.True(content.Value.HydraBrief);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-053")] // App/Runtime/ThreadRuntime.swift:1186-1195
    public void An_ordinary_user_turn_stores_no_false_hydra_brief_marker()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Text = "ordinary user turn" });

        h.Main.OnMain(runtime.Send);
        h.Main.Loop.RunUntil(
            () => runtime.Turns.Count == 1 && runtime.Phase == RuntimePhase.Idle,
            AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var turn = Assert.Single(runtime.Turns);
            Assert.Equal(0, turn.Index);
            var user = Assert.Single(runtime.Entries, entry => entry.Item.Id == turn.UserItemID);
            var content = Assert.IsType<TimelineItem.Content.User>(user.Item.TimelineItemContent);
            Assert.Equal("ordinary user turn", content.Value.Text);
            Assert.Null(content.Value.HydraBrief);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-059")] // App/Runtime/ThreadRuntime.swift:1246-1250
    public void The_first_message_names_the_thread_even_when_startup_fails()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Text = "Please sort these tickets" });

        h.Main.OnMain(runtime.Send);
        h.Main.Loop.RunUntil(
            () => runtime.Turns.Count > 0 && runtime.Phase == RuntimePhase.Idle,
            AppModelHarness.Bound);

        h.Main.OnMain(() => Assert.Equal("Please sort these tickets", model.Thread(thread.Id)!.Title));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-060")] // App/Runtime/ThreadRuntime.swift:3563-3577
    public void The_immediate_title_is_a_single_line_limited_to_48_characters()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        var message = new string('A', 60) + "\n" + new string('B', 40);
        h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Text = message });

        h.Main.OnMain(runtime.Send);
        h.Main.Loop.RunUntil(
            () => runtime.Turns.Count > 0 && runtime.Phase == RuntimePhase.Idle,
            AppModelHarness.Bound);

        h.Main.OnMain(() => Assert.Equal(new string('A', 47) + "…", model.Thread(thread.Id)!.Title));
    }
}

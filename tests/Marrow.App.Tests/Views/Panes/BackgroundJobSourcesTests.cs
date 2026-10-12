using System.Reflection;
using Marrow.App.Jobs;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Marrow.Testing.Downloads;

namespace Marrow.App.Tests.Views.Panes;

// Web surfaces W5 (DESIGN 3.10, 13.2 "BackgroundJobs: aggregation and ordering from fake sources"; ED-NEW-web-w5-1): the app's sources
// read the places the app already keeps its work, and carry out Stop, Pause and Resume there. Hydra heads, helpers and the panel's
// subagent, terminals, downloads and the published feed are the six; Tactus, Visus agent tabs and workflows publish into the last.
[Trait("Decision", "ED-NEW-web-w5-1")]
public sealed class BackgroundJobSourcesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private static ChatThread Head(Project project, Guid lead, HydraHeadInfo.Status status, long index, string task, bool inPanel = true, bool native = false) =>
        new(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised)
        {
            ParentThreadID = lead,
            IsInPanel = inPanel,
            Hydra = AppModelSamples.Head(status) with
            {
                Index = index,
                Task = task,
                StartedAt = Now.AddMinutes(-12),
                FinishedAt = status == HydraHeadInfo.Status.Running ? null : Now.AddMinutes(-3),
                ToolCalls = 7,
                Tokens = 4_000,
                HydraHeadInfoKind = native ? HydraHeadInfo.Kind.Native : HydraHeadInfo.Kind.Droppy,
                NativeID = native ? "n1" : null,
            },
        };

    private static ChatThread Child(Project project, Guid parent, string title, TurnStatus? last, bool inPanel = false, bool archived = false) =>
        new(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised)
        {
            ParentThreadID = parent,
            Title = title,
            LastStatus = last,
            IsInPanel = inPanel,
            IsArchived = archived,
            CreatedAt = Now.AddMinutes(-30),
            UpdatedAt = Now.AddMinutes(-4),
        };

    private static (AppModelHarness Harness, AppModel Model, Project Project, ChatThread Lead) Library(Func<Project, ChatThread, IEnumerable<ChatThread>> others)
    {
        var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var threads = others(project, lead).ToList();
        h.WriteLibrary([project], [lead, .. threads]);
        var model = h.MakeModel();
        // Loading a library ends the heads a closed app left running; the ones a test means to be running are put back.
        foreach (var head in threads.Where(thread => thread.Hydra is { HydraHeadInfoStatus: HydraHeadInfo.Status.Running }))
            h.Main.OnMain(() => model.UpdateThread(head.Id, thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running, FinishedAt = null } }));
        return (h, model, project, lead);
    }

    private static void SetField(ThreadRuntime runtime, string name, object? value)
    {
        var field = typeof(ThreadRuntime).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(runtime, value);
    }

    // ---- Hydra heads ----

    [Fact]
    public void Heads_in_the_panel_are_listed_with_their_task_persona_and_transcript_and_the_rest_are_not()
    {
        ChatThread running = null!, done = null!;
        var (h, model, _, lead) = Library((project, lead) =>
        {
            running = Head(project, lead.Id, HydraHeadInfo.Status.Running, 2, "Port settings tests\n  group A");
            done = Head(project, lead.Id, HydraHeadInfo.Status.Completed, 3, "Review the diff");
            return [running, done, Head(project, lead.Id, HydraHeadInfo.Status.Completed, 4, "cleared", inPanel: false), Head(project, lead.Id, HydraHeadInfo.Status.Failed, 5, "archived") with { IsArchived = true }];
        });
        using (h)
        {
            var jobs = new HydraHeadJobSource(model).Snapshot(Now);
            Assert.Equal(2, jobs.Count);

            var live = Assert.Single(jobs, job => job.State == BackgroundJobState.Running);
            Assert.Equal("hydra:" + running.Id.ToString("N"), live.Id);
            Assert.Equal(BackgroundJobKind.HydraHead, live.Kind);
            Assert.Equal("Port settings tests group A", live.Title);
            Assert.Equal("Hydra head", live.KindLabel);
            Assert.Equal(HydraRoster.Persona(2).Name, live.Subject);
            Assert.Equal(lead.Id, live.ThreadId);
            Assert.Equal(running.Id, live.TranscriptThreadId);
            Assert.Equal(Now.AddMinutes(-12), live.StartedAt);
            Assert.Null(live.FinishedAt);
            Assert.Equal((4_000L, 7L), (live.Tokens, live.ToolUses));
            Assert.Equal("Working", live.Activity);
            Assert.True(live.CanStop);
            Assert.False(live.CanPause);

            var over = Assert.Single(jobs, job => job.State == BackgroundJobState.Done);
            Assert.Equal(Now.AddMinutes(-3), over.FinishedAt);
            Assert.Null(over.Activity);
            Assert.False(over.CanStop);
        }
    }

    [Fact]
    public void A_running_heads_numbers_and_activity_come_live_from_its_runtime()
    {
        ChatThread head = null!;
        var (h, model, _, _) = Library((project, lead) => [head = Head(project, lead.Id, HydraHeadInfo.Status.Running, 1, "Fix the build")]);
        using (h)
        {
            h.Main.OnMain(() =>
            {
                var runtime = model.Runtime(head.Id);
                runtime.HydraActivity = "Reading Foo.cs";
                runtime.HydraToolCalls = 132;
                runtime.HydraTokens = 165_400;
            });
            var live = Assert.Single(h.Main.OnMain(() => new HydraHeadJobSource(model).Snapshot(Now)));
            Assert.Equal("Reading Foo.cs", live.Activity);
            Assert.Equal(132, live.ToolUses);
            Assert.Equal(165_400, live.Tokens);
        }
    }

    [Fact]
    public void Stop_reaches_only_a_running_head_and_Dismiss_takes_a_finished_one_out_of_the_panel()
    {
        ChatThread running = null!, done = null!;
        var (h, model, _, _) = Library((project, lead) => [running = Head(project, lead.Id, HydraHeadInfo.Status.Running, 1, "a", native: true),
            done = Head(project, lead.Id, HydraHeadInfo.Status.Completed, 2, "b")]);
        using (h)
        {
            var source = new HydraHeadJobSource(model);
            Assert.True(h.Main.OnMain(() => source.Stop("hydra:" + running.Id.ToString("N"))));
            Assert.False(h.Main.OnMain(() => source.Stop("hydra:" + done.Id.ToString("N"))));
            Assert.False(source.Stop("hydra:not-a-guid"));
            Assert.False(source.Stop("helper:" + running.Id.ToString("N")));
            Assert.False(source.Pause("hydra:" + running.Id.ToString("N")));
            Assert.False(source.Resume("hydra:" + running.Id.ToString("N")));

            h.Main.OnMain(() => source.Dismiss("hydra:" + done.Id.ToString("N")));
            Assert.False(model.Thread(done.Id)!.IsInPanel);
            Assert.DoesNotContain(source.Snapshot(Now), job => job.TranscriptThreadId == done.Id);
            h.Main.OnMain(() => source.Dismiss("hydra:" + running.Id.ToString("N"))); // a running head is not dismissed
            Assert.True(model.Thread(running.Id)!.IsInPanel);
        }
    }

    // ---- helpers and the panel's subagent ----

    [Fact]
    public void Helper_chats_that_ran_are_listed_with_how_they_ended_and_the_rest_are_not()
    {
        var ids = new Dictionary<string, Guid>();
        var (h, model, _, lead) = Library((project, lead) =>
        {
            var set = new[]
            {
                Child(project, lead.Id, "Review the diff", TurnStatus.Completed),
                Child(project, lead.Id, "Broken helper", TurnStatus.Failed),
                Child(project, lead.Id, "Cut helper", TurnStatus.Interrupted),
                Child(project, lead.Id, "Panel subagent", TurnStatus.Completed, inPanel: true),
                Child(project, lead.Id, "Never ran", null),
                Child(project, lead.Id, "Archived", TurnStatus.Completed, archived: true),
                Head(project, lead.Id, HydraHeadInfo.Status.Completed, 9, "a head is not a helper", inPanel: false),
            };
            foreach (var thread in set) ids[thread.Title] = thread.Id;
            return set;
        });
        using (h)
        {
            var jobs = new HelperJobSource(model).Snapshot(Now).ToDictionary(job => job.Title);
            Assert.Equal(["Broken helper", "Cut helper", "Panel subagent", "Review the diff"], jobs.Keys.Order(StringComparer.Ordinal));
            var review = jobs["Review the diff"];
            Assert.Equal(("helper:" + ids["Review the diff"].ToString("N"), BackgroundJobKind.Helper, "Helper", lead.Id), (review.Id, review.Kind, review.KindLabel, review.ThreadId));
            Assert.Equal(BackgroundJobState.Done, review.State);
            Assert.Equal(Now.AddMinutes(-4), review.FinishedAt);
            Assert.Equal(ids["Review the diff"], review.TranscriptThreadId);
            Assert.False(review.CanStop);
            Assert.Equal(BackgroundJobState.Failed, jobs["Broken helper"].State);
            Assert.Equal(BackgroundJobState.Stopped, jobs["Cut helper"].State);
            Assert.Equal("Subagent", jobs["Panel subagent"].KindLabel);
        }
    }

    [Fact]
    public void A_running_helper_shows_its_turns_tokens_its_tool_uses_since_the_turn_began_and_its_newest_running_tool()
    {
        ChatThread helper = null!;
        var (h, model, _, _) = Library((project, lead) => [helper = Child(project, lead.Id, "Helper at work", TurnStatus.Running)]);
        using (h)
        {
            h.Main.OnMain(() =>
            {
                var runtime = model.Runtime(helper.Id);
                var turn = Guid.NewGuid();
                SetField(runtime, "phase", RuntimePhase.Running);
                SetField(runtime, "turnStartedAt", (DateTimeOffset?)Now.AddMinutes(-2));
                SetField(runtime, "turns", new ValueList<TurnRecord>([new TurnRecord
                {
                    Index = 1,
                    Status = TurnStatus.Running,
                    TokenSpends = new ValueList<TokenSpend>([new TokenSpend(ProviderKind.Codex, 12_000), new TokenSpend(ProviderKind.Codex, 800)]),
                }]));
                TimelineEntry Tool(string title, ToolCall.Status status, DateTimeOffset at) => new(new TimelineItem(turn, new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = ToolCall.Kind.Read, Title = title, ToolCallStatus = status }), date: at));
                SetField(runtime, "entries", new ValueList<TimelineEntry>(
                [
                    Tool("Read Old.cs", ToolCall.Status.Completed, Now.AddMinutes(-9)), // before this turn
                    Tool("Read A.cs", ToolCall.Status.Completed, Now.AddMinutes(-1)),
                    Tool("Read B.cs", ToolCall.Status.Running, Now.AddSeconds(-5)),
                ]));
            });
            var job = Assert.Single(h.Main.OnMain(() => new HelperJobSource(model).Snapshot(Now)));
            Assert.Equal(BackgroundJobState.Running, job.State);
            Assert.Equal(Now.AddMinutes(-2), job.StartedAt);
            Assert.Equal(12_800, job.Tokens);
            Assert.Equal(2, job.ToolUses);
            Assert.Equal("Reading B.cs", job.Activity);
            Assert.True(job.CanStop);
        }
    }

    // ---- terminals ----

    private static TerminalJobSource.TerminalFacts Facts(bool scripted = true, bool running = true, bool busy = false, string title = "dev", string? url = null,
        DateTimeOffset? ended = null, bool byUser = false, Guid? thread = null) =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), thread ?? Guid.Parse("22222222-2222-2222-2222-222222222222"), title, scripted, running, busy, Now.AddMinutes(-18), ended, byUser, url);

    [Fact]
    public void A_script_running_in_a_terminal_is_a_task_and_a_dev_server_shows_its_address()
    {
        var plain = TerminalJobSource.Map(Facts(), Now)!;
        Assert.Equal(("terminal:11111111111111111111111111111111", BackgroundJobKind.Terminal, "dev", "Terminal", null), (plain.Id, plain.Kind, plain.Title, plain.KindLabel, plain.Subject));
        Assert.Equal((BackgroundJobState.Running, "Running", true, false, Now.AddMinutes(-18)), (plain.State, plain.Activity, plain.CanStop, plain.CanPause, plain.StartedAt));
        Assert.Null(plain.TranscriptThreadId);
        Assert.False(plain.IsDevServer);

        var server = TerminalJobSource.Map(Facts(title: "npm run dev", url: "http://localhost:5173/"), Now)!;
        Assert.Equal(("Dev server", "localhost:5173", "http://localhost:5173/"), (server.KindLabel, server.Subject, server.DevServerUrl));
        Assert.Equal("Dev server · localhost:5173", server.KindText);
    }

    [Fact]
    public void A_plain_shell_is_a_task_only_while_a_program_runs_in_it()
    {
        Assert.Null(TerminalJobSource.Map(Facts(scripted: false, busy: false), Now));
        var busy = TerminalJobSource.Map(Facts(scripted: false, busy: true, title: "Terminal 2"), Now)!;
        Assert.Equal((BackgroundJobState.Running, "Terminal 2"), (busy.State, busy.Title));
        Assert.Null(TerminalJobSource.Map(Facts(scripted: false, running: false), Now)); // a shell that quit ran no task
    }

    [Fact]
    public void A_script_that_ended_is_Done_or_Stopped_with_the_time_it_ended_and_a_blank_title_reads_Terminal()
    {
        var ended = Now.AddMinutes(-2);
        var done = TerminalJobSource.Map(Facts(running: false, ended: ended), Now)!;
        Assert.Equal((BackgroundJobState.Done, ended, false, null), (done.State, done.FinishedAt, done.CanStop, done.Activity));
        var stopped = TerminalJobSource.Map(Facts(running: false, ended: ended, byUser: true, url: "http://localhost:3000"), Now)!;
        Assert.Equal(BackgroundJobState.Stopped, stopped.State);
        Assert.False(stopped.IsDevServer); // an ended server has no address to open
        Assert.Equal("Terminal", TerminalJobSource.Map(Facts(title: "  "), Now)!.Title);
    }

    [Fact]
    public void A_terminal_source_over_a_model_with_no_sessions_lists_nothing_and_refuses_every_action()
    {
        var (h, model, _, _) = Library((_, _) => []);
        using (h)
        {
            var source = new TerminalJobSource(model);
            Assert.Empty(source.Snapshot(Now));
            Assert.False(source.Stop("terminal:11111111111111111111111111111111"));
            Assert.False(source.Stop("nope"));
            Assert.False(source.Pause("terminal:11111111111111111111111111111111"));
            Assert.False(source.Resume("terminal:11111111111111111111111111111111"));
        }
    }

    // ---- downloads ----

    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(10_000, seed: 3);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    public async Task A_running_download_shows_its_progress_pauses_resumes_and_stops()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/m.bin", Artifact, new FakeBehavior { Trickle = new FakeTrickle(2_000, TimeSpan.FromMinutes(1)) });
        var registry = new DownloadJobRegistry();
        var job = rig.Create().Start(rig.Request("/m.bin", Artifact));
        registry.Track(job, [rig.Request("/m.bin", Artifact)], SwiftClock.Provider.Current);
        await DownloadRig.Until(() => job.BytesReceived >= 2_000);
        var source = new DownloadJobSource(registry);
        var now = SwiftClock.Provider.Current.GetUtcNow();

        var shown = Assert.Single(source.Snapshot(now));
        Assert.Equal((BackgroundJobKind.Download, "m.bin", "Download", BackgroundJobState.Running, true, true), (shown.Kind, shown.Title, shown.KindLabel, shown.State, shown.CanPause, shown.CanStop));
        Assert.StartsWith("download:", shown.Id, StringComparison.Ordinal);
        Assert.InRange(shown.Progress!.Value, 0.2, 0.9);
        Assert.Matches(@"^\d+(\.\d+)? of 10 KB$", shown.Subject!);
        Assert.Null(shown.ThreadId);
        Assert.Null(shown.FinishedAt);

        Assert.True(source.Pause(shown.Id));
        Assert.Equal(BackgroundJobState.Paused, Assert.Single(source.Snapshot(now)).State);
        Assert.False(source.Pause(shown.Id)); // already paused
        Assert.True(source.Resume(shown.Id));
        Assert.Equal(BackgroundJobState.Running, Assert.Single(source.Snapshot(now)).State);
        Assert.False(source.Resume(shown.Id));

        Assert.True(source.Stop(shown.Id));
        await job.Completion.WaitAsync(FakeDownloadServer.Guard);
        var over = Assert.Single(source.Snapshot(now));
        Assert.Equal((BackgroundJobState.Stopped, false, false), (over.State, over.CanPause, over.CanStop));
        Assert.False(source.Stop(shown.Id));
        Assert.False(source.Stop("download:" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    public async Task A_finished_download_is_Done_at_100_percent()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/ok.bin", Artifact, new FakeBehavior());
        var registry = new DownloadJobRegistry();
        var job = rig.Create().Start(rig.Request("/ok.bin", Artifact));
        var entry = registry.Track(job, [rig.Request("/ok.bin", Artifact)], SwiftClock.Provider.Current);
        await job.Completion.WaitAsync(FakeDownloadServer.Guard);
        await DownloadRig.Until(() => entry.FinishedAt is not null);
        var done = Assert.Single(new DownloadJobSource(registry).Snapshot(SwiftClock.Provider.Current.GetUtcNow()));
        Assert.Equal((BackgroundJobState.Done, 1.0, "10 of 10 KB"), (done.State, done.Progress, done.Subject));
        Assert.Equal(entry.FinishedAt, done.FinishedAt);
    }

    // ---- the app's list ----

    [Fact]
    public void The_app_has_one_list_over_all_its_sources_and_published_jobs_join_it_with_their_handles()
    {
        ChatThread head = null!;
        var (h, model, _, lead) = Library((project, lead) => [head = Head(project, lead.Id, HydraHeadInfo.Status.Running, 1, "Fix the build")]);
        using (h)
        {
            var jobs = model.BackgroundJobs;
            Assert.Same(jobs, model.BackgroundJobs);
            Assert.Equal(["Hydra heads", "Helpers", "Terminals", "Downloads", "Published"], jobs.Sources.Select(source => source.Name));

            var stops = 0;
            var pauses = 0;
            model.PublishedJobs.Publish(new BackgroundJob
            {
                Id = "tactus:1",
                Kind = BackgroundJobKind.Tactus,
                Title = "Fill the Q3 sheet",
                KindLabel = "Tactus",
                Subject = "Excel",
                ThreadId = lead.Id,
                StartedAt = Now.AddMinutes(-3),
                CanPause = true,
                Activity = "Typing in C7",
                Tokens = 41_200,
                ToolUses = 38,
            }, new BackgroundJobControl(() => { stops++; return true; }, () => { pauses++; return true; }));

            h.Main.OnMain(jobs.Refresh);
            // The downloader's registry is the process's, so downloads other tests started may be listed too.
            Assert.Contains(jobs.Jobs, job => job.Id == "hydra:" + head.Id.ToString("N"));
            Assert.Contains(jobs.Jobs, job => job.Id == "tactus:1");
            Assert.All(jobs.Jobs, job => Assert.True(job.Id.StartsWith("download:", StringComparison.Ordinal) || job.Id == "tactus:1" || job.Id == "hydra:" + head.Id.ToString("N"), job.Id));
            Assert.True(jobs.Pause("tactus:1"));
            Assert.True(jobs.Stop("tactus:1"));
            Assert.Equal((1, 1), (pauses, stops));
            Assert.Equal(BackgroundJobState.Stopping, jobs.Find("tactus:1")!.State);
        }
    }
}

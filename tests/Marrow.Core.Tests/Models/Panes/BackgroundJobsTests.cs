using Marrow.Core.Models;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models.Panes;

// DESIGN 3.10, 13.2 "BackgroundJobs: aggregation and ordering from fake sources" (web surfaces W5; ED-NEW-web-w5-1): the six
// sources merged into one list, running first by start, finished after, the Done group going after ten minutes, Stop showing
// Stopping until the source says it is over, Pause and Resume only where the job can, Clear done, a broken source, and the 4 Hz
// ticker that runs only while a view holds a lease.
[Trait("Decision", "ED-NEW-web-w5-1")]
public sealed class BackgroundJobsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ThreadA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ThreadB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private sealed class FakeSource(string name) : IBackgroundJobSource
    {
        public List<BackgroundJob> Jobs { get; } = [];
        public List<string> Stopped { get; } = [];
        public List<string> Paused { get; } = [];
        public List<string> Resumed { get; } = [];
        public List<string> Dismissed { get; } = [];
        public bool Accept { get; set; } = true;
        public bool Throws { get; set; }
        public int Snapshots { get; private set; }

        public string Name => name;

        public IReadOnlyList<BackgroundJob> Snapshot(DateTimeOffset now)
        {
            Snapshots++;
            return Throws ? throw new InvalidOperationException("broken source") : Jobs.ToArray();
        }

        public bool Stop(string jobId) { Stopped.Add(jobId); return Accept; }

        public bool Pause(string jobId) { Paused.Add(jobId); return Accept; }

        public bool Resume(string jobId) { Resumed.Add(jobId); return Accept; }

        public void Dismiss(string jobId) => Dismissed.Add(jobId);
    }

    private static BackgroundJob Job(string id, BackgroundJobKind kind = BackgroundJobKind.Helper, int startedMinute = 0, BackgroundJobState state = BackgroundJobState.Running,
        Guid? thread = null, DateTimeOffset? finished = null, bool pause = false) => new()
        {
            Id = id,
            Kind = kind,
            Title = "Task " + id,
            KindLabel = kind.ToString(),
            ThreadId = thread,
            State = state,
            StartedAt = T0.AddMinutes(startedMinute),
            FinishedAt = finished,
            CanPause = pause,
            CanStop = state.IsLive(),
        };

    private static (BackgroundJobs Jobs, ManualTimeProvider Clock, List<Action> Posted) Make(params IBackgroundJobSource[] sources)
    {
        var clock = new ManualTimeProvider(T0);
        var posted = new List<Action>();
        var jobs = new BackgroundJobs(clock, posted.Add);
        foreach (var source in sources) jobs.AddSource(source);
        return (jobs, clock, posted);
    }

    [Fact]
    public void All_six_sources_are_listed_with_their_kinds_in_one_list()
    {
        var heads = new FakeSource("Hydra heads");
        heads.Jobs.Add(Job("hydra:1", BackgroundJobKind.HydraHead, 0, thread: ThreadA));
        var helpers = new FakeSource("Helpers");
        helpers.Jobs.Add(Job("helper:1", BackgroundJobKind.Helper, 1, thread: ThreadA));
        var terminals = new FakeSource("Terminals");
        terminals.Jobs.Add(Job("terminal:1", BackgroundJobKind.Terminal, 2, thread: ThreadA));
        var downloads = new FakeSource("Downloads");
        downloads.Jobs.Add(Job("download:1", BackgroundJobKind.Download, 3, pause: true));
        var published = new PublishedJobSource("Published");
        published.Publish(Job("tactus:1", BackgroundJobKind.Tactus, 4, thread: ThreadA, pause: true));
        published.Publish(Job("browser:1", BackgroundJobKind.BrowserSession, 5, thread: ThreadA, pause: true));
        published.Publish(Job("flow:1", BackgroundJobKind.Workflow, 6, thread: ThreadA, pause: true));
        var (jobs, _, _) = Make(heads, helpers, terminals, downloads, published);
        jobs.Refresh();
        Assert.Equal(["hydra:1", "helper:1", "terminal:1", "download:1", "tactus:1", "browser:1", "flow:1"], jobs.Jobs.Select(job => job.Id));
        Assert.Equal(
            [BackgroundJobKind.HydraHead, BackgroundJobKind.Helper, BackgroundJobKind.Terminal, BackgroundJobKind.Download, BackgroundJobKind.Tactus, BackgroundJobKind.BrowserSession, BackgroundJobKind.Workflow],
            jobs.Jobs.Select(job => job.Kind));
    }

    [Fact]
    public void Running_jobs_come_first_by_start_then_finished_ones_most_recent_first_and_ties_break_on_the_id()
    {
        var source = new FakeSource("s");
        source.Jobs.Add(Job("done-old", startedMinute: 0, state: BackgroundJobState.Done, finished: T0.AddMinutes(1)));
        source.Jobs.Add(Job("run-late", startedMinute: 5));
        source.Jobs.Add(Job("done-new", startedMinute: 1, state: BackgroundJobState.Failed, finished: T0.AddMinutes(3)));
        source.Jobs.Add(Job("run-early", startedMinute: 2));
        source.Jobs.Add(Job("run-tie-b", startedMinute: 3));
        source.Jobs.Add(Job("run-tie-a", startedMinute: 3));
        var (jobs, _, _) = Make(source);
        jobs.Refresh();
        Assert.Equal(["run-early", "run-tie-a", "run-tie-b", "run-late", "done-new", "done-old"], jobs.Jobs.Select(job => job.Id));
    }

    [Fact]
    public void The_order_is_the_same_for_any_arrival_order()
    {
        var jobs = Enumerable.Range(0, 24).Select(i => Job("j" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), startedMinute: i % 5,
            state: i % 3 == 0 ? BackgroundJobState.Done : BackgroundJobState.Running, finished: i % 3 == 0 ? T0.AddMinutes(i % 4) : null)).ToList();
        var expected = BackgroundJobOrder.Sort(jobs).Select(job => job.Id).ToList();
        var random = new Random(7);
        for (var round = 0; round < 1000; round++)
            Assert.Equal(expected, BackgroundJobOrder.Sort(jobs.OrderBy(_ => random.Next())).Select(job => job.Id));
    }

    [Fact]
    public void This_thread_keeps_the_threads_own_jobs_and_the_app_wide_ones_and_All_threads_keeps_everything()
    {
        var source = new FakeSource("s");
        source.Jobs.Add(Job("a", thread: ThreadA, startedMinute: 0));
        source.Jobs.Add(Job("b", thread: ThreadB, startedMinute: 1));
        source.Jobs.Add(Job("wide", BackgroundJobKind.Download, startedMinute: 2));
        var (jobs, _, _) = Make(source);
        jobs.Refresh();
        Assert.Equal(["a", "wide"], jobs.For(ThreadA, BackgroundJobScope.ThisThread).Select(job => job.Id));
        Assert.Equal(["b", "wide"], jobs.For(ThreadB, BackgroundJobScope.ThisThread).Select(job => job.Id));
        Assert.Equal(["wide"], jobs.For(null, BackgroundJobScope.ThisThread).Select(job => job.Id));
        Assert.Equal(["a", "b", "wide"], jobs.For(ThreadA, BackgroundJobScope.AllThreads).Select(job => job.Id));
    }

    [Fact]
    public void A_finished_job_stays_for_ten_minutes_and_then_goes()
    {
        var source = new FakeSource("s");
        source.Jobs.Add(Job("done", state: BackgroundJobState.Done, finished: T0));
        var (jobs, clock, _) = Make(source);
        jobs.Refresh();
        Assert.Single(jobs.Jobs);
        clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromMilliseconds(1));
        jobs.Refresh();
        Assert.Single(jobs.Jobs);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        jobs.Refresh();
        Assert.Empty(jobs.Jobs);
    }

    [Fact]
    public void A_source_that_cannot_tell_when_a_job_ended_has_it_stamped_when_first_seen_over_and_the_stamp_holds()
    {
        var source = new FakeSource("s");
        source.Jobs.Add(Job("quiet"));
        var (jobs, clock, _) = Make(source);
        jobs.Refresh();
        clock.Advance(TimeSpan.FromMinutes(2));
        source.Jobs[0] = Job("quiet", state: BackgroundJobState.Done);
        jobs.Refresh();
        var stamped = Assert.Single(jobs.Jobs).FinishedAt;
        Assert.Equal(T0.AddMinutes(2), stamped);
        clock.Advance(TimeSpan.FromMinutes(7));
        jobs.Refresh();
        Assert.Equal(stamped, Assert.Single(jobs.Jobs).FinishedAt);
        clock.Advance(TimeSpan.FromMinutes(3)); // ten minutes after the stamp
        jobs.Refresh();
        Assert.Empty(jobs.Jobs);
    }

    [Fact]
    public void Elapsed_time_runs_while_the_job_runs_and_freezes_at_its_end()
    {
        var running = Job("r", startedMinute: 0);
        Assert.Equal(TimeSpan.FromSeconds(725), running.Elapsed(T0.AddSeconds(725)));
        var finished = Job("f", startedMinute: 0, state: BackgroundJobState.Done, finished: T0.AddSeconds(60));
        Assert.Equal(TimeSpan.FromSeconds(60), finished.Elapsed(T0.AddHours(3)));
        Assert.Equal(TimeSpan.FromHours(3) - TimeSpan.FromSeconds(60), finished.Age(T0.AddHours(3)));
        Assert.Equal(TimeSpan.Zero, running.Elapsed(T0.AddSeconds(-5)));
        Assert.Equal(TimeSpan.Zero, running.Age(T0.AddHours(1)));
    }

    [Fact]
    public void Stop_asks_the_source_and_the_job_shows_Stopping_until_the_source_says_it_is_over()
    {
        var source = new FakeSource("s");
        source.Jobs.Add(Job("a", pause: true));
        var (jobs, clock, _) = Make(source);
        jobs.Refresh();
        Assert.True(jobs.Stop("a"));
        Assert.Equal(["a"], source.Stopped);
        var stopping = Assert.Single(jobs.Jobs);
        Assert.Equal(BackgroundJobState.Stopping, stopping.State);
        Assert.False(stopping.CanStop);
        Assert.False(stopping.CanPause);
        Assert.False(jobs.Stop("a")); // already stopping: no second request
        Assert.Single(source.Stopped);
        clock.Advance(TimeSpan.FromSeconds(3));
        source.Jobs[0] = Job("a", state: BackgroundJobState.Stopped, finished: clock.GetUtcNow());
        jobs.Refresh();
        Assert.Equal(BackgroundJobState.Stopped, Assert.Single(jobs.Jobs).State);
    }

    [Fact]
    public void A_stop_the_source_refuses_changes_nothing_and_one_that_never_lands_returns_to_Running_after_the_grace()
    {
        var source = new FakeSource("s") { Accept = false };
        source.Jobs.Add(Job("a"));
        var (jobs, clock, _) = Make(source);
        jobs.Refresh();
        Assert.False(jobs.Stop("a"));
        Assert.Equal(BackgroundJobState.Running, Assert.Single(jobs.Jobs).State);
        source.Accept = true;
        Assert.True(jobs.Stop("a"));
        Assert.Equal(BackgroundJobState.Stopping, Assert.Single(jobs.Jobs).State);
        clock.Advance(BackgroundJobs.StoppingGrace - TimeSpan.FromMilliseconds(1));
        jobs.Refresh();
        Assert.Equal(BackgroundJobState.Stopping, Assert.Single(jobs.Jobs).State);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        jobs.Refresh();
        var back = Assert.Single(jobs.Jobs);
        Assert.Equal(BackgroundJobState.Running, back.State);
        Assert.True(back.CanStop);
    }

    [Fact]
    public void Stop_is_refused_for_an_unknown_finished_or_unstoppable_job()
    {
        var source = new FakeSource("s");
        source.Jobs.Add(Job("done", state: BackgroundJobState.Done, finished: T0));
        source.Jobs.Add(Job("fixed") with { CanStop = false });
        var (jobs, _, _) = Make(source);
        jobs.Refresh();
        Assert.False(jobs.Stop("nope"));
        Assert.False(jobs.Stop("done"));
        Assert.False(jobs.Stop("fixed"));
        Assert.Empty(source.Stopped);
    }

    [Fact]
    public void Pause_and_Resume_reach_only_jobs_that_can_and_follow_the_sources_state()
    {
        var source = new FakeSource("s");
        source.Jobs.Add(Job("dl", BackgroundJobKind.Download, pause: true));
        source.Jobs.Add(Job("head", BackgroundJobKind.HydraHead));
        var (jobs, _, _) = Make(source);
        jobs.Refresh();
        Assert.False(jobs.Pause("head"));
        Assert.True(jobs.Pause("dl"));
        Assert.Equal(["dl"], source.Paused);
        Assert.False(jobs.Resume("dl")); // the source has not reported it paused yet
        source.Jobs[0] = Job("dl", BackgroundJobKind.Download, state: BackgroundJobState.Paused, pause: true);
        jobs.Refresh();
        Assert.False(jobs.Pause("dl"));
        Assert.True(jobs.Resume("dl"));
        Assert.Equal(["dl"], source.Resumed);
        Assert.True(jobs.Find("dl")!.IsPaused);
    }

    [Fact]
    public void Clear_done_hides_the_finished_jobs_of_the_list_shown_and_tells_their_sources()
    {
        var source = new FakeSource("s");
        source.Jobs.Add(Job("a-done", thread: ThreadA, state: BackgroundJobState.Done, finished: T0));
        source.Jobs.Add(Job("b-done", thread: ThreadB, state: BackgroundJobState.Failed, finished: T0));
        source.Jobs.Add(Job("a-run", thread: ThreadA));
        var (jobs, _, _) = Make(source);
        jobs.Refresh();
        Assert.Equal(1, jobs.ClearDone(ThreadA, BackgroundJobScope.ThisThread));
        Assert.Equal(["a-done"], source.Dismissed);
        Assert.Equal(["a-run", "b-done"], jobs.Jobs.Select(job => job.Id));
        // The source still lists it; it does not come back on the next tick.
        jobs.Refresh();
        Assert.DoesNotContain(jobs.Jobs, job => job.Id == "a-done");
        Assert.Equal(1, jobs.ClearDone(ThreadA, BackgroundJobScope.AllThreads));
        Assert.Equal(["a-run"], jobs.Jobs.Select(job => job.Id));
        Assert.Equal(0, jobs.ClearDone(ThreadA, BackgroundJobScope.AllThreads));
    }

    [Fact]
    public void A_cleared_job_that_runs_again_under_the_same_id_is_listed_again()
    {
        var source = new FakeSource("s");
        source.Jobs.Add(Job("x", state: BackgroundJobState.Done, finished: T0));
        var (jobs, _, _) = Make(source);
        jobs.Refresh();
        jobs.ClearDone(null, BackgroundJobScope.AllThreads);
        Assert.Empty(jobs.Jobs);
        source.Jobs[0] = Job("x");
        jobs.Refresh();
        Assert.Equal(BackgroundJobState.Running, Assert.Single(jobs.Jobs).State);
    }

    [Fact]
    public void A_source_that_throws_loses_only_its_own_jobs_and_a_repeated_id_keeps_the_first()
    {
        var broken = new FakeSource("broken") { Throws = true };
        var good = new FakeSource("good");
        good.Jobs.Add(Job("g"));
        var other = new FakeSource("other");
        other.Jobs.Add(Job("g") with { Title = "second" });
        other.Jobs.Add(Job("o", startedMinute: 1));
        var (jobs, _, _) = Make(broken, good, other);
        jobs.Refresh();
        Assert.Equal(["g", "o"], jobs.Jobs.Select(job => job.Id));
        Assert.Equal("Task g", jobs.Find("g")!.Title);
    }

    [Fact]
    public void Changed_is_raised_when_the_list_the_pane_draws_changes_and_Ticked_on_every_refresh()
    {
        var source = new FakeSource("s");
        source.Jobs.Add(Job("a") with { Tokens = 100, ToolUses = 1, Activity = "Reading" });
        var (jobs, _, _) = Make(source);
        var changed = 0;
        var ticked = 0;
        jobs.Changed += () => changed++;
        jobs.Ticked += () => ticked++;
        jobs.Refresh();
        Assert.Equal((1, 1), (changed, ticked));
        var version = jobs.Version;
        // Only live values change: Ticked, not Changed.
        source.Jobs[0] = Job("a") with { Tokens = 250, ToolUses = 2, Activity = "Editing" };
        jobs.Refresh();
        Assert.Equal((1, 2), (changed, ticked));
        Assert.Equal(version, jobs.Version);
        // A new job, then a state change, then a job going: each is a change.
        source.Jobs.Add(Job("b", startedMinute: 1));
        jobs.Refresh();
        source.Jobs[1] = Job("b", startedMinute: 1, state: BackgroundJobState.Done, finished: T0);
        jobs.Refresh();
        source.Jobs.RemoveAt(1);
        jobs.Refresh();
        Assert.Equal((4, 5), (changed, ticked));
        Assert.Equal(version + 3, jobs.Version);
    }

    [Fact]
    public void SameShape_ignores_the_live_values_and_sees_everything_else()
    {
        var baseline = Job("a") with { Tokens = 1, ToolUses = 1, Activity = "x", Progress = 0.1 };
        Assert.True(baseline.SameShape(baseline with { Tokens = 2000, ToolUses = 9, Activity = "y", Progress = 0.9, Subject = "3.1 of 5.0 GB" }));
        Assert.False(baseline.SameShape(baseline with { State = BackgroundJobState.Paused }));
        Assert.False(baseline.SameShape(baseline with { Title = "other" }));
        Assert.False(baseline.SameShape(baseline with { CanPause = true }));
        Assert.False(baseline.SameShape(baseline with { Tokens = null }));
        Assert.False(baseline.SameShape(baseline with { Progress = null }));
        Assert.False(baseline.SameShape(baseline with { TranscriptThreadId = ThreadA }));
        Assert.False(baseline.SameShape(baseline with { DevServerUrl = "http://localhost:5173" }));
        Assert.False(baseline.SameShape(baseline with { FinishedAt = T0 }));
        Assert.False(baseline.SameShape(null));
    }

    [Fact]
    public void KindText_joins_the_kind_and_the_subject_with_a_middle_dot()
    {
        Assert.Equal("Hydra head", (Job("a") with { KindLabel = "Hydra head" }).KindText);
        Assert.Equal("Tactus · Excel", (Job("a") with { KindLabel = "Tactus", Subject = "Excel" }).KindText);
    }

    // ---- the ticker ----

    [Fact]
    public void The_first_lease_refreshes_at_once_and_starts_a_4_Hz_timer_the_last_one_stops()
    {
        var source = new FakeSource("s");
        source.Jobs.Add(Job("a"));
        var (jobs, clock, posted) = Make(source);
        Assert.Equal(0, clock.ActiveTimers);
        var first = jobs.Attach();
        Assert.Equal(1, clock.ActiveTimers);
        Assert.Equal(1, source.Snapshots);
        Assert.Single(jobs.Jobs);
        var second = jobs.Attach();
        Assert.Equal(1, clock.ActiveTimers);
        Assert.Equal(2, jobs.Leases);
        Assert.Equal(1, source.Snapshots);

        // Four ticks in a second, each run on the main actor after its timer fires.
        for (var i = 0; i < 4; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(250));
            Assert.Single(posted);
            posted[0]();
            posted.Clear();
        }
        Assert.Equal(5, source.Snapshots);

        first.Dispose();
        first.Dispose(); // idempotent
        Assert.Equal(1, jobs.Leases);
        Assert.Equal(1, clock.ActiveTimers);
        second.Dispose();
        Assert.Equal(0, jobs.Leases);
        Assert.Equal(0, clock.ActiveTimers);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(posted);
        Assert.Equal(5, source.Snapshots);
    }

    [Fact]
    public void A_tick_still_waiting_for_the_main_actor_is_not_queued_twice()
    {
        var source = new FakeSource("s");
        var (jobs, clock, posted) = Make(source);
        using var lease = jobs.Attach();
        var before = source.Snapshots;
        clock.Advance(TimeSpan.FromSeconds(2)); // eight timer fires with a busy main thread
        Assert.Single(posted);
        posted[0]();
        Assert.Equal(before + 1, source.Snapshots);
        clock.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(2, posted.Count);
    }

    [Fact]
    public void A_queued_tick_that_finds_no_lease_refreshes_nothing()
    {
        var source = new FakeSource("s");
        var (jobs, clock, posted) = Make(source);
        var lease = jobs.Attach();
        clock.Advance(TimeSpan.FromMilliseconds(250));
        var before = source.Snapshots;
        lease.Dispose();
        posted[0]();
        Assert.Equal(before, source.Snapshots);
    }

    // ---- the published source ----

    [Fact]
    public void A_published_job_is_replaced_by_its_id_keeps_its_handles_and_is_retired()
    {
        var source = new PublishedJobSource("Published");
        var stops = 0;
        var pauses = 0;
        var resumes = 0;
        source.Publish(Job("tactus:1", BackgroundJobKind.Tactus, pause: true), new BackgroundJobControl(() => { stops++; return true; }, () => { pauses++; return true; }, () => { resumes++; return true; }));
        source.Publish(Job("tactus:1", BackgroundJobKind.Tactus, pause: true) with { Tokens = 900 }); // a progress update keeps the handles
        Assert.Equal(900, Assert.Single(source.Snapshot(T0)).Tokens);
        Assert.True(source.Stop("tactus:1"));
        Assert.True(source.Pause("tactus:1"));
        Assert.True(source.Resume("tactus:1"));
        Assert.Equal((1, 1, 1), (stops, pauses, resumes));
        Assert.False(source.Stop("none"));
        Assert.True(source.Retire("tactus:1"));
        Assert.False(source.Retire("tactus:1"));
        Assert.Empty(source.Snapshot(T0));
    }

    [Fact]
    public void A_published_job_with_no_handles_refuses_every_action_and_a_cleared_finished_one_leaves_the_feed()
    {
        var source = new PublishedJobSource("Published");
        var dismissed = 0;
        source.Publish(Job("flow:1", BackgroundJobKind.Workflow));
        Assert.False(source.Stop("flow:1"));
        Assert.False(source.Pause("flow:1"));
        Assert.False(source.Resume("flow:1"));
        source.Publish(Job("flow:2", BackgroundJobKind.Workflow, state: BackgroundJobState.Done, finished: T0), new BackgroundJobControl(Dismiss: () => dismissed++));
        source.Dismiss("flow:1"); // running: stays
        source.Dismiss("flow:2");
        Assert.Equal(1, dismissed);
        Assert.Equal(["flow:1"], source.Snapshot(T0).Select(job => job.Id));
    }

    [Fact]
    public void Publishers_on_other_threads_never_corrupt_the_feed()
    {
        var source = new PublishedJobSource("Published");
        Parallel.For(0, 200, i =>
        {
            source.Publish(Job("j" + (i % 20).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            _ = source.Snapshot(T0);
            if (i % 7 == 0) source.Retire("j" + (i % 20).ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
        Assert.True(source.Snapshot(T0).Count <= 20);
    }
}

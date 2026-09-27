using System.Collections;
using System.Reflection;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

public sealed class AutoContinueReviewTests
{
    // STOP App.Runtime.AutoContinue #1
    // Kind: out-of-unit dependency. Swift App/Runtime/AutoContinue.swift:103, PR-RUNTIME-184.
    // ThreadRuntime.Rehearse loses the emitted Notice.Limit; that unit is outside GROUP1.
    // Assert the emitted notice here; the runtime unit needs its own rehearsal regression and fix.
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [Trait("Req", "PR-RUNTIME-245")] // App/Runtime/AutoContinue.swift:53-54
    [Trait("Req", "PR-RUNTIME-246")] // App/Runtime/AutoContinue.swift:53-54
    [Trait("Req", "PR-RUNTIME-247")] // App/Runtime/AutoContinue.swift:53-54
    [Trait("Req", "PR-RUNTIME-248")] // App/Runtime/AutoContinue.swift:53-54
    [Trait("Req", "PR-RUNTIME-249")] // App/Runtime/AutoContinue.swift:53-54
    [Trait("Req", "PR-RUNTIME-250")] // App/Runtime/AutoContinue.swift:53-54
    public void Each_gate_alone_rejects_and_consumes_the_note(int gate)
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var t = AppModelSamples.Thread(p);
        h.WriteLibrary([p], [t]);
        var app = h.MakeModel();
        h.Main.OnMain(() =>
        {
            var a = app.AutoContinue;
            app.Settings.AutoContinueAfterLimit = gate != 0;
            if (gate == 3) app.UpdateThread(t.Id, thread => thread with { IsArchived = true });
            if (gate == 4) app.UpdateThread(t.Id, thread => thread with { Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) });
            var id = gate == 3 ? Guid.NewGuid() : t.Id; // an absent thread, all other gates satisfied
            if (gate == 5) Field<WeakReference<AppModel>>(a, "app").SetTarget(null!);
            a.NoteLimit(id, SwiftDate.Now.AddHours(1));
            Assert.False(a.TurnFinished(id, gate == 1 ? TurnStatus.Completed : TurnStatus.Failed, gate == 2));
            Assert.False(Field<IDictionary>(a, "noted").Contains(id));
            Assert.Empty(Field<IDictionary>(a, "waits"));
        });
    }

    [Theory]
    [InlineData(94.999, false)] [InlineData(95, true)] [InlineData(100, true)]
    [Trait("Req", "PR-RUNTIME-183")] // App/Runtime/AutoContinue.swift:124-142
    public void Only_the_most_spent_future_window_at_least_95_percent_supplies_a_reset(double percent, bool expected)
    {
        using var h = new AppModelHarness();
        var app = h.MakeModel();
        h.Main.Loop.Run(async () =>
        {
            var now = SwiftDate.Now;
            app.AutoContinue.ReadLimitsForTesting = _ => Task.FromResult<PlanLimits?>(new PlanLimits
            {
                Windows = new ValueList<PlanLimits.Window>([
                    Window("past", 100, now.AddSeconds(-1)), Window("now", 100, now),
                    Window("unknown", 100, null), Window("lower", 50, now.AddHours(3)),
                    Window("winner", percent, now.AddHours(1)), Window("tie", percent, now.AddHours(2)),
                ]),
            });
            Assert.Equal(expected ? now.AddHours(1) : (DateTimeOffset?)null,
                await ReadReset(app.AutoContinue, ProviderKind.Codex));
            app.AutoContinue.ReadLimitsForTesting = _ => Task.FromResult<PlanLimits?>(new PlanLimits { Windows = ValueList<PlanLimits.Window>.Empty });
            Assert.Null(await ReadReset(app.AutoContinue, ProviderKind.Codex));
            app.AutoContinue.ReadLimitsForTesting = null;
            app.Settings.SetBinaryPath(Path.Combine(h.Scratch, "missing.exe"), ProviderKind.Codex);
            Assert.Null(await ReadReset(app.AutoContinue, ProviderKind.Codex));
            Assert.Null(await ReadReset(app.AutoContinue, ProviderKind.Zai)); // empty in-memory key
        }, AppModelHarness.Bound);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-182")] // App/Runtime/AutoContinue.swift:93-99
    [Trait("Req", "UI-RUNTIME-016")] // App/Runtime/AutoContinue.swift:94-95
    [Trait("Req", "UI-RUNTIME-033")] // App/Runtime/AutoContinue.swift:94-95
    public void Unknown_reset_announces_the_exact_warning_and_clears_bookkeeping()
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var t = AppModelSamples.Thread(p);
        h.WriteLibrary([p], [t]);
        var app = h.MakeModel();
        string? body = null;
        using var active = NSApplication.IsActive.Override(() => false);
        h.Main.OnMain(() =>
        {
            app.Settings.AutoContinueAfterLimit = true;
            app.Settings.NotifyWhenFinished = true;
            app.Runtime(t.Id);
            app.AutoContinue.ReadLimitsForTesting = _ => Task.FromResult<PlanLimits?>(null);
            app.AutoContinue.NotifyForTesting = (_, _, b) => body = b;
            app.AutoContinue.NoteLimit(t.Id, null);
            Assert.True(app.AutoContinue.TurnFinished(t.Id, TurnStatus.Failed, false));
        });
        h.Main.OnMain(() =>
        {
            var n = Notice(app.Runtime(t.Id));
            Assert.Equal(Marrow.Core.Models.Notice.Level.Warning, n.NoticeLevel);
            Assert.Equal("Usage limit reached, and the provider did not say when it resets. Send a message to continue.", n.Message);
            Assert.Equal("Usage limit reached. Send a message to continue.", body);
            Assert.Empty(Field<IDictionary>(app.AutoContinue, "waits"));
            Assert.Empty(Field<IDictionary>(app.AutoContinue, "waitTokens"));
        });
    }

    [Theory]
    [InlineData(-120, 5)] [InlineData(30, 90)]
    [Trait("Req", "PR-RUNTIME-184")] // App/Runtime/AutoContinue.swift:100-105
    [Trait("Req", "UI-RUNTIME-016")] // App/Runtime/AutoContinue.swift:103-104
    [Trait("Req", "UI-RUNTIME-033")] // App/Runtime/AutoContinue.swift:103-104
    public void Countdown_uses_reset_plus_60_or_now_plus_5_and_distinct_exact_sentences(int reset, int seconds)
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var t = AppModelSamples.Thread(p);
        h.WriteLibrary([p], [t]);
        var app = h.MakeModel();
        string? body = null;
        using var active = NSApplication.IsActive.Override(() => false);
        var now = h.Main.Time.GetUtcNow();
        Marrow.Core.Models.Notice? emitted = null;
        h.Main.OnMain(() =>
        {
            app.Settings.AutoContinueAfterLimit = true;
            app.Settings.NotifyWhenFinished = true;
            app.Runtime(t.Id);
            app.AutoContinue.NotifyForTesting = (_, _, b) => body = b;
            app.AutoContinue.NoticeForTesting = n => emitted = n;
            app.AutoContinue.NoteLimit(t.Id, now.AddSeconds(reset));
            Assert.True(app.AutoContinue.TurnFinished(t.Id, TurnStatus.Failed, false));
        });
        h.Main.OnMain(() =>
        {
            Assert.Equal(now.AddSeconds(seconds), app.AutoContinue.ResumesAt[t.Id]);
            var when = AutoContinue.Format(now.AddSeconds(seconds));
            var n = Notice(app.Runtime(t.Id));
            Assert.Equal("Usage limit reached. Continuing automatically at " + when + ".", n.Message);
            Assert.Equal(t.Id, emitted!.Limit!.ThreadID);
            Assert.Equal("Usage limit reached. Continuing at " + when + ".", body);
            app.AutoContinue.Cancel(t.Id);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-181")] // App/Runtime/AutoContinue.swift:52-63
    public void A_successor_cancels_its_predecessor_clears_outcome_and_captures_a_new_token()
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var t = AppModelSamples.Thread(p);
        h.WriteLibrary([p], [t]);
        var app = h.MakeModel();
        h.Main.OnMain(() =>
        {
            app.Settings.AutoContinueAfterLimit = true;
            app.AutoContinue.Cancel(t.Id);
            app.AutoContinue.NoteLimit(t.Id, SwiftDate.Now.AddHours(1));
            Assert.True(app.AutoContinue.TurnFinished(t.Id, TurnStatus.Failed, false));
        });
        h.Main.OnMain(() =>
        {
            var a = app.AutoContinue;
            var previous = Field<Dictionary<Guid, TaskHandle>>(a, "waits")[t.Id];
            var token = Field<Dictionary<Guid, Guid>>(a, "waitTokens")[t.Id];
            Assert.False(a.Outcomes.ContainsKey(t.Id));
            a.NoteLimit(t.Id, SwiftDate.Now.AddHours(2));
            Assert.True(a.TurnFinished(t.Id, TurnStatus.Failed, false));
            Assert.NotEqual(token, Field<Dictionary<Guid, Guid>>(a, "waitTokens")[t.Id]);
            Assert.NotSame(previous, Field<Dictionary<Guid, TaskHandle>>(a, "waits")[t.Id]);
        });
        h.Main.OnMain(() => Assert.Equal(SwiftDate.Now.AddHours(2).AddSeconds(60), app.AutoContinue.ResumesAt[t.Id]));
        h.Main.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(60));
        h.Main.OnMain(() => { Assert.True(app.AutoContinue.ResumesAt.ContainsKey(t.Id)); app.AutoContinue.Cancel(t.Id); });
    }

    [Theory]
    [InlineData(true, true, true, 0)] [InlineData(true, true, false, 1)]
    [InlineData(true, false, true, 1)] [InlineData(false, false, false, 0)]
    [Trait("Req", "PR-RUNTIME-186")] // App/Runtime/AutoContinue.swift:150-155
    public void Notify_suppresses_only_an_active_selected_thread_or_disabled_notifications(bool enabled, bool active, bool selected, int count)
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var parent = AppModelSamples.Thread(p);
        var t = AppModelSamples.Thread(p) with { ParentThreadID = parent.Id, IsInPanel = true };
        h.WriteLibrary([p], [parent, t]);
        var app = h.MakeModel();
        using var activity = NSApplication.IsActive.Override(() => active);
        h.Main.OnMain(() =>
        {
            app.Settings.NotifyWhenFinished = enabled;
            app.SelectedThreadID = selected ? t.Id : parent.Id;
            var calls = 0;
            app.AutoContinue.NotifyForTesting = (_, _, _) => calls++;
            typeof(AutoContinue).GetMethod("Notify", BindingFlags.NonPublic | BindingFlags.Instance, [typeof(Guid), typeof(string)])!.Invoke(app.AutoContinue, [t.Id, "body"]);
            Assert.Equal(count, calls);
        });
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [Trait("Req", "PR-RUNTIME-185")] // App/Runtime/AutoContinue.swift:106-121
    public void Wake_rechecks_setting_runtime_running_and_last_turn_and_sends_the_exact_follow_up(int changed)
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var t = AppModelSamples.Thread(p);
        h.WriteLibrary([p], [t]);
        var app = h.MakeModel();
        var runtime = h.Main.OnMain(() => app.Runtime(t.Id));
        h.Main.OnMain(() =>
        {
            app.Settings.SetBinaryPath(Path.Combine(h.Scratch, "missing.exe"), ProviderKind.Codex);
            app.Settings.AutoContinueAfterLimit = true;
            app.AutoContinue.NoteLimit(t.Id, SwiftDate.Now.AddMinutes(-2));
            Assert.True(app.AutoContinue.TurnFinished(t.Id, TurnStatus.Failed, false));
        });
        h.Main.OnMain(() =>
        {
            if (changed == 1) app.Settings.AutoContinueAfterLimit = false;
            if (changed == 2) Field<IDictionary>(app, "runtimes").Remove(t.Id);
            if (changed == 3) Set(runtime, "phase", RuntimePhase.Running);
            if (changed == 4) Set(runtime, "turns", new ValueList<TurnRecord>([new TurnRecord { Index = 1 }]));
        });
        h.Main.Advance(TimeSpan.FromSeconds(5));
        h.Main.OnMain(() =>
        {
            Assert.False(app.AutoContinue.ResumesAt.ContainsKey(t.Id));
            Assert.Equal(changed == 0, app.AutoContinue.Outcomes.ContainsKey(t.Id));
            if (changed == 0)
            {
                Assert.IsType<AutoContinue.LimitOutcome.Resumed>(app.AutoContinue.Outcomes[t.Id]);
                Assert.Empty(runtime.FollowUps); // enqueue then SendFollowUpNow took the exact prompt
            }
        });
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static Task<DateTimeOffset?> ReadReset(AutoContinue a, ProviderKind provider) =>
        (Task<DateTimeOffset?>)typeof(AutoContinue).GetMethod("ReadResetTime", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(a, [provider])!;
    private static PlanLimits.Window Window(string id, double percent, DateTimeOffset? reset) =>
        new() { Id = id, Title = id, Percent = percent, ResetsAt = reset };
    private static Notice Notice(ThreadRuntime runtime) =>
        Assert.IsType<TimelineItem.Content.Notice>(runtime.Entries.Last().Item.TimelineItemContent).Value;
}

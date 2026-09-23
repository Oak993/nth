using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;

namespace Marrow.App.Tests.Views.Settings;

// Tests for DroppyCode/App/Views/Settings/SettingsScrollTracker.swift (B2 1fbf81e), :1-56, record PR-SETTINGS-019.
//
// Every expected value is transcribed from the Swift: the 150 ms settle (:30), the freeze rule (:27), coast()
// (:41-44), end() (:47-55), the one fixed pane id (:10, `private static let pane = UUID()`), the report of the
// start once per scroll (:23-26) with `reporter: self` (:25), and the report of the stop (:34, :54).
//
// The pane id is private, so what the tracker reports is observed through ScrollActivity
// (DroppyCode/UI/Common/RenderBudget.swift:12-73), replaced per test through its Ambient `Shared` (R-LIFE-2).
// Because the pane id is static, two trackers report under the same id: a stop from one clears the pane the
// other still scrolls. The tests use that to observe the id, the "once" rules and the timer cancellation.
// The watchdog of RenderBudget.swift:44-62 (2 s sweep, drops a timeline whose reporter is gone or answers
// isScrollingNow == false) is used to observe that the tracker reports itself as the reporter.
//
// API assumed (R-NAME-1 to R-NAME-6, R-FILE-3, R-OBS-1, R-OBS-6, R-PROTO-1, R-LIFE-2):
//   Marrow.App.Views.Settings.SettingsScrollTracker : MainActorModel, ScrollActivityReporter, with a public
//   parameterless constructor, bool IsScrolling and bool IsFrozen (getter public, setter not), bool
//   IsScrollingNow, void Note(), void Coast(), void End().
//   Marrow.UI.Common.ScrollActivity : MainActorModel with a public parameterless constructor (the Swift type
//   has no explicit init), static Ambient<ScrollActivity> Shared, bool IsScrolling.
//   Marrow.UI.Common.ScrollActivityReporter, an interface with bool IsScrollingNow { get; }.
public sealed class SettingsScrollTrackerTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150); // :30

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:11-19
    public void A_new_tracker_is_neither_scrolling_nor_frozen_and_reports_nothing()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var tracker = runtime.OnMain(() => new SettingsScrollTracker());

        AssertState(runtime, tracker, scrolling: false, frozen: false);
        Assert.False(Reported(runtime, activity));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:22-27
    public void The_first_movement_frame_marks_the_pane_scrolling_freezes_the_rows_and_reports_to_ScrollActivity()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var tracker = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(tracker.Note);

        AssertState(runtime, tracker, scrolling: true, frozen: true);
        Assert.True(Reported(runtime, activity));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:28-36
    public void Scrolling_settles_150_ms_after_the_only_movement_frame_and_reports_the_pane_stopped()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var tracker = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(tracker.Note);                     // t = 0
        runtime.AdvanceMilliseconds(149);                 // t = 149
        AssertState(runtime, tracker, scrolling: true, frozen: true);
        Assert.True(Reported(runtime, activity));

        runtime.AdvanceMilliseconds(1);                   // t = 150
        AssertState(runtime, tracker, scrolling: false, frozen: false);
        Assert.False(Reported(runtime, activity));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:28-36
    public void Every_movement_frame_cancels_and_restarts_the_150_ms_timer()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var tracker = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(tracker.Note);                     // t = 0, first settle due at 150
        runtime.AdvanceMilliseconds(100);
        runtime.OnMain(tracker.Note);                     // t = 100, first settle cancelled, next due at 250

        runtime.AdvanceMilliseconds(50);                  // t = 150: the cancelled settle does nothing
        AssertState(runtime, tracker, scrolling: true, frozen: true);
        Assert.True(Reported(runtime, activity));

        runtime.AdvanceMilliseconds(99);                  // t = 249
        AssertState(runtime, tracker, scrolling: true, frozen: true);
        Assert.True(Reported(runtime, activity));

        runtime.AdvanceMilliseconds(1);                   // t = 250
        AssertState(runtime, tracker, scrolling: false, frozen: false);
        Assert.False(Reported(runtime, activity));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:39-44
    public void Coast_thaws_the_rows_at_once_while_the_pane_still_reports_scrolling_until_the_settle()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var tracker = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(tracker.Note);                     // t = 0, settle due at 150
        runtime.AdvanceMilliseconds(50);
        runtime.OnMain(tracker.Coast);                    // t = 50

        AssertState(runtime, tracker, scrolling: true, frozen: false);
        Assert.True(Reported(runtime, activity));

        runtime.AdvanceMilliseconds(99);                  // t = 149: coast() left the settle running
        AssertState(runtime, tracker, scrolling: true, frozen: false);
        Assert.True(Reported(runtime, activity));

        runtime.AdvanceMilliseconds(1);                   // t = 150
        AssertState(runtime, tracker, scrolling: false, frozen: false);
        Assert.False(Reported(runtime, activity));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:27-30
    public void A_movement_frame_while_coasting_keeps_the_rows_thawed_but_restarts_the_timer()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var tracker = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(tracker.Note);                     // t = 0
        runtime.OnMain(tracker.Coast);                    // t = 0
        runtime.AdvanceMilliseconds(100);
        runtime.OnMain(tracker.Note);                     // t = 100, coasting: not frozen, settle due at 250

        AssertState(runtime, tracker, scrolling: true, frozen: false);

        runtime.AdvanceMilliseconds(149);                 // t = 249
        AssertState(runtime, tracker, scrolling: true, frozen: false);
        Assert.True(Reported(runtime, activity));

        runtime.AdvanceMilliseconds(1);                   // t = 250
        AssertState(runtime, tracker, scrolling: false, frozen: false);
        Assert.False(Reported(runtime, activity));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:27, :31-35
    public void The_settle_clears_coasting_so_the_next_movement_freezes_the_rows_again()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var tracker = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(tracker.Note);
        runtime.OnMain(tracker.Coast);
        runtime.Advance(Settle);
        AssertState(runtime, tracker, scrolling: false, frozen: false);

        runtime.OnMain(tracker.Note);

        AssertState(runtime, tracker, scrolling: true, frozen: true);
        Assert.True(Reported(runtime, activity));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:27, :41-44
    public void Coast_on_a_resting_pane_reports_nothing_and_the_next_movement_stays_thawed_until_it_settles()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var tracker = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(tracker.Coast);
        AssertState(runtime, tracker, scrolling: false, frozen: false);
        Assert.False(Reported(runtime, activity));

        runtime.OnMain(tracker.Note);                     // still coasting: scrolling, not frozen
        AssertState(runtime, tracker, scrolling: true, frozen: false);
        Assert.True(Reported(runtime, activity));

        runtime.Advance(Settle);                          // the settle clears coasting
        AssertState(runtime, tracker, scrolling: false, frozen: false);
        Assert.False(Reported(runtime, activity));

        runtime.OnMain(tracker.Note);
        AssertState(runtime, tracker, scrolling: true, frozen: true);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:46-55
    public void End_mid_scroll_thaws_stops_scrolling_and_reports_the_pane_stopped_at_once()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var tracker = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(tracker.Note);                     // t = 0
        runtime.AdvanceMilliseconds(50);
        runtime.OnMain(tracker.End);                      // t = 50, no time passes before the report

        AssertState(runtime, tracker, scrolling: false, frozen: false);
        Assert.False(Reported(runtime, activity));

        runtime.AdvanceMilliseconds(200);                 // past the cancelled settle
        AssertState(runtime, tracker, scrolling: false, frozen: false);
        Assert.False(Reported(runtime, activity));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:10, :47-48, :31-35
    public void End_cancels_the_pending_settle_so_it_never_reports_a_later_scroll_of_the_pane_stopped()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var first = runtime.OnMain(() => new SettingsScrollTracker());
        var second = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(first.Note);                       // t = 0, first's settle due at 150
        runtime.AdvanceMilliseconds(50);
        runtime.OnMain(first.End);                        // t = 50, that settle is cancelled
        runtime.AdvanceMilliseconds(10);
        runtime.OnMain(second.Note);                      // t = 60, same pane id, second's settle due at 210
        Assert.True(Reported(runtime, activity));

        runtime.AdvanceMilliseconds(90);                  // t = 150: a live first settle would report the pane stopped here
        Assert.True(Reported(runtime, activity));
        AssertState(runtime, first, scrolling: false, frozen: false);
        AssertState(runtime, second, scrolling: true, frozen: true);

        runtime.AdvanceMilliseconds(59);                  // t = 209
        Assert.True(Reported(runtime, activity));

        runtime.AdvanceMilliseconds(1);                   // t = 210
        Assert.False(Reported(runtime, activity));
        AssertState(runtime, second, scrolling: false, frozen: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:27, :50
    public void End_clears_coasting_so_the_next_movement_freezes_the_rows()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var tracker = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(tracker.Note);
        runtime.OnMain(tracker.Coast);
        runtime.OnMain(tracker.End);
        AssertState(runtime, tracker, scrolling: false, frozen: false);

        runtime.OnMain(tracker.Note);

        AssertState(runtime, tracker, scrolling: true, frozen: true);
        Assert.True(Reported(runtime, activity));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:10, :51
    public void End_on_a_tracker_that_was_not_scrolling_reports_nothing()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var resting = runtime.OnMain(() => new SettingsScrollTracker());
        var moving = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(moving.Note);                      // the pane id is now reported as scrolling
        Assert.True(Reported(runtime, activity));

        runtime.OnMain(resting.End);                      // guard isScrolling else { return }: no stop report

        Assert.True(Reported(runtime, activity));
        AssertState(runtime, resting, scrolling: false, frozen: false);
        AssertState(runtime, moving, scrolling: true, frozen: true);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:10, :51-54
    public void End_reports_the_pane_stopped_exactly_once()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var closing = runtime.OnMain(() => new SettingsScrollTracker());
        var other = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(closing.Note);
        runtime.OnMain(closing.End);                      // the one stop report
        Assert.False(Reported(runtime, activity));

        runtime.OnMain(other.Note);                       // the pane id scrolls again
        Assert.True(Reported(runtime, activity));

        runtime.OnMain(closing.End);                      // a second end() reports nothing

        Assert.True(Reported(runtime, activity));
        AssertState(runtime, closing, scrolling: false, frozen: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:10, :25, :54
    public void Every_tracker_reports_under_the_one_fixed_pane_id()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var first = runtime.OnMain(() => new SettingsScrollTracker());
        var second = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(first.Note);
        runtime.OnMain(second.Note);
        Assert.True(Reported(runtime, activity));

        runtime.OnMain(first.End);                        // withdraws the shared pane id

        // With one id per tracker, the second tracker's scroll would still be on record.
        Assert.False(Reported(runtime, activity));
        AssertState(runtime, second, scrolling: true, frozen: true);

        runtime.Advance(Settle);
        AssertState(runtime, second, scrolling: false, frozen: false);
        Assert.False(Reported(runtime, activity));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:10, :23-26
    public void The_start_is_reported_once_per_scroll_not_on_every_movement_frame()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var first = runtime.OnMain(() => new SettingsScrollTracker());
        var second = runtime.OnMain(() => new SettingsScrollTracker());

        runtime.OnMain(first.Note);                       // reports the start
        runtime.OnMain(second.Note);
        runtime.OnMain(second.End);                       // withdraws the shared pane id
        Assert.False(Reported(runtime, activity));

        runtime.OnMain(first.Note);                       // already scrolling: no second start report

        Assert.False(Reported(runtime, activity));
        AssertState(runtime, first, scrolling: true, frozen: true);

        runtime.Advance(Settle);
        AssertState(runtime, first, scrolling: false, frozen: false);
        Assert.False(Reported(runtime, activity));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:20, :25; UI/Common/RenderBudget.swift:44-62
    public void The_tracker_reports_itself_so_the_2_s_watchdog_keeps_a_long_scroll_on_record()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var tracker = runtime.OnMain(() => new SettingsScrollTracker());

        // A movement frame every 100 ms from t = 0 to t = 2500. The watchdog sweeps at t = 2000 and keeps the
        // pane only if its reporter is alive and answers isScrollingNow == true (RenderBudget.swift:51-55).
        runtime.OnMain(tracker.Note);
        for (var elapsed = 100; elapsed <= 2500; elapsed += 100)
        {
            runtime.AdvanceMilliseconds(100);
            Assert.True(Reported(runtime, activity));
            runtime.OnMain(tracker.Note);
        }

        AssertState(runtime, tracker, scrolling: true, frozen: true);

        runtime.AdvanceMilliseconds(149);                 // t = 2649
        Assert.True(Reported(runtime, activity));

        runtime.AdvanceMilliseconds(1);                   // t = 2650
        AssertState(runtime, tracker, scrolling: false, frozen: false);
        Assert.False(Reported(runtime, activity));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-019")] // App/Views/Settings/SettingsScrollTracker.swift:7-8, :11, :15, :27, :33-35, :43
    [Trait("Rule", "R-OBS-1")]
    public void Scrolling_and_frozen_are_published_to_bindings_as_they_change()
    {
        using var runtime = new TestRuntime();
        var activity = runtime.OnMain(() => new ScrollActivity());
        using var shared = ScrollActivity.Shared.Override(activity);
        var tracker = runtime.OnMain(() => new SettingsScrollTracker());
        using var log = new ChangeLog(tracker);

        runtime.OnMain(tracker.Note);
        Assert.Equal(new[] { "IsFrozen", "IsScrolling" }, Published(log));

        log.Names.Clear();
        runtime.OnMain(tracker.Coast);                    // :43 thaws; :42 isCoasting is @ObservationIgnored
        Assert.Equal(new[] { "IsFrozen" }, Published(log));
        Assert.DoesNotContain("IsCoasting", log.Names);

        log.Names.Clear();
        runtime.Advance(Settle);                          // :32 isScrolling = false; isFrozen already false
        Assert.Equal(new[] { "IsScrolling" }, Published(log));
        Assert.DoesNotContain("IsCoasting", log.Names);
    }

    // The stored tracked properties only, in ordinal order; a computed property such as IsScrollingNow may
    // also be told when what it read changes (R-OBS-4), which is not what this test checks.
    private static string[] Published(ChangeLog log) =>
        log.Names.Where(n => n is "IsScrolling" or "IsFrozen").Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    [Trait("Rule", "R-OBS-6")]
    [Trait("Rule", "R-PROTO-1")]
    [Trait("Rule", "R-NAME-6")]
    public void The_tracker_is_a_main_actor_observable_scroll_activity_reporter_with_read_only_state()
    {
        // SettingsScrollTracker.swift:7-9 @MainActor @Observable final, conforming to ScrollActivityReporter;
        // :11 and :15 private(set); :16-17 private isCoasting and settle.
        var type = typeof(SettingsScrollTracker);
        Assert.True(type.IsSealed);
        Assert.True(typeof(MainActorModel).IsAssignableFrom(type));
        Assert.True(typeof(ScrollActivityReporter).IsAssignableFrom(type));

        foreach (var name in new[] { "IsScrolling", "IsFrozen" })
        {
            var property = type.GetProperty(name);
            Assert.NotNull(property);
            Assert.True(property.GetMethod!.IsPublic);
            Assert.False(property.SetMethod?.IsPublic ?? false);
        }

        Assert.Null(type.GetProperty("IsCoasting"));
        Assert.Null(type.GetProperty("Settle"));
    }

    private static bool Reported(TestRuntime runtime, ScrollActivity activity) =>
        runtime.OnMain(() => activity.IsScrolling);

    private static void AssertState(TestRuntime runtime, SettingsScrollTracker tracker, bool scrolling, bool frozen)
    {
        var (isScrolling, isFrozen, isScrollingNow, reporterAnswer) = runtime.OnMain(() =>
            (tracker.IsScrolling, tracker.IsFrozen, tracker.IsScrollingNow, ((ScrollActivityReporter)tracker).IsScrollingNow));
        Assert.Equal(scrolling, isScrolling);
        Assert.Equal(frozen, isFrozen);
        Assert.Equal(scrolling, isScrollingNow);                  // :20 isScrollingNow { isScrolling }
        Assert.Equal(scrolling, reporterAnswer);
    }
}

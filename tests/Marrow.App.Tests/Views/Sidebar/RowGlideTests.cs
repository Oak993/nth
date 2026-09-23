using Avalonia;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Sidebar;

// DroppyCode/App/Views/Sidebar/RowGlide.swift (B2 1fbf81e), the model half: RowGlideAnimator (:12-138). Every
// expectation is read from that file and from records PR-SIDEBAR-136, -137, -138, -139, -141, never from a C# port.
// The three AppKit reads (:76 NSWorkspace, :135-136 NSScreen/ImageRenderer) go through Marrow.UI.Port.IGeniePlatform,
// the seam UI/Common/Genie.swift's GenieAnimator already uses for the identical reads (Fixtures/RowGlideFixtures.cs).
public sealed class RowGlideTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-0000000000B2");

    // A thread row's frame in the window, the list's visible frame, and a fallback just past its bottom edge: the
    // shapes :63-64 and :44-45 describe. Numbers are arbitrary test geometry; only their relations matter.
    private static readonly Rect From = new(10, 40, 240, 28);
    private static readonly Rect Fallback = new(10, 400, 240, 28);
    private static readonly Rect Bounds = new(0, 0, 300, 380);
    private static readonly Rect Landing = new(18, 160, 220, 32);

    private static readonly FaceSample Departure = new(240, 28);
    private static readonly FaceSample Arrival = new(220, 32);

    private static RowGlideAnimator NewAnimator(bool reduceMotion = false) =>
        new(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });

    private static void LaunchGlide(RowGlideAnimator animator, Guid thread, RowGlideAnimator.Glide.Direction direction, Rect? from = null) =>
        animator.Launch(
            threadID: thread, direction: direction, from: from ?? From, fallback: Fallback, bounds: Bounds,
            departure: Departure, arrival: Arrival, departureFill: 0.1, arrivalFill: 0);

    [Fact]
    [Trait("Req", "PR-SIDEBAR-136")] // App/Views/Sidebar/RowGlide.swift:65-83
    public void Launch_appends_a_glide_headed_for_the_fallback_and_hides_the_threads_row()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = NewAnimator();
            Assert.Empty(animator.Glides); // :48 `= []`
            Assert.False(animator.IsHiding(A)); // :102 a cell starts `false`
            var launchedAt = SwiftDate.Now;
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            var glide = Assert.Single(animator.Glides);
            Assert.NotEqual(Guid.Empty, glide.Id); // :26 `let id = UUID()`
            Assert.Equal(A, glide.ThreadID);
            Assert.Equal(RowGlideAnimator.Glide.Direction.Reopen, glide.GlideDirection);
            Assert.Same(Departure, glide.Departure);
            Assert.Same(Arrival, glide.Arrival);
            Assert.Equal(0.1, glide.DepartureFill);
            Assert.Equal(0, glide.ArrivalFill);
            Assert.Equal(From, glide.From);
            Assert.Equal(Fallback, glide.To); // :80 `to: fallback`
            Assert.Equal(Bounds, glide.Bounds);
            Assert.Equal(launchedAt, glide.LaunchedAt); // :40 `let launchedAt = Date.now`
            Assert.False(glide.HasArrived); // :42
            Assert.False(glide.HasLanded); // :45
            Assert.True(animator.IsHiding(A)); // :82 `setHiding(threadID, true)`
            Assert.False(animator.IsHiding(B));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-136")] // App/Views/Sidebar/RowGlide.swift:76
    public void A_source_frame_no_wider_or_taller_than_one_point_launches_nothing_and_hides_nothing()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = NewAnimator();
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen, from: new Rect(10, 40, 1, 28));
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen, from: new Rect(10, 40, 240, 1));
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen, from: new Rect(10, 40, 0, 0));
            Assert.Empty(animator.Glides);
            Assert.False(animator.IsHiding(A));

            // `> 1` is strict and holds for anything above one point.
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen, from: new Rect(10, 40, 1.5, 1.5));
            Assert.Single(animator.Glides);
            Assert.True(animator.IsHiding(A));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-136")] // App/Views/Sidebar/RowGlide.swift:76-77
    public void A_refused_launch_returns_before_it_removes_the_threads_glide_already_in_the_air()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = NewAnimator();
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            var first = Assert.Single(animator.Glides);
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Settle, from: new Rect(10, 40, 1, 1)); // the guard is :76, the removal :77
            Assert.Equal(first.Id, Assert.Single(animator.Glides).Id);
            Assert.True(animator.IsHiding(A));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-136")] // App/Views/Sidebar/RowGlide.swift:76
    public void With_Reduce_Motion_on_nothing_flies_and_the_row_is_never_hidden()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = NewAnimator(reduceMotion: true);
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            LaunchGlide(animator, B, RowGlideAnimator.Glide.Direction.Settle);
            Assert.Empty(animator.Glides);
            Assert.False(animator.IsHiding(A));
            Assert.False(animator.IsHiding(B));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-136")] // App/Views/Sidebar/RowGlide.swift:76
    public void Reduce_Motion_turned_on_mid_flight_refuses_the_next_launch_and_leaves_the_glide_in_the_air()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var platform = new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false };
            var animator = new RowGlideAnimator(platform);
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            var first = Assert.Single(animator.Glides);
            platform.AccessibilityDisplayShouldReduceMotion = true;
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            Assert.Equal(first.Id, Assert.Single(animator.Glides).Id); // the guard returns before :77 removes anything
            Assert.True(animator.IsHiding(A));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-136")] // App/Views/Sidebar/RowGlide.swift:77-82
    public void A_relaunch_replaces_the_threads_glide_keeps_the_others_and_the_row_stays_hidden()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = NewAnimator();
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            LaunchGlide(animator, B, RowGlideAnimator.Glide.Direction.Reopen);
            var firstA = animator.Glides[0];
            var firstB = animator.Glides[1];
            Assert.Equal(A, firstA.ThreadID);
            Assert.Equal(B, firstB.ThreadID);

            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Settle);
            // :77 removes A's glide, :78 appends the new one at the end.
            Assert.Equal(2, animator.Glides.Count);
            Assert.Equal(firstB.Id, animator.Glides[0].Id);
            var secondA = animator.Glides[1];
            Assert.Equal(A, secondA.ThreadID);
            Assert.NotEqual(firstA.Id, secondA.Id); // a new UUID per Glide (:26)
            Assert.Equal(RowGlideAnimator.Glide.Direction.Settle, secondA.GlideDirection);
            Assert.True(animator.IsHiding(A));

            // The replaced glide's own task ends with arrive and finish for its id (:233, :235); both find nothing
            // (:117, :123), so the new glide inherits the hidden row.
            animator.Arrive(firstA.Id);
            animator.Finish(firstA.Id);
            Assert.Equal(2, animator.Glides.Count);
            Assert.False(animator.Glides[1].HasArrived);
            Assert.True(animator.IsHiding(A));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-137")] // App/Views/Sidebar/RowGlide.swift:86-92
    public void Landing_within_the_window_and_matching_direction_retargets_the_ghost()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = NewAnimator();
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            animator.Land(threadID: A, settled: false, at: Landing); // reopen matches `settled == false` (:89)
            var glide = Assert.Single(animator.Glides);
            Assert.Equal(Landing, glide.To);
            Assert.True(glide.HasLanded);
            Assert.Equal(From, glide.From); // only `to` moves (:90)
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-137")] // App/Views/Sidebar/RowGlide.swift:89
    public void Landing_whose_settled_state_does_not_match_the_glides_direction_is_ignored()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = NewAnimator();
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            animator.Land(threadID: A, settled: true, at: Landing); // reopen wants settled == false
            var glide = Assert.Single(animator.Glides);
            Assert.Equal(Fallback, glide.To);
            Assert.False(glide.HasLanded);
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-137")] // App/Views/Sidebar/RowGlide.swift:87
    public void Landing_for_a_thread_with_no_glide_does_nothing()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = NewAnimator();
            animator.Land(threadID: A, settled: false, at: Landing);
            Assert.Empty(animator.Glides);
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-137")] // App/Views/Sidebar/RowGlide.swift:61, :88
    public void A_landing_report_outside_the_half_second_window_is_ignored()
    {
        using var runtime = new TestRuntime();
        RowGlideAnimator animator = null!;
        runtime.OnMain(() =>
        {
            animator = NewAnimator();
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
        });
        runtime.AdvanceMilliseconds(501); // `< landingWindow` (:88): 0.5s inclusive fails just past it
        runtime.OnMain(() =>
        {
            animator.Land(threadID: A, settled: false, at: Landing);
            var glide = Assert.Single(animator.Glides);
            Assert.Equal(Fallback, glide.To);
            Assert.False(glide.HasLanded);
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-137")] // App/Views/Sidebar/RowGlide.swift:90-91, "the latest report inside the window wins"
    public void A_later_landing_report_inside_the_window_replaces_an_earlier_one()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = NewAnimator();
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            var earlier = new Rect(18, 150, 220, 32);
            animator.Land(threadID: A, settled: false, at: earlier);
            animator.Land(threadID: A, settled: false, at: Landing);
            Assert.Equal(Landing, Assert.Single(animator.Glides).To);
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-138")] // App/Views/Sidebar/RowGlide.swift:93-98, :100-105, :109-112
    public void IsHiding_reads_a_per_thread_cell_made_on_first_ask_and_kept_once_made()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = NewAnimator();
            Assert.False(animator.IsHiding(A)); // :102 a cell starts `false`
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            Assert.True(animator.IsHiding(A));
            Assert.False(animator.IsHiding(B)); // a different thread's own cell, untouched
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-138")] // App/Views/Sidebar/RowGlide.swift:116-120, :122-128
    public void Arrive_shows_the_row_and_finish_removes_the_glide_when_nothing_else_is_in_the_air()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = NewAnimator();
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            var id = Assert.Single(animator.Glides).Id;
            animator.Arrive(id);
            Assert.True(Assert.Single(animator.Glides).HasArrived); // :118
            Assert.False(animator.IsHiding(A)); // :119
            animator.Finish(id);
            Assert.Empty(animator.Glides); // :125
            Assert.False(animator.IsHiding(A)); // :127, none un-arrived left
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-138")] // App/Views/Sidebar/RowGlide.swift:126-127, "two glides for one thread"
    public void A_thread_with_two_glides_stays_hidden_until_every_un_arrived_one_has_arrived()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = NewAnimator();
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            var firstId = Assert.Single(animator.Glides).Id;
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Settle); // :77 replaces the first
            var secondId = Assert.Single(animator.Glides).Id;
            Assert.NotEqual(firstId, secondId);
            // The replaced glide's own task still calls arrive/finish for its own id (:233, :235); both no-op (:117,
            // :123) because it is gone from `glides`, so only the surviving glide's own arrive matters here.
            animator.Finish(firstId);
            Assert.True(animator.IsHiding(A));
            animator.Arrive(secondId);
            Assert.False(animator.IsHiding(A));
            animator.Finish(secondId);
            Assert.False(animator.IsHiding(A));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-138")] // App/Views/Sidebar/RowGlide.swift:117, :123
    public void Arrive_and_finish_for_an_unknown_id_do_nothing()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var animator = NewAnimator();
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            var unknown = Guid.NewGuid();
            animator.Arrive(unknown);
            animator.Finish(unknown);
            Assert.Single(animator.Glides);
            Assert.True(animator.IsHiding(A));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-141")] // App/Views/Sidebar/RowGlide.swift:230-236, timing math only (no live view)
    public void The_handover_fires_arrive_at_settling_duration_minus_012_and_finish_012_later()
    {
        using var runtime = new TestRuntime();
        RowGlideAnimator animator = null!;
        Guid id = default;
        runtime.OnMain(() =>
        {
            animator = NewAnimator();
            LaunchGlide(animator, A, RowGlideAnimator.Glide.Direction.Reopen);
            id = animator.Glides[0].Id;
        });
        var duration = RowGlideAnimator.Spring.SettlingDuration; // :231
        var toArrive = Math.Max(duration - 0.12, 0); // :232
        runtime.AdvanceMilliseconds((long)(toArrive * 1000));
        runtime.OnMain(() =>
        {
            animator.Arrive(id); // what RowGlideView.task calls at this point (:233)
            Assert.True(animator.Glides[0].HasArrived);
            Assert.False(animator.IsHiding(A));
        });
        runtime.AdvanceMilliseconds(120); // :234, always exactly 0.12s regardless of `duration`
        runtime.OnMain(() =>
        {
            animator.Finish(id); // :235
            Assert.Empty(animator.Glides);
        });
    }
}

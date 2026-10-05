using Marrow.Core.Port.MultiAccount.Capsule;

namespace Marrow.Core.Tests.Port.MultiAccount.Capsule;

// MA-9 (step 3a): what the shared capsule shows for a pill (roles of the steps, the usage-light inputs, the name text, the geometry the styles draw).
[Trait("Feature", "MA-9")]
[Trait("Rule", "R-TEST-4")]
public sealed class CapsuleLayoutTests
{
    private static CapsuleStep Step(string mono, bool local = false) => new(mono, 20, local);

    private static CapsuleView View(int count = 3, int cur = 0, IReadOnlyList<int>? used = null, int? stand = null, bool handshake = false, double? use = null, double? nextUse = null, string? name = null) =>
        new([.. Enumerable.Range(0, count).Select(i => Step("S" + i))], cur, used, stand, handshake, use, nextUse, name);

    [Fact]
    public void Steps_before_the_current_one_are_used_and_the_ones_after_it_are_next()
    {
        Assert.Equal([StepRole.Used, StepRole.Cur, StepRole.Next], CapsuleLayout.Roles(View(3, cur: 1)).Select(r => r.Role));
    }

    [Fact]
    public void The_steps_a_thread_has_run_can_be_given_and_override_the_order()
    {
        var roles = CapsuleLayout.Roles(View(3, cur: 0, used: [2]));
        Assert.Equal([StepRole.Cur, StepRole.Next, StepRole.Used], roles.Select(r => r.Role));
    }

    [Fact]
    public void The_step_on_stand_by_is_marked_and_a_current_step_is_never_marked_used_or_next()
    {
        var roles = CapsuleLayout.Roles(View(3, cur: 1, stand: 2));
        Assert.Equal([false, false, true], roles.Select(r => r.Stand));
        var current = CapsuleLayout.Roles(View(3, cur: 1, used: [0, 1]));
        Assert.Equal(StepRole.Cur, current[1].Role);
    }

    [Fact]
    public void The_current_step_is_kept_inside_the_steps()
    {
        Assert.Equal(StepRole.Cur, CapsuleLayout.Roles(View(2, cur: 9))[1].Role);
        Assert.Equal(StepRole.Cur, CapsuleLayout.Roles(View(2, cur: -3))[0].Role);
    }

    [Fact]
    public void The_light_shows_what_is_left_of_the_current_account_thirty_percent_used_when_nothing_is_known()
    {
        Assert.Equal(0.7, CapsuleLayout.Left(View(), 0.3), 9);
        Assert.Equal(0.7, CapsuleLayout.Left(View(), null), 9);
        Assert.Equal(0.6, CapsuleLayout.Left(View(use: 0.4), null), 9);
        Assert.Equal(0, CapsuleLayout.Left(View(use: 1.7), null), 9);
    }

    [Fact]
    public void A_local_model_never_runs_low()
    {
        var local = new CapsuleView([Step("L", local: true), Step("S")], 0, null, null, false, 0.99, null, null);
        Assert.Equal(1, CapsuleLayout.Left(local, null), 9);
    }

    [Fact]
    public void The_next_light_follows_the_stand_by_step_only_while_a_handshake_runs_else_the_current_one()
    {
        var idle = View(3, cur: 0, use: 0.5, nextUse: 0.2);
        Assert.Equal(0.5, CapsuleLayout.NextLeft(idle), 9);
        var running = View(3, cur: 0, handshake: true, use: 0.5, nextUse: 0.2);
        Assert.Equal(0.8, CapsuleLayout.NextLeft(running), 9);
        var unknown = View(3, cur: 0, handshake: true, use: 0.5);
        Assert.Equal(0.9, CapsuleLayout.NextLeft(unknown), 9);
        var last = View(2, cur: 1, handshake: true, use: 0.5, nextUse: 0.1);
        Assert.Equal(0.5, CapsuleLayout.NextLeft(last), 9);
    }

    [Fact]
    public void The_next_step_is_the_stand_by_one_else_the_step_after_the_current_one()
    {
        Assert.Equal(1, CapsuleLayout.NextIndex(View(3, cur: 0)));
        Assert.Equal(2, CapsuleLayout.NextIndex(View(3, cur: 0, stand: 2)));
        Assert.Null(CapsuleLayout.NextIndex(View(3, cur: 2)));
    }

    [Fact]
    public void The_glow_swells_up_to_a_third_again_as_the_account_runs_low()
    {
        Assert.Equal(1, CapsuleLayout.GlowBoost(1), 3);
        Assert.Equal(1, CapsuleLayout.GlowBoost(0.5), 3);
        Assert.Equal(1.45, CapsuleLayout.GlowBoost(0.04), 3);
        Assert.Equal(1.45, CapsuleLayout.GlowBoost(0), 3);
        Assert.InRange(CapsuleLayout.GlowBoost(0.27), 1.01, 1.44);
    }

    [Fact]
    public void The_liquid_fill_is_the_use_between_four_percent_and_full_thirty_percent_when_unknown()
    {
        Assert.Equal(0.3, CapsuleLayout.Fill(View()), 9);
        Assert.Equal(0.04, CapsuleLayout.Fill(View(use: 0)), 9);
        Assert.Equal(0.62, CapsuleLayout.Fill(View(use: 0.62)), 9);
        Assert.Equal(1, CapsuleLayout.Fill(View(use: 4)), 9);
    }

    [Fact]
    public void The_hover_name_is_the_pills_name_alone_for_name_below_and_with_the_step_for_the_others()
    {
        var view = View(3, cur: 1, name: "Deep work");
        Assert.Equal("Deep work", CapsuleLayout.NameText("2", view));
        Assert.Equal("Deep work · step 2 of 3", CapsuleLayout.NameText("1", view));
        Assert.Equal("Deep work · step 2 of 3", CapsuleLayout.NameText("3", view));
        Assert.Null(CapsuleLayout.NameText("1", View()));
    }

    [Fact]
    public void Segmented_rim_splits_the_rim_in_one_segment_per_step_with_a_four_unit_gap()
    {
        var segments = CapsuleLayout.RimSegments(3);
        Assert.Equal(3, segments.Count);
        Assert.Equal(100.0 / 3 - 4, segments[0].Length, 2);
        Assert.Equal(2, segments[0].Start, 2);
        Assert.Equal(100.0 / 3 + 2, segments[1].Start, 2);
    }

    [Fact]
    public void Dial_splits_the_circle_with_a_five_unit_gap()
    {
        var segments = CapsuleLayout.DialSegments(4);
        Assert.Equal(4, segments.Count);
        Assert.Equal(20, segments[0].Length, 2);
        Assert.Equal(2.5, segments[0].Start, 2);
        Assert.Equal(27.5, segments[1].Start, 2);
    }

    [Fact]
    public void Liquid_fill_etches_one_tick_per_step_between_twelve_in_from_each_end_and_a_lone_step_in_the_middle()
    {
        var ticks = CapsuleLayout.Ticks(3, 44);
        Assert.Equal([12.0, 22.0, 32.0], ticks);
        Assert.Equal([22.0], CapsuleLayout.Ticks(1, 44));
    }

    [Fact]
    public void Orbit_dots_start_evenly_spaced_round_a_twenty_eight_second_lap()
    {
        var dots = CapsuleLayout.Orbit(4);
        Assert.Equal(28, CapsuleLayout.OrbitSeconds);
        Assert.Equal([0.0, 0.25, 0.5, 0.75], dots);
    }

    [Fact]
    public void The_plain_account_capsule_is_hot_from_ninety_percent_used_and_its_arc_is_the_use()
    {
        Assert.False(CapsuleLayout.IsHot(0.89));
        Assert.True(CapsuleLayout.IsHot(0.9));
        Assert.Equal(0.4, CapsuleLayout.Arc(0.4), 9);
        Assert.Equal(1, CapsuleLayout.Arc(3), 9);
        Assert.Equal(0, CapsuleLayout.Arc(-1), 9);
    }

    [Fact]
    public void The_capsule_outline_starts_at_the_top_middle_for_the_arc_and_the_left_middle_for_the_rim()
    {
        var plain = CapsuleLayout.CapsuleOutline(34, 22, 0.6, fromLeft: false);
        Assert.Equal((17.0, 0.6), plain[0]);
        var rim = CapsuleLayout.CapsuleOutline(36, 22, 0.75, fromLeft: true);
        Assert.Equal((0.75, 11.0), rim[0]);
    }
}

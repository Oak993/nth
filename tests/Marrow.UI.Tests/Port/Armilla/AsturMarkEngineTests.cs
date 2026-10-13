using Marrow.UI.Port.Armilla;

namespace Marrow.UI.Tests.Port.Armilla;

// Dextra AS-17 (DESIGN.md rev 5, 2.5 "An Astur's own mark"; the canvas's .av.ast; ED-NEW-dextra-as17-2): an Astur's mark is its node shape alone, in its tint. At rest it shows
// at 80 % scale and 72 % strength; busy, waiting or done at full size and strength; paused, stopping and stopped dim it. Each state has its own corner badge, a change of
// state is one spring from the pose on screen to the next (no fade, no cut, interruptible), and with Reduce motion each state shows its own still pose at once.
public sealed class AsturMarkEngineTests
{
    private static readonly AsturMarkState[] States = Enum.GetValues<AsturMarkState>();
    private static readonly ArmillaShape[] Shapes = Enum.GetValues<ArmillaShape>();

    private static AsturMarkEngine Mark(AsturMarkState state, double size = 30, ArmillaShape shape = ArmillaShape.Hexagon, bool dark = true, bool reduce = false) =>
        new(new AsturMarkLook { Size = size, Shape = shape, State = state, Dark = dark, Reduce = reduce });

    [Theory]
    [InlineData(AsturMarkState.Idle)]
    [InlineData(AsturMarkState.Blocked)]
    [InlineData(AsturMarkState.Paused)]
    [InlineData(AsturMarkState.Stopping)]
    [InlineData(AsturMarkState.Stopped)]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void At_rest_the_shape_shows_at_eighty_percent_scale_and_seventy_two_percent_strength(AsturMarkState state)
    {
        var mark = Mark(state);

        Assert.Equal(.8, mark.Scale);
        var dim = state is AsturMarkState.Paused or AsturMarkState.Stopping or AsturMarkState.Stopped ? AsturMarkEngine.Dim : 1;
        Assert.Equal(.72 * dim, mark.Strength, 6);
    }

    [Theory]
    [InlineData(AsturMarkState.Thinking)]
    [InlineData(AsturMarkState.Working)]
    [InlineData(AsturMarkState.Waiting)]
    [InlineData(AsturMarkState.Done)]
    [InlineData(AsturMarkState.Still)]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void Busy_waiting_done_and_a_chat_messages_mark_show_at_full_size_and_strength(AsturMarkState state)
    {
        var mark = Mark(state);

        Assert.Equal(1, mark.Scale);
        Assert.Equal(1, mark.Strength);
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void Each_state_has_its_own_corner_badge_and_working_and_thinking_take_one_only_in_a_still_pose()
    {
        var motion = States.ToDictionary(s => s, s => AsturMarkStates.BadgeOf(s, still: false));
        Assert.Equal(AsturBadge.None, motion[AsturMarkState.Idle]);
        Assert.Equal(AsturBadge.None, motion[AsturMarkState.Still]);
        Assert.Equal(AsturBadge.None, motion[AsturMarkState.Working]);
        Assert.Equal(AsturBadge.None, motion[AsturMarkState.Thinking]);
        Assert.Equal(AsturBadge.Bang, motion[AsturMarkState.Waiting]);
        Assert.Equal(AsturBadge.Check, motion[AsturMarkState.Done]);
        Assert.Equal(AsturBadge.Minus, motion[AsturMarkState.Blocked]);
        Assert.Equal(AsturBadge.Bars, motion[AsturMarkState.Paused]);
        Assert.Equal(AsturBadge.Hollow, motion[AsturMarkState.Stopping]);
        Assert.Equal(AsturBadge.Solid, motion[AsturMarkState.Stopped]);
        Assert.Equal(AsturBadge.Dot, AsturMarkStates.BadgeOf(AsturMarkState.Working, still: true));
        Assert.Equal(AsturBadge.Dots, AsturMarkStates.BadgeOf(AsturMarkState.Thinking, still: true));
        Assert.Equal(AsturBadge.None, AsturMarkStates.BadgeOf(AsturMarkState.Idle, still: true));
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void An_Astur_presence_maps_onto_a_mark_state_of_its_own()
    {
        Assert.Equal(
            [AsturMarkState.Idle, AsturMarkState.Thinking, AsturMarkState.Working, AsturMarkState.Waiting, AsturMarkState.Blocked, AsturMarkState.Paused, AsturMarkState.Stopped],
            Enum.GetValues<Marrow.Core.Port.Assistant.AsturPresence>().Select(AsturMarkStates.For));
        Assert.Equal(ArmillaShape.Triangle, AsturMarkStates.ShapeOf(Marrow.Core.Port.Assistant.AsturShape.RoundedTriangle));
        Assert.Equal(ArmillaShape.Squircle, AsturMarkStates.ShapeOf(null));
        Assert.Equal(Enum.GetValues<Marrow.Core.Port.Assistant.AsturShape>().Select(s => (int)s), Enum.GetValues<Marrow.Core.Port.Assistant.AsturShape>().Select(s => (int)AsturMarkStates.ShapeOf(s)));
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void A_change_of_state_is_one_spring_from_the_pose_on_screen_with_no_jump_and_hardly_any_overshoot()
    {
        var look = new AsturMarkLook { Size = 30, Shape = ArmillaShape.Star, State = AsturMarkState.Idle };
        var mark = new AsturMarkEngine(look);
        mark.Set(look with { State = AsturMarkState.Working });

        var scales = new List<double> { mark.Scale };
        var strengths = new List<double> { mark.Strength };
        var more = true;
        for (var i = 0; i < 120 && more; i++)
        {
            more = mark.Tick(AsturDrive.Frame);
            scales.Add(mark.Scale);
            strengths.Add(mark.Strength);
        }

        Assert.Equal(.8, scales[0]);
        Assert.False(more);                                                                    // it settles and leaves the clock
        Assert.Equal(1, mark.Scale);
        Assert.Equal(1, mark.Strength);
        Assert.True(scales.Zip(scales.Skip(1), (a, b) => Math.Abs(b - a)).Max() < .045, "the scale jumped");
        Assert.True(strengths.Zip(strengths.Skip(1), (a, b) => Math.Abs(b - a)).Max() < .06, "the strength jumped");
        Assert.True(scales.Max() < 1.03 && strengths.Max() < 1.03, "a spring of damping 0.8 overshoots by about one percent");
        Assert.True(scales.All(s => s >= .8 - 1e-9));
        Assert.InRange(scales.FindIndex(s => s > .99) / 60.0, .2, .6);                         // about half a second
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void A_change_that_comes_while_the_mark_moves_carries_on_from_where_it_is_without_a_snap()
    {
        var look = new AsturMarkLook { Size = 30, Shape = ArmillaShape.Clover, State = AsturMarkState.Idle };
        var mark = new AsturMarkEngine(look);
        mark.Set(look with { State = AsturMarkState.Working });
        for (var i = 0; i < 8; i++) mark.Tick(AsturDrive.Frame);
        var mid = mark.Scale;
        Assert.InRange(mid, .81, .99);

        mark.Set(look with { State = AsturMarkState.Idle });
        Assert.Equal(mid, mark.Scale);                                                         // nothing moved by being asked to turn round
        mark.Tick(AsturDrive.Frame);
        Assert.InRange(Math.Abs(mark.Scale - mid), 0, .04);
        for (var i = 0; i < 200; i++) mark.Tick(AsturDrive.Frame);
        Assert.Equal(.8, mark.Scale);
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void A_badge_pops_in_from_nothing_pops_again_when_it_replaces_another_and_shrinks_away_when_it_goes()
    {
        var look = new AsturMarkLook { Size = 30, Shape = ArmillaShape.Diamond, State = AsturMarkState.Idle };
        var mark = new AsturMarkEngine(look);
        Assert.Equal(0, mark.Pop);

        mark.Set(look with { State = AsturMarkState.Done });
        Assert.Equal(AsturBadge.Check, mark.Badge);
        Assert.Equal(0, mark.Pop);
        var peak = 0.0;
        for (var i = 0; i < 90; i++) { mark.Tick(AsturDrive.Frame); peak = Math.Max(peak, mark.Pop); }
        Assert.Equal(1, mark.Pop);
        Assert.InRange(peak, 1, 1.08);                                                         // a small overshoot: the pop

        mark.Set(look with { State = AsturMarkState.Blocked });
        Assert.Equal(AsturBadge.Minus, mark.Badge);
        Assert.Equal(.45, mark.Pop);                                                           // a changed badge pops again from 45 %
        for (var i = 0; i < 90; i++) mark.Tick(AsturDrive.Frame);

        mark.Set(look with { State = AsturMarkState.Idle });
        Assert.Equal(AsturBadge.Minus, mark.Badge);                                            // the old one stays while it shrinks
        for (var i = 0; i < 90; i++) mark.Tick(AsturDrive.Frame);
        Assert.Equal(AsturBadge.None, mark.Badge);
        Assert.Equal(0, mark.Pop);
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void With_reduce_motion_a_change_lands_on_the_new_still_pose_at_once_and_asks_for_no_frame()
    {
        foreach (var from in States)
        foreach (var to in States)
        {
            if (from == to) continue;
            var look = new AsturMarkLook { Size = 30, Shape = ArmillaShape.Hexagon, State = from, Reduce = true };
            var mark = new AsturMarkEngine(look);
            mark.Set(look with { State = to });
            Assert.True(mark.Settled, $"{from} to {to}: a still pose is settled");
            Assert.False(mark.Tick(AsturDrive.Frame), $"{from} to {to}: a reduced mark kept asking for frames");
            var drawn = new RecordingAsturCanvas();
            mark.Draw(drawn);
            Assert.Equal(AsturDrive.Still(30, ArmillaShape.Hexagon, to), drawn.Log);
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(30)]
    [InlineData(44)]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void No_two_states_share_a_still_pose_for_any_shape_in_either_theme(double size)
    {
        foreach (var shape in Shapes)
        foreach (var dark in new[] { true, false })
        {
            var stills = States.ToDictionary(s => s, s => AsturDrive.Still(size, shape, s, dark));
            foreach (var a in States)
            foreach (var b in States)
                if (a < b) Assert.False(stills[a] == stills[b], $"{size} px {shape} dark={dark}: {a} and {b} show the same still pose");
        }
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void A_mark_that_is_not_on_screen_takes_its_new_pose_at_once_and_draws_no_spring()
    {
        var look = new AsturMarkLook { Size = 30, Shape = ArmillaShape.Star, State = AsturMarkState.Idle };
        var mark = new AsturMarkEngine(look) { Visible = false };
        mark.Set(look with { State = AsturMarkState.Done });

        Assert.Equal(1, mark.Scale);
        Assert.Equal(1, mark.Pop);
        Assert.True(mark.Settled);
        Assert.False(mark.Tick(AsturDrive.Frame));
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void The_shape_is_drawn_into_one_layer_so_its_strength_is_one_group_opacity_and_the_badge_ring_is_cut_from_that_layer_only()
    {
        var mark = Mark(AsturMarkState.Waiting, shape: ArmillaShape.Triangle);
        var canvas = new RecordingAsturCanvas();
        mark.Draw(canvas);

        var lines = canvas.Log.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var begin = Array.FindIndex(lines, l => l.StartsWith("layer ", StringComparison.Ordinal));
        var cut = Array.FindIndex(lines, l => l.StartsWith("cut ", StringComparison.Ordinal));
        var end = Array.FindIndex(lines, l => l == "endlayer");
        Assert.True(begin == 0 && cut > begin && end > cut, canvas.Log);
        Assert.StartsWith("shape Triangle 0 0", lines[1], StringComparison.Ordinal);
        Assert.EndsWith(" 1", lines[1], StringComparison.Ordinal);                             // the shape itself is opaque; the layer carries the strength
        Assert.StartsWith("layer 1 15", lines[0], StringComparison.Ordinal);
        Assert.True(lines.Skip(end + 1).Any(l => l.StartsWith("circle ", StringComparison.Ordinal)), "the badge disc is drawn over the layer");
        Assert.Equal(1, canvas.Layers);
        Assert.Equal(1, canvas.Cuts);
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void A_mark_without_a_badge_cuts_nothing_and_a_dim_one_composites_at_its_dimmed_strength()
    {
        var plain = new RecordingAsturCanvas();
        Mark(AsturMarkState.Working).Draw(plain);
        Assert.Equal(0, plain.Cuts);

        var dim = new RecordingAsturCanvas();
        Mark(AsturMarkState.Stopped).Draw(dim);
        Assert.StartsWith("layer 0.4 15", dim.Log, StringComparison.Ordinal);                  // .72 x .55 = .396
        Assert.Equal(1, dim.Cuts);
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void The_tints_are_the_six_of_the_table_in_both_themes_and_each_shape_has_its_own()
    {
        var drawn = new Dictionary<(ArmillaShape, bool), string>();
        foreach (var shape in Shapes)
        foreach (var dark in new[] { true, false })
        {
            var canvas = new RecordingAsturCanvas();
            Mark(AsturMarkState.Still, shape: shape, dark: dark).Draw(canvas);
            drawn[(shape, dark)] = canvas.Log.Split('\n').First(l => l.StartsWith("shape ", StringComparison.Ordinal));
        }
        string[] dark6 = ["143,216,184", "242,169,158", "169,193,250", "235,203,130", "201,176,245", "134,211,227"];
        string[] light6 = ["46,139,102", "192,87,74", "74,111,208", "156,116,20", "126,91,196", "42,143,166"];
        for (var i = 0; i < Shapes.Length; i++)
        {
            Assert.Contains(dark6[i], drawn[(Shapes[i], true)], StringComparison.Ordinal);
            Assert.Contains(light6[i], drawn[(Shapes[i], false)], StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void Every_shape_state_theme_and_size_stays_inside_the_slot_including_the_overshoot_of_every_move()
    {
        var worst = 0.0;
        var where = "";
        foreach (var size in new[] { 16.0, 18, 20, 24, 30, 36, 44, 48 })
        foreach (var shape in Shapes)
        foreach (var dark in new[] { true, false })
        foreach (var from in States)
        foreach (var to in States)
        {
            var look = new AsturMarkLook { Size = size, Shape = shape, State = from, Dark = dark };
            var mark = new AsturMarkEngine(look);
            var extent = AsturDrive.Hold(mark, .1);
            mark.Set(look with { State = to });
            extent = Math.Max(extent, AsturDrive.Hold(mark, 1.2));
            if (extent > worst) { worst = extent; where = $"{size} px {shape} dark={dark} {from} to {to}"; }
        }
        Assert.True(worst < 1.0, $"worst extent {worst:F3} of the half-box at {where}");
        Assert.True(worst > .5, $"the extent was not measured ({worst:F3})");
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void The_widest_shape_at_full_size_reaches_about_nine_tenths_of_the_half_box_so_it_is_never_clipped()
    {
        var mark = Mark(AsturMarkState.Still, shape: ArmillaShape.Triangle);
        mark.Draw(NullAsturCanvas.Instance);

        Assert.InRange(mark.MaxExtent, .92, .95);                                              // 8.9/12 x 1.26
    }
}

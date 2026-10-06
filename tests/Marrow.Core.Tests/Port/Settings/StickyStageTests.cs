using System.Text.Json;
using Marrow.Core.Port.Settings;

namespace Marrow.Core.Tests.Port.Settings;

// customization-SPEC CZ-3.5 (pin, compaction, hairline, focus scroll) and CZ-T60 to T62, T66, T67: the arithmetic of the sticky stage. The traces and constants are from the locked design (tests/fixtures/customization/sticky.json,
// emitted by node from the unmodified cz.js stageTick() and gen.py), not from this port.
public sealed class StickyStageTests
{
    private const double Rest = 372, StageRest = 212, Frame = 1.0 / 60;

    private static JsonDocument Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!.FullName, "tests/fixtures/customization/sticky.json")));
    }

    private static JsonElement Trace(JsonDocument fixture, string name) => fixture.RootElement.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("name").GetString() == name);

    // The port's compaction over a trace of offsets, one frame (dt seconds) each: a scroll event, then the frame that follows it.
    private static List<Compaction> Replay(JsonElement trace, bool still)
    {
        var dt = trace.GetProperty("dt").GetDouble();
        var state = new Compaction(StickyStage.Target(trace.GetProperty("frames")[0].GetProperty("top").GetDouble()), false);
        var result = new List<Compaction>();
        var first = true;
        foreach (var frame in trace.GetProperty("frames").EnumerateArray())
        {
            var top = frame.GetProperty("top").GetDouble();
            if (!first) state = StickyStage.Scrolled(state, top, still);
            first = false;
            state = StickyStage.Frame(state, top, dt, still);
            result.Add(state);
        }
        return result;
    }

    [Fact]
    [Trait("Feature", "CZ-6")]
    [Trait("Spec", "CZ-T62")]
    [Trait("Decision", "ED-251")]
    public void The_constants_are_the_designs()
    {
        using var fixture = Fixture();
        var c = fixture.RootElement.GetProperty("constants");
        Assert.Equal(StickyStage.MaxCompaction, c.GetProperty("maxCompaction").GetDouble());
        Assert.Equal(StickyStage.MaxCompaction, c.GetProperty("colkDivisor").GetDouble());
        Assert.Equal(StickyStage.ExactRange, c.GetProperty("exactRange").GetDouble());
        Assert.Equal(StickyStage.EaseSeconds, c.GetProperty("tau").GetDouble());
        Assert.Equal(StickyStage.ChatFadeRate, c.GetProperty("chatFade").GetDouble());
        Assert.Equal(StickyStage.ChatLift, c.GetProperty("chatLift").GetDouble());
        Assert.Equal(StickyStage.HairlineOffset, c.GetProperty("hairlineOffset").GetDouble());
        Assert.Equal(StickyStage.HairlineSeconds, c.GetProperty("hairlineSeconds").GetDouble());
        Assert.Equal(StageRest, c.GetProperty("stageRest").GetDouble());
        Assert.Equal(8, StickyStage.FocusGap);
        Assert.Equal(0.3, StickyStage.FocusSeconds);
    }

    [Fact]
    [Trait("Feature", "CZ-6")]
    [Trait("Spec", "CZ-T60")]
    [Trait("Decision", "ED-251")]
    public void The_pin_follows_the_offset_to_the_point_where_the_block_lets_go()
    {
        // CZ-3.5.1: pinTop = clamp(y, 0, H_c - h_pin), for every offset from the top to the container's height and beyond, at rest and fully compacted
        foreach (var compaction in new[] { 0.0, 46, 92 })
        {
            var container = 1200.0;
            var block = StickyStage.BlockHeight(Rest, compaction);
            for (var y = -20.0; y <= container + 50; y += 7)
                Assert.Equal(Math.Min(Math.Max(y, 0), container - block), StickyStage.PinTop(y, container, block));
            Assert.Equal(0, StickyStage.PinTop(0, container, block));
            Assert.Equal(container - block, StickyStage.PinTop(container, container, block));
            // up to the release point the block is at the viewport's top (its top is the offset); after it, it is carried off with the page
            Assert.Equal(container - block, StickyStage.PinTop(container - block, container, block));
            Assert.Equal(container - block, StickyStage.PinTop(container - block + 1, container, block));
        }
        // a container shorter than the block cannot move it (nothing to scroll under)
        Assert.Equal(0, StickyStage.PinTop(300, 200, 280));
        Assert.Equal(280, StickyStage.BlockHeight(Rest, 92));
        Assert.Equal(372, StickyStage.BlockHeight(Rest, 0));
    }

    [Theory]
    [InlineData("wheel down then up")]
    [InlineData("step of 72")]
    [Trait("Feature", "CZ-6")]
    [Trait("Spec", "CZ-T61")]
    [Trait("Decision", "ED-251")]
    public void Small_steps_are_followed_exactly_as_the_designs_stage_follows_them(string name)
    {
        using var fixture = Fixture();
        var trace = Trace(fixture, name);
        var expected = trace.GetProperty("frames").EnumerateArray().Select(f => f.GetProperty("col").GetDouble()).ToArray();
        var actual = Replay(trace, still: false);
        Assert.Equal(expected, actual.Select(s => s.Value));
        Assert.All(actual, s => Assert.False(s.Easing));
    }

    [Fact]
    [Trait("Feature", "CZ-6")]
    [Trait("Spec", "CZ-T61")]
    [Trait("Decision", "ED-251")]
    public void Reduce_motion_takes_every_target_at_once()
    {
        using var fixture = Fixture();
        var trace = Trace(fixture, "reduce motion");
        Assert.Equal(trace.GetProperty("frames").EnumerateArray().Select(f => f.GetProperty("col").GetDouble()), Replay(trace, still: true).Select(s => s.Value));
        // and a jump taken while it was easing is finished at once when reduce motion is switched on
        var easing = StickyStage.Scrolled(new Compaction(0, false), 300, false);
        Assert.True(easing.Easing);
        Assert.Equal(new Compaction(92, false), StickyStage.Frame(easing, 300, Frame, still: true));
    }

    [Theory]
    [InlineData("jump from rest at 60 Hz", 1 / 60.0)]
    [InlineData("jump from rest at 120 Hz", 1 / 120.0)]
    [Trait("Feature", "CZ-6")]
    [Trait("Spec", "CZ-T61")]
    [Trait("Decision", "ED-251")]
    public void A_jump_eases_with_the_designs_time_constant_and_goes_on_to_its_end(string name, double dt)
    {
        using var fixture = Fixture();
        var trace = Trace(fixture, name);
        var expected = trace.GetProperty("frames").EnumerateArray().Select(f => f.GetProperty("col").GetDouble()).ToArray();
        var actual = Replay(trace, still: false);
        // the design's first eased frame is the port's (the same exponential from rest)
        var firstEased = Array.FindIndex(expected, v => v > 0);
        Assert.Equal(expected[firstEased], actual[firstEased].Value, 9);
        Assert.Equal(92 * (1 - Math.Exp(-dt / 0.06)), actual[firstEased].Value, 9);
        // the design then tests `|target - col| <= 72` again and snaps the rest (a cut of up to 72 points): the port goes on easing (ENGINEERING DECISION, OD-0076)
        var state = actual[firstEased];
        var steps = new List<double> { state.Value };
        for (var i = 0; i < 200 && state.Easing; i++)
        {
            var before = state.Value;
            state = StickyStage.Scrolled(state, 300, false);
            Assert.True(state.Easing, "a scroll during an ease does not cut it");
            Assert.Equal(before, state.Value);
            state = StickyStage.Frame(state, 300, dt, false);
            var next = before + (92 - before) * (1 - Math.Exp(-dt / 0.06));
            if (state.Easing) Assert.Equal(next, state.Value, 9);
            else Assert.InRange(92 - next, -1e-9, StickyStage.SettleDistance); // the last frame ends within a few hundredths of the target and is taken to it
            steps.Add(state.Value);
        }
        Assert.False(state.Easing);
        Assert.Equal(92, state.Value);
        // never a cut: the biggest frame is the first (the exponential only slows), and nothing overshoots
        var jumps = steps.Zip(steps.Skip(1), (a, b) => b - a).ToArray();
        Assert.True(jumps.Max() <= steps[0] + 1e-9, $"the largest frame step is {jumps.Max()}, the first {steps[0]}");
        Assert.All(jumps, j => Assert.True(j >= 0));
        Assert.All(steps, v => Assert.InRange(v, 0, 92));
        // settled within a quarter second of 60 Hz frames at 0.06 s: 92 * exp(-t / 0.06) < 0.05 at t = 0.45 s
        Assert.InRange(steps.Count * dt, 0.3, 0.6);
    }

    [Fact]
    [Trait("Feature", "CZ-6")]
    [Trait("Spec", "CZ-T61")]
    [Trait("Decision", "ED-251")]
    public void A_jump_back_to_the_top_eases_down_and_a_far_step_during_a_wheel_scroll_is_a_jump()
    {
        var state = new Compaction(92, false);
        state = StickyStage.Scrolled(state, 0, false);
        Assert.Equal(new Compaction(92, true), state);
        var previous = state.Value;
        var frames = 0;
        while (state.Easing && frames++ < 200)
        {
            state = StickyStage.Frame(state, 0, Frame, false);
            Assert.True(state.Value <= previous);
            Assert.True(previous - state.Value <= 92 * (1 - Math.Exp(-Frame / 0.06)) + 1e-9);
            previous = state.Value;
        }
        Assert.Equal(new Compaction(0, false), state);
        // 73 away from where it is, a step is eased; 72 away it is exact
        Assert.True(StickyStage.Scrolled(new Compaction(0, false), 73, false).Easing);
        Assert.False(StickyStage.Scrolled(new Compaction(0, false), 72, false).Easing);
        // a frame with nothing easing changes nothing
        Assert.Equal(new Compaction(40, false), StickyStage.Frame(new Compaction(40, false), 90, Frame, false));
    }

    [Fact]
    [Trait("Feature", "CZ-6")]
    [Trait("Spec", "CZ-T62")]
    [Trait("Decision", "ED-251")]
    public void The_stage_height_and_the_text_opacity_and_lift_at_k_0_a_quarter_a_half_and_1()
    {
        using var fixture = Fixture();
        var c = fixture.RootElement.GetProperty("constants");
        var fade = c.GetProperty("chatFade").GetDouble();
        var lift = c.GetProperty("chatLift").GetDouble();
        var rows = new (double K, double Height, double Opacity, double Offset)[]
        {
            (0, 212, 1, 0), (0.25, 189, 0.55, -3.5), (0.5, 166, 0.1, -7), (1, 120, 0, -14),
        };
        foreach (var row in rows)
        {
            var col = row.K * 92;
            Assert.Equal(row.Height, StickyStage.StageHeight(StageRest, col), 9);
            Assert.Equal(row.Opacity, StickyStage.ChatOpacity(col), 9);
            Assert.Equal(row.Offset, StickyStage.ChatOffset(col), 9);
            // and the designs's own expressions, `clamp(0, 1 - colk * 1.8, 1)` and `colk * -14px`, from the css
            Assert.Equal(Math.Clamp(1 - row.K * fade, 0, 1), StickyStage.ChatOpacity(col), 9);
            Assert.Equal(row.K * -lift, StickyStage.ChatOffset(col), 9);
            Assert.Equal(Rest - col, StickyStage.BlockHeight(Rest, col), 9);
        }
        // the text is gone before the stage is fully folded: 1 - 1.8 k reaches 0 at k = 0.5555..
        Assert.Equal(0, StickyStage.ChatOpacity(92 * 0.56));
        Assert.True(StickyStage.ChatOpacity(92 * 0.55) > 0);
        // beyond the ends nothing runs on
        Assert.Equal(1, StickyStage.ChatOpacity(-5));
        Assert.Equal(0, StickyStage.Target(-5));
        Assert.Equal(92, StickyStage.Target(10000));
    }

    [Fact]
    [Trait("Feature", "CZ-6")]
    [Trait("Spec", "CZ-T66")]
    [Trait("Decision", "ED-251")]
    public void The_hairline_shows_past_4_points()
    {
        Assert.False(StickyStage.HairlineShown(0));
        Assert.False(StickyStage.HairlineShown(4));
        Assert.True(StickyStage.HairlineShown(4.01));
        Assert.True(StickyStage.HairlineShown(900));
    }

    [Fact]
    [Trait("Feature", "CZ-6")]
    [Trait("Spec", "CZ-T67")]
    [Trait("Decision", "ED-251")]
    public void A_focused_control_is_cleared_to_8_below_the_block_computed_with_the_compaction_the_new_offset_makes()
    {
        // a control 900 down: the offset that puts its top 8 below the block's bottom, the block being 372 - 92 = 280 high there
        var offset = StickyStage.FocusOffset(900, Rest, 5000);
        Assert.Equal(900 - 280 - 8, offset);
        Assert.Equal(900 - offset, StickyStage.BlockHeight(Rest, StickyStage.Target(offset)) + StickyStage.FocusGap);
        // 500 down: 212, still past 92, so the same rule
        Assert.Equal(212, StickyStage.FocusOffset(500, Rest, 5000));
        // a control at the Style cards (about 402 down) needs the stage compacted: offset 114 is past the 92 the stage folds in
        Assert.Equal(402 - 288, StickyStage.FocusOffset(402, Rest, 5000));
        // never past what the page can scroll, never negative; a control no offset can clear takes the full compaction
        Assert.Equal(300, StickyStage.FocusOffset(900, Rest, 300));
        Assert.Equal(92, StickyStage.FocusOffset(300, Rest, 5000));
        Assert.Equal(0, StickyStage.FocusOffset(900, Rest, -10));
    }

    [Fact]
    [Trait("Feature", "CZ-6")]
    [Trait("Spec", "CZ-T67")]
    [Trait("Decision", "ED-251")]
    public void Focus_scrolls_only_for_a_control_above_the_clear_line_or_not_wholly_in_the_viewport()
    {
        const double blockBottom = 280, viewport = 600;
        Assert.True(StickyStage.FocusNeedsScroll(250, 290, blockBottom, viewport)); // under the block
        Assert.True(StickyStage.FocusNeedsScroll(284, 320, blockBottom, viewport)); // not 8 clear of it
        Assert.False(StickyStage.FocusNeedsScroll(288, 320, blockBottom, viewport)); // exactly 8 below (the result of a focus scroll): stays
        Assert.False(StickyStage.FocusNeedsScroll(287.6, 320, blockBottom, viewport)); // within a layout rounding of it
        Assert.False(StickyStage.FocusNeedsScroll(400, 440, blockBottom, viewport));
        Assert.True(StickyStage.FocusNeedsScroll(700, 740, blockBottom, viewport)); // below the viewport
        Assert.True(StickyStage.FocusNeedsScroll(580, 620, blockBottom, viewport)); // cut by the bottom edge
        Assert.False(StickyStage.FocusNeedsScroll(560, 600, blockBottom, viewport));
    }
}

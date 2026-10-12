using Marrow.UI.Port.Armilla;

namespace Marrow.UI.Tests.Port.Armilla;

// AS-8 "done when": Done, Stopping and Stopped glyphs spring in once, only when the state begins; resting marks draw at 30 fps
// and settled row marks stop drawing; every thinking mark scans in step on the shared clock (2.5 "Render budget").
public sealed class ArmillaMotionTests
{
    [Theory]
    [InlineData(ArmillaGlyph.Done)]
    [InlineData(ArmillaGlyph.Stopping)]
    [InlineData(ArmillaGlyph.Stopped)]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void A_glyph_springs_in_from_forty_five_percent_once_when_its_state_begins(ArmillaGlyph glyph)
    {
        var look = new ArmillaLook { Size = 20, State = ArmillaState.Working, Rest = true };
        var mark = new ArmillaEngine(look);
        var clock = 0.0;
        ArmillaDrive.Hold(mark, ref clock, .5);
        mark.Set(look = look with { State = ArmillaState.Paused, Glyph = glyph });
        Assert.Equal(.45, mark.GlyphScale, 6);
        var peak = 0.0;
        for (var i = 0; i < 90; i++) { clock += ArmillaDrive.Frame; mark.Tick(ArmillaDrive.Frame, clock); peak = Math.Max(peak, mark.GlyphScale); }
        Assert.Equal(1, mark.GlyphScale, 2);
        Assert.True(peak < 1.2, $"the glyph overshot to {peak:F3}");
        // a later change that keeps the glyph (a theme flip, a new size) never springs it again
        mark.Set(look with { Dark = !look.Dark });
        Assert.Equal(1, mark.GlyphScale, 2);
        mark.Set(look with { Dark = !look.Dark, Size = 24 });
        Assert.Equal(1, mark.GlyphScale, 2);
    }

    [Fact]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void A_mark_that_appears_with_its_glyph_or_changes_off_screen_shows_it_at_full_size()
    {
        var mark = new ArmillaEngine(new ArmillaLook { Size = 20, State = ArmillaState.Paused, Glyph = ArmillaGlyph.Done, Rest = true });
        Assert.Equal(1, mark.GlyphScale);
        var hidden = new ArmillaEngine(new ArmillaLook { Size = 20, State = ArmillaState.Working, Rest = true }) { Visible = false };
        hidden.Set(hidden.Look with { State = ArmillaState.Paused, Glyph = ArmillaGlyph.Stopped });
        Assert.Equal(1, hidden.GlyphScale);
    }

    [Theory]
    [InlineData(ArmillaState.Idle, 20)]
    [InlineData(ArmillaState.Idle, 24)]
    [InlineData(ArmillaState.Paused, 20)]
    [InlineData(ArmillaState.Offline, 20)]
    [InlineData(ArmillaState.Working, 20)]
    [InlineData(ArmillaState.NeedsYou, 24)]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void A_settled_row_mark_stops_asking_for_frames(ArmillaState state, double size)
    {
        var look = new ArmillaLook { Size = size, State = ArmillaState.Thinking, Rest = true };
        var mark = new ArmillaEngine(look);
        var clock = 0.0;
        ArmillaDrive.Hold(mark, ref clock, .5);
        mark.Set(look with { State = state });
        var settledAfter = -1.0;
        for (var i = 0; i < 60 * 8 && settledAfter < 0; i++)
        {
            clock += ArmillaDrive.Frame;
            if (!mark.Tick(ArmillaDrive.Frame, clock)) settledAfter = i * ArmillaDrive.Frame;
        }
        Assert.True(settledAfter >= 0, $"a {size} px {state} row never settled");
        Assert.True(mark.Settled());
    }

    [Theory]
    [InlineData(ArmillaState.Thinking)]
    [InlineData(ArmillaState.Listening)]
    [InlineData(ArmillaState.Speaking)]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void A_thinking_or_voice_mark_never_settles(ArmillaState state)
    {
        var mark = new ArmillaEngine(new ArmillaLook { Size = 20, State = state, Rest = true });
        var clock = 0.0;
        for (var i = 0; i < 600; i++) { clock += ArmillaDrive.Frame; Assert.True(mark.Tick(ArmillaDrive.Frame, clock)); }
    }

    [Theory]
    [InlineData(ArmillaState.Idle)]
    [InlineData(ArmillaState.Paused)]
    [InlineData(ArmillaState.Offline)]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void A_mark_resting_one_point_two_seconds_steps_at_thirty_frames_a_second(ArmillaState state)
    {
        var mark = new ArmillaEngine(new ArmillaLook { Size = 176, State = state });
        var stepped = 0;
        for (var i = 0; i < 71; i++) if (mark.RestStep(ArmillaDrive.Frame) > 0) stepped++;
        Assert.Equal(71, stepped);
        stepped = 0;
        var time = 0.0;
        for (var i = 0; i < 120; i++)
        {
            var d = mark.RestStep(ArmillaDrive.Frame);
            if (d > 0) { stepped++; time += d; }
        }
        Assert.InRange(stepped, 59, 61);
        Assert.Equal(2, time, 1);
    }

    [Theory]
    [InlineData(ArmillaState.Thinking)]
    [InlineData(ArmillaState.Working)]
    [InlineData(ArmillaState.NeedsYou)]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void A_mark_that_is_not_resting_or_has_reduce_motion_steps_every_frame(ArmillaState state)
    {
        var mark = new ArmillaEngine(new ArmillaLook { Size = 176, State = state });
        for (var i = 0; i < 240; i++) Assert.Equal(ArmillaDrive.Frame, mark.RestStep(ArmillaDrive.Frame));
        var reduced = new ArmillaEngine(new ArmillaLook { Size = 176, State = ArmillaState.Idle, Reduce = true });
        for (var i = 0; i < 240; i++) Assert.Equal(ArmillaDrive.Frame, reduced.RestStep(ArmillaDrive.Frame));
    }

    [Fact]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-2")]
    public void Every_thinking_mark_on_the_shared_clock_scans_in_step_whenever_it_began()
    {
        var hub = new ArmillaHub();
        var first = new ArmillaEngine(new ArmillaLook { Size = 20, State = ArmillaState.Thinking, Rest = true });
        var second = new ArmillaEngine(new ArmillaLook { Size = 176, State = ArmillaState.Idle });
        var third = new ArmillaEngine(new ArmillaLook { Size = 48, State = ArmillaState.Thinking });
        var now = 10.0;
        double? lastFirst = null, lastSecond = null, lastThird = null;
        for (long tick = 1; tick <= 400; tick++)
        {
            now += ArmillaDrive.Frame;
            Step(first, tick, ref lastFirst);
            Step(second, tick, ref lastSecond);
            if (tick >= 150) Step(third, tick, ref lastThird);
            if (tick == 100) second.Set(second.Look with { State = ArmillaState.Thinking });
            if (tick > 160)
            {
                Assert.Equal(first.ScanPhase, third.ScanPhase, 9);
                if (tick > 110) Assert.Equal(first.ScanPhase, second.ScanPhase, 9);
            }
        }
        void Step(ArmillaEngine mark, long tick, ref double? last)
        {
            var t = hub.Time(tick, now);
            var dt = last is { } l ? Math.Clamp(t - l, 0, ArmillaHub.MaxStep) : 0;
            last = t;
            mark.Tick(dt, t);
        }
    }

    [Fact]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-2")]
    public void The_shared_clock_gives_every_mark_of_one_tick_the_same_time_and_caps_a_step_at_fifty_milliseconds()
    {
        var hub = new ArmillaHub();
        Assert.Equal(0, hub.Time(1, 100));
        Assert.Equal(.016, hub.Time(2, 100.016), 9);
        Assert.Equal(.016, hub.Time(2, 100.020), 9);
        Assert.Equal(.066, hub.Time(3, 160), 9);
        Assert.Equal(.066, hub.Time(4, 150), 9);
    }
}

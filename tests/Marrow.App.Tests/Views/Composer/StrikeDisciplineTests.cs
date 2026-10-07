using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeDisciplineTests
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Resolve_contacts_crossfade_drag_fast_toggle_and_reentry_allocate_zero_bytes_over_2880_frames()
    {
        var g = new StrikeGeometry(298, 34, 272, 24); var storm = new StrikeStorm(1274); var lanes = new StrikeBubbleLanes("53,9,3,1", 298, 34); var clock = new StrikeFastClock(); var contacts = new StrikeContacts(1274, lanes, clock);
        var a = StrikeInk.Of(StrikeOracleLiquid.From("Claude", "dark")); var b = StrikeInk.Of(StrikeOracleLiquid.From("OpenAI", "dark"));
        var entry = new StrikeReentry(1274, new ScriptedStrikeDraws([0.2, 0.4, 0.7, 0.8, 0.3, 0.9]), 88464);
        for (var n = 0; n < 3600; n++) { var t = n / 240d; var f = storm.Resolve(g, t + entry.Offset, t, 1, b.Amount); contacts.Process(g, storm.Plans, f, t, entry.Offset, b.Amount); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var n = 0; n < 2880; n++)
        {
            var t = 15 + n / 240d; if (n == 700) clock.SetFast(false, t); if (n == 1000) clock.SetFast(true, t); if (n == 1500) entry.Request(); entry.BeforeFrame(t);
            var u = Strike.Ss(0, 0.6, (n - 200) / 240d); var ink = StrikeInk.Mix(a, b, u);
            var ex = n is >= 1100 and < 1300 ? 272 - (n - 1100) * 0.25 : n >= 1300 ? 222 : 272; var geometry = g with { Knob = ex };
            var f = storm.Resolve(geometry, t + entry.Offset, t, clock.FastAt(t), ink.Amount); contacts.Process(geometry, storm.Plans, f, t, entry.Offset, ink.Amount); contacts.ResolveLight(geometry, f, t);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Geometry_changes_follow_the_knob_immediately_and_keep_timing_with_at_most_four_plan_builds()
    {
        var g = new StrikeGeometry(298, 34, 272, 24); var storm = new StrikeStorm(1274); var p = StrikeMix.Timing(36863, 1274); var t = p.At + 0.03;
        storm.Resolve(g, t, 1, 1, 0); var plan = storm.Plans.Get(g, p.K); var timing = plan.Timing; var first = plan.Channels[0].Points[0]; var before = storm.Plans.Builds;
        var moved = g with { Knob = 188.75 }; var frame = storm.Resolve(moved, t, 1, 1, 0); Assert.InRange(storm.Plans.Builds - before, 1, 4);
        var after = storm.Plans.Get(moved, p.K); Assert.Equal(timing, after.Timing); Assert.NotEqual(first, after.Channels[0].Points[0]); Assert.Equal(188.75, after.Channels[0].Points[0]); Assert.True(frame.Masks.Count > 0);
        for (var n = 1; n <= 3600; n++) { before = storm.Plans.Builds; storm.Resolve(moved, t + n / 60d, n / 60d, 1, 0); Assert.InRange(storm.Plans.Builds - before, 0, 1); }
    }
}

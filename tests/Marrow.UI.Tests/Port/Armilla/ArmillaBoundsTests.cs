using Marrow.UI.Port.Armilla;

namespace Marrow.UI.Tests.Port.Armilla;

// AS-8 "done when": marks never draw outside their slot. A bounds test of every Armilla pose and move at 16, 20, 24, 40, 48,
// 64, 160 and 176 px with 0, 3 and 6 Asturs, in both themes, as a live presence and as a row: the worst extent stays under
// 1.0 of the half-box (2.5 "Never clipped"; the canvas audit measured 0.976).
public sealed class ArmillaBoundsTests
{
    public static TheoryData<double> Sizes => new() { 16, 20, 24, 40, 48, 64, 160, 176 };

    private static readonly ArmillaState[] States = Enum.GetValues<ArmillaState>();

    [Theory]
    [MemberData(nameof(Sizes))]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void Every_pose_held_with_every_glyph_and_the_voice_at_full_level_stays_inside_the_slot(double size)
    {
        var worst = 0.0;
        var where = "";
        foreach (var state in States)
        foreach (var glyph in Enum.GetValues<ArmillaGlyph>())
        foreach (var asturs in new[] { 0, 3, 6 })
        foreach (var dark in new[] { true, false })
        foreach (var rest in new[] { false, true })
        {
            var mark = new ArmillaEngine(new ArmillaLook { Size = size, State = state, Glyph = glyph, Nodes = ArmillaDrive.Asturs(asturs), Dark = dark, Rest = rest });
            var clock = 0.0;
            var level = state is ArmillaState.Listening or ArmillaState.Speaking ? 1.0 : (double?)null;
            var extent = ArmillaDrive.Hold(mark, ref clock, 2.5, level);
            if (extent > worst) { worst = extent; where = $"{state} {glyph} {asturs} asturs dark={dark} rest={rest}"; }
        }
        Assert.True(worst < 1.0, $"{size} px: worst extent {worst:F3} of the half-box at {where}");
        Assert.True(worst > .5, $"{size} px: the extent was not measured ({worst:F3})");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void Every_move_between_two_poses_stays_inside_the_slot_including_the_overshoot(double size)
    {
        var worst = 0.0;
        var where = "";
        foreach (var from in States)
        foreach (var to in States)
        foreach (var asturs in new[] { 0, 3, 6 })
        {
            if (from == to) continue;
            var look = new ArmillaLook { Size = size, State = from, Nodes = ArmillaDrive.Asturs(asturs) };
            var mark = new ArmillaEngine(look);
            var clock = 0.0;
            ArmillaDrive.Hold(mark, ref clock, .6);
            mark.Set(look with { State = to, Glyph = to == ArmillaState.Paused ? ArmillaGlyph.Stopped : ArmillaGlyph.None });
            var level = to is ArmillaState.Listening or ArmillaState.Speaking ? 1.0 : (double?)null;
            var extent = ArmillaDrive.Hold(mark, ref clock, 1.4, level);
            if (extent > worst) { worst = extent; where = $"{from} to {to} with {asturs} asturs"; }
        }
        Assert.True(worst < 1.0, $"{size} px: worst extent {worst:F3} of the half-box moving {where}");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void The_needs_you_step_out_and_its_repeat_after_thirty_seconds_stay_inside_the_slot(double size)
    {
        foreach (var asturs in new[] { 0, 3, 6 })
        {
            var look = new ArmillaLook { Size = size, State = ArmillaState.Idle, Nodes = ArmillaDrive.Asturs(asturs) };
            var mark = new ArmillaEngine(look);
            var clock = 0.0;
            ArmillaDrive.Hold(mark, ref clock, .5);
            mark.Set(look with { State = ArmillaState.NeedsYou });
            var extent = ArmillaDrive.Hold(mark, ref clock, 32);
            Assert.True(extent < 1.0, $"{size} px with {asturs} asturs: needs-you reached {extent:F3} of the half-box");
        }
    }
}

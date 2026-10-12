using Marrow.UI.Port.Armilla;

namespace Marrow.UI.Tests.Port.Armilla;

// AS-8 "done when": with Reduce motion each state shows its own still pose, distinct per state, with no spring; Done, Stopping
// and Stopped keep their centre glyphs; while listening or speaking only the brightness follows the voice (2.5 "Reduce motion").
public sealed class ArmillaStillPoseTests
{
    private static readonly ArmillaState[] States = Enum.GetValues<ArmillaState>();

    private static string Still(double size, ArmillaState state, ArmillaGlyph glyph = ArmillaGlyph.None, bool dark = true, bool rest = false, int asturs = 0)
    {
        var mark = new ArmillaEngine(new ArmillaLook { Size = size, State = state, Glyph = glyph, Dark = dark, Rest = rest, Reduce = true, Nodes = ArmillaDrive.Asturs(asturs) });
        var canvas = new RecordingArmillaCanvas();
        mark.DrawStill(canvas);
        return canvas.Log;
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(24, false)]
    [InlineData(24, true)]
    [InlineData(40, false)]
    [InlineData(48, false)]
    [InlineData(64, false)]
    [InlineData(160, false)]
    [InlineData(176, false)]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void No_two_states_share_a_still_pose_in_either_theme(double size, bool rest)
    {
        foreach (var dark in new[] { true, false })
        {
            var stills = States.ToDictionary(s => s, s => Still(size, s, dark: dark, rest: rest));
            foreach (var a in States)
            foreach (var b in States)
                if (a < b) Assert.False(stills[a] == stills[b], $"{size} px dark={dark}: {a} and {b} show the same still pose");
        }
    }

    [Fact]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void A_state_change_with_reduce_motion_lands_on_the_new_pose_at_once_and_asks_for_no_more_frames()
    {
        foreach (var from in States)
        foreach (var to in States)
        {
            if (from == to || to is ArmillaState.Listening or ArmillaState.Speaking) continue;
            var look = new ArmillaLook { Size = 48, State = from, Reduce = true };
            var mark = new ArmillaEngine(look);
            mark.Tick(ArmillaDrive.Frame, ArmillaDrive.Frame);
            mark.Set(look with { State = to });
            var more = mark.Tick(ArmillaDrive.Frame, 2 * ArmillaDrive.Frame);
            var moved = new RecordingArmillaCanvas();
            mark.Draw(moved);
            Assert.False(more, $"{from} to {to}: a reduced mark kept asking for frames");
            Assert.Equal(Still(48, to), moved.Log);
        }
    }

    [Theory]
    [InlineData(ArmillaState.Paused, ArmillaGlyph.Done)]
    [InlineData(ArmillaState.Working, ArmillaGlyph.Stopping)]
    [InlineData(ArmillaState.Paused, ArmillaGlyph.Stopped)]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void Done_stopping_and_stopped_keep_their_centre_glyph_at_full_size_with_reduce_motion(ArmillaState state, ArmillaGlyph glyph)
    {
        var look = new ArmillaLook { Size = 20, State = state, Rest = true, Reduce = true };
        var mark = new ArmillaEngine(look);
        mark.Set(look with { Glyph = glyph });
        Assert.Equal(1, mark.GlyphScale);
        var canvas = new RecordingArmillaCanvas();
        mark.Tick(ArmillaDrive.Frame, ArmillaDrive.Frame);
        mark.Draw(canvas);
        Assert.Contains($"glyph {glyph}", canvas.Log, StringComparison.Ordinal);
        Assert.NotEqual(Still(20, state, rest: true), Still(20, state, glyph, rest: true));
    }

    [Theory]
    [InlineData(ArmillaState.Listening)]
    [InlineData(ArmillaState.Speaking)]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void While_listening_or_speaking_with_reduce_motion_only_the_brightness_follows_the_voice(ArmillaState state)
    {
        var mark = new ArmillaEngine(new ArmillaLook { Size = 176, State = state, Reduce = true });
        var quiet = new RecordingArmillaCanvas();
        var loud = new RecordingArmillaCanvas();
        var clock = 0.0;
        for (var i = 0; i < 30; i++) { clock += ArmillaDrive.Frame; mark.SetLevel(0); mark.Tick(ArmillaDrive.Frame, clock); }
        mark.Draw(quiet);
        for (var i = 0; i < 30; i++) { clock += ArmillaDrive.Frame; mark.SetLevel(1); Assert.True(mark.Tick(ArmillaDrive.Frame, clock)); }
        mark.Draw(loud);
        Assert.NotEqual(quiet.Log, loud.Log);
        Assert.Equal(Geometry(quiet.Log), Geometry(loud.Log));
    }

    // every call with its colour and strength dropped: what is left is where and how big each stroke is
    private static string Geometry(string log) => string.Join('\n', log.Split('\n').Select(line =>
    {
        var parts = line.Split(' ');
        return parts[0] switch
        {
            "arc" when parts.Length >= 6 => string.Join(' ', parts[..5]),
            "line" when parts.Length >= 7 => string.Join(' ', parts[..6]),
            "circle" when parts.Length >= 5 => string.Join(' ', parts[..4]),
            "haze" => parts[0],
            "shape" when parts.Length >= 6 => string.Join(' ', parts[..5]),
            "glyph" when parts.Length >= 4 => string.Join(' ', parts[..4]),
            _ => line,
        };
    }));
}

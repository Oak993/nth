// PORT-ONLY: Tactus's mark, Cuspis (OD-0146, ED-NEW-pcctl-11; DESIGN.md 4.5). The glyph drawn from its numbers is the
// owner-locked artwork, digit for digit: the 24 unit master (pointer-1-final.svg), the three hand-hinted 16 px drawings
// (pointer-1-final-16*.svg), and the canvas's paused, wind-up and pressed poses.
using Marrow.Core.Port.PcControl.Mark;

namespace Marrow.Core.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-11")]
public sealed class TactusGlyphTests
{
    // E:/Droppy/work/design/tactus-icon/pointer-1-final.svg
    private const string Master = "M7 4.652L7 19.756A0.75 0.75 0 0 0 8.181 20.369L9.93 19.139A0.5 0.5 0 0 0 10.143 18.745L10.381 10.737A0.12 0.12 0 0 1 10.614 10.7L12.882 17.029A0.022 0.022 0 0 0 12.915 17.039L12.915 17.039A0.135 0.135 0 0 1 12.967 17.017L18.642 15.893A0.75 0.75 0 0 0 19.027 14.627L8.451 4.051A0.85 0.85 0 0 0 7 4.652Z";

    // pointer-1-final-16.svg, -16-working.svg and -16-paused.svg
    private const string Ready16 = "M5 3.328L5 13.029A0.5 0.5 0 0 0 5.79 13.436L6.825 12.698A0.4 0.4 0 0 0 6.993 12.388L7.194 7.303A0.066 0.066 0 0 1 7.321 7.283L8.702 10.96A0.4 0.4 0 0 0 9.152 11.212L12.473 10.577A0.5 0.5 0 0 0 12.733 9.733L5.939 2.939A0.55 0.55 0 0 0 5 3.328Z";
    private const string Working16 = "M5 3.328L5 13.029A0.5 0.5 0 0 0 5.79 13.436L8.766 11.313A0.3 0.3 0 0 1 8.884 11.263L12.473 10.577A0.5 0.5 0 0 0 12.733 9.733L5.939 2.939A0.55 0.55 0 0 0 5 3.328Z";
    private const string Paused16 = "M5 2.328L5 12.029A0.5 0.5 0 0 0 5.79 12.436L6.825 11.698A0.4 0.4 0 0 0 6.993 11.388L7.194 6.303A0.066 0.066 0 0 1 7.321 6.283L8.702 9.96A0.4 0.4 0 0 0 9.152 10.212L12.473 9.577A0.5 0.5 0 0 0 12.733 8.733L5.939 1.939A0.55 0.55 0 0 0 5 2.328Z";

    // The canvas (pc-control-ui/index.html) pads its paths with zero-length corners so CSS can morph them; without those:
    private const string Lift = "M7.152 5.022L7.152 20.215A0.703 0.703 0 0 0 8.26 20.791L9.187 20.139A0.5 0.5 0 0 0 9.396 19.791L10.505 10.859A0.12 0.12 0 0 1 10.73 10.818L13.851 16.874A0.5 0.5 0 0 0 14.393 17.135L18.795 16.263A0.75 0.75 0 0 0 19.179 14.997L8.603 4.421A0.85 0.85 0 0 0 7.152 5.022Z";
    private const string Wind = "M7.114 4.929L7.114 20.033A0.75 0.75 0 0 0 8.296 20.646L9.776 19.605A0.5 0.5 0 0 0 9.988 19.226L10.483 10.911A0.12 0.12 0 0 1 10.714 10.873L13.143 16.869A0.5 0.5 0 0 0 13.703 17.172L18.757 16.17A0.75 0.75 0 0 0 19.141 14.904L8.565 4.328A0.85 0.85 0 0 0 7.114 4.929Z";
    private const string Press = "M6.772 4.097L6.772 19.201A0.75 0.75 0 0 0 7.953 19.814L10.623 17.936A0.24 0.24 0 0 0 10.725 17.724L10.193 9.863A0.024 0.024 0 0 1 10.24 9.857L11.759 16.731A0.24 0.24 0 0 0 12.131 16.876L12.658 16.505A0.3 0.3 0 0 1 12.773 16.456L18.414 15.338A0.75 0.75 0 0 0 18.798 14.072L8.223 3.496A0.85 0.85 0 0 0 6.772 4.097Z";

    [Fact]
    public void The_rest_pose_is_the_locked_24_unit_master()
    {
        Assert.Equal(Master, TactusGlyph.Draw(TactusPose.Rest).ToSvg());
        Assert.Equal("Cuspis", TactusGlyph.Name);
        Assert.Equal(24, TactusGlyph.MasterUnits);
    }

    [Theory]
    [InlineData(TactusSmallState.Ready, Ready16)]
    [InlineData(TactusSmallState.Working, Working16)]
    [InlineData(TactusSmallState.Paused, Paused16)]
    public void The_16_px_drawings_are_the_locked_hinted_files(TactusSmallState state, string expected)
    {
        var drawing = TactusGlyph.SmallDrawing(state);

        Assert.Equal(expected, drawing.Mark.ToSvg());
        Assert.Equal(state == TactusSmallState.Paused, drawing.KnockOut is not null);
        Assert.Equal(state == TactusSmallState.Paused, drawing.Extra is not null);
    }

    [Fact]
    public void Paused_at_16_px_cuts_the_corner_and_draws_two_bars()
    {
        var drawing = TactusGlyph.SmallDrawing(TactusSmallState.Paused);

        Assert.Equal((10d, 10d, 6d, 6d), drawing.KnockOut!.Value);
        Assert.Equal("M11 11L13 11L13 16L11 16ZM14 11L16 11L16 16L14 16Z", drawing.Extra!.ToSvg());
    }

    [Theory]
    [InlineData(TactusPose.Lift, Lift)]
    [InlineData(TactusPose.Wind, Wind)]
    [InlineData(TactusPose.Press, Press)]
    public void The_poses_are_the_canvas_paths(TactusPose pose, string expected) =>
        Assert.Equal(expected, TactusGlyph.Draw(pose).ToSvg());

    [Fact]
    public void Zero_is_the_mark_shrunk_to_its_tip()
    {
        var zero = TactusGlyph.Draw(TactusPose.Zero);

        Assert.All(zero.Segments.Where(s => s.Kind != TactusSegmentKind.Close), s =>
        {
            Assert.Equal(7, s.X, 9);
            Assert.Equal(2.6, s.Y, 9);
            Assert.Equal(0, s.Radius, 9);
        });
    }

    [Fact]
    public void Half_scale_shrinks_about_the_tip_radii_included()
    {
        var values = TactusGlyph.Pose(TactusPose.Rest);
        values[TactusGlyph.Scale] = 0.5;

        var half = TactusGlyph.Draw(values);
        var whole = TactusGlyph.Draw(TactusPose.Rest);

        Assert.Equal(whole.Segments.Count, half.Segments.Count);
        for (var i = 0; i < whole.Segments.Count; i++)
        {
            if (whole.Segments[i].Kind == TactusSegmentKind.Close) continue;
            Assert.Equal(7 + ((whole.Segments[i].X - 7) * 0.5), half.Segments[i].X, 9);
            Assert.Equal(2.6 + ((whole.Segments[i].Y - 2.6) * 0.5), half.Segments[i].Y, 9);
            Assert.Equal(whole.Segments[i].Radius * 0.5, half.Segments[i].Radius, 9);
        }
    }

    [Fact]
    public void Every_pose_is_a_fresh_copy_of_fifteen_numbers()
    {
        var pose = TactusGlyph.Pose(TactusPose.Rest);
        pose[TactusGlyph.Gap] = 99;

        Assert.Equal(TactusGlyph.Count, pose.Length);
        Assert.Equal(3, TactusGlyph.Pose(TactusPose.Rest)[TactusGlyph.Gap]);
        Assert.Throws<ArgumentException>(() => TactusGlyph.Draw(new double[3]));
    }

    [Fact]
    public void The_arrival_grows_from_the_tip_then_opens_the_cut_and_the_leaving_reverses_it()
    {
        Assert.Equal(new[] { TactusGlyph.Scale }, TactusGlyph.Enter[0].Parameters);
        Assert.Equal(1d, TactusGlyph.Enter[0].Value);
        Assert.Null(TactusGlyph.Enter[1].Value);
        Assert.Equal(170, TactusGlyph.Enter[1].DelayMs);
        Assert.Equal(0d, TactusGlyph.Leave[0].Value);
        Assert.Equal(new[] { TactusGlyph.Scale }, TactusGlyph.Leave[1].Parameters);
        Assert.Equal(190, TactusGlyph.Leave[1].DelayMs);
    }

    [Theory]
    [InlineData(0.0004, "0")]
    [InlineData(-0.0004, "0")]
    [InlineData(4.65151, "4.652")]
    [InlineData(-2.25, "-2.25")]
    [InlineData(19.75, "19.75")]
    [InlineData(7, "7")]
    public void Numbers_print_as_the_design_pages_print_them(double value, string expected) =>
        Assert.Equal(expected, TactusPath.Number(value));
}

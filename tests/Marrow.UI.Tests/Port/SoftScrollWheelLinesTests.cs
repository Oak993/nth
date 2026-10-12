using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

// ED-NEW-softscroll-2: a wheel notch scrolls Windows' "lines to scroll" setting x 33 1/3 points (SPI_GETWHEELSCROLLLINES,
// read only; the tests set it through SystemWheel.Shared, so the PC's own setting never reaches them); "one screen at a
// time" scrolls a viewport; a precision touchpad's stream keeps its own weight, 50 points per WHEEL_DELTA, whatever the
// setting, even when its first report looked like a fine wheel's.
[Collection("Headless")]
public sealed class SoftScrollWheelLinesTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [Trait("Decision", "ED-NEW-softscroll-2")]
    public void A_notch_glides_the_lines_setting_times_33_points(int lines) => headless.OnUiThread(() =>
    {
        using var setting = SystemWheel.Shared.Override(new SystemWheel(() => lines));
        using var rig = new SoftScrollRig();
        rig.Wheel(new Vector(0, -1));
        Assert.Equal(0, rig.Scroll.Offset.Y); // a glide, not a jump
        var offsets = rig.Run(1.0);
        Assert.InRange(offsets[0], 0.001, lines * 100.0 / 3 * 0.01); // from rest: under 1 % of the notch in the first 240 Hz frame
        Assert.Equal(lines * 100.0 / 3, offsets[^1], 9);
        Assert.False(SoftScroll.For(rig.Scroll).IsMoving);
        // A report of two notches is two notches' worth; one back is one notch back, from where it rested.
        rig.Wheel(new Vector(0, -2));
        rig.Run(1.0);
        Assert.Equal(3 * lines * 100.0 / 3, rig.Scroll.Offset.Y, 9);
        rig.Wheel(new Vector(0, 1));
        rig.Run(1.0);
        Assert.Equal(2 * lines * 100.0 / 3, rig.Scroll.Offset.Y, 9);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-softscroll-2")] // WHEEL_PAGESCROLL: "one screen at a time"
    public void One_screen_at_a_time_glides_a_viewport_a_notch_on_either_axis() => headless.OnUiThread(() =>
    {
        using var setting = SystemWheel.Shared.Override(new SystemWheel(() => SystemWheel.PageScroll));
        using var rig = new SoftScrollRig(
            viewer => viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            new Border { Width = 3000, Height = 3000 });
        var page = rig.Scroll.Viewport;
        Assert.True(page.Height > 100 && page.Width > 100, "no viewport: " + page);
        rig.Wheel(new Vector(0, -1));
        Assert.Equal(0, rig.Scroll.Offset.Y);
        rig.Run(1.0);
        Assert.Equal(page.Height, rig.Scroll.Offset.Y, 9);
        rig.Wheel(new Vector(-1, 0)); // a tilt wheel's notch to the right
        rig.Run(1.0);
        Assert.Equal(page.Width, rig.Scroll.Offset.X, 9);
        Assert.Equal(page.Height, rig.Scroll.Offset.Y, 9);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-softscroll-2")] // 0 lines: the wheel scrolls nothing, and the presenter does not step either
    public void No_lines_to_scroll_moves_nothing() => headless.OnUiThread(() =>
    {
        using var setting = SystemWheel.Shared.Override(new SystemWheel(() => 0));
        using var rig = new SoftScrollRig();
        rig.Wheel(new Vector(0, -1));
        rig.Run(0.5);
        Assert.Equal(0, rig.Scroll.Offset.Y);
        Assert.False(SoftScroll.For(rig.Scroll).IsMoving);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-softscroll-2")] // the touchpad keeps ED-246's path: 50 points per WHEEL_DELTA, any lines setting
    public void A_touchpad_stream_keeps_its_own_weight_whatever_the_lines_setting() => headless.OnUiThread(() =>
    {
        using var setting = SystemWheel.Shared.Override(new SystemWheel(() => 10));
        using var rig = new SoftScrollRig();
        for (var i = 0; i < 12; i++)
        {
            rig.Wheel(new Vector(0, -0.08)); // 9.6 units: 4 points
            rig.Frame(250);
            rig.Frame(250);
        }
        rig.Run(1);
        Assert.Equal(48, rig.Scroll.Offset.Y, 9);
    });

    [Theory]
    [InlineData(60)]
    [InlineData(240)]
    [Trait("Decision", "ED-NEW-softscroll-2")] // a touchpad's first report can look like a fine wheel's (24 units divide 120)
    public void A_touchpad_stream_whose_first_report_looked_like_a_wheels_takes_the_excess_back(int hz) => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        var units = new[] { 24, 17, 13, 14, 13, 9, 6 };
        var trace = rig.Play(units.Select((u, i) => (i * 0.008, new Vector(0, -u / 120.0))), hz, 1.0);
        // The whole stream at a touchpad's weight, nothing of the first report's wheel weight (100 / 120 a unit) left.
        Assert.Equal(units.Sum() * 50 / 120.0, trace[^1].Y, 9);
        var steps = trace.Zip(trace.Skip(1), (a, b) => b.Y - a.Y).ToArray();
        Assert.All(steps, s => Assert.True(s >= -1e-9, "it went back: " + s));
        Assert.False(SoftScroll.For(rig.Scroll).IsMoving);
    });
}

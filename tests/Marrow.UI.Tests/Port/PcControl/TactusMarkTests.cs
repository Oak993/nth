// PORT-ONLY: the Tactus mark control, the one swappable mark component (OD-0146, ED-NEW-pcctl-11; DESIGN.md 4.5). Its
// sizes (the pill's 32 DIP slot and the fullscreen dot around the 24 DIP master; the hinted drawing under 20 DIP), its
// states (the working arc that runs and grows in from its head, the hands-off ring, Paused's lift pose), the press and
// hold, Reduce motion, and its pixels: driven by a manual frame source and clock, never a real display.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port.PcControl.Mark;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-11")]
public sealed class TactusMarkTests(HeadlessSession headless)
{
    [Fact]
    public void It_measures_its_glyph_alone_or_the_32_DIP_slot_around_the_24_DIP_master() => headless.OnUiThread(() =>
    {
        static Size Measure(TactusMark mark)
        {
            mark.UseLayoutRounding = false;
            mark.Measure(Size.Infinity);
            return mark.DesiredSize;
        }

        Assert.Equal(new Size(24, 24), Measure(new TactusMark()));
        Assert.Equal(new Size(14, 14), Measure(new TactusMark { GlyphSize = 14 }));
        Assert.Equal(new Size(32, 32), Measure(new TactusMark { InSlot = true }));
        Assert.Equal(new Size(32, 32), Measure(new TactusMark { IsDot = true }));
        Assert.Equal(new Size(48, 48), Measure(new TactusMark { InSlot = true, GlyphSize = 36 }));
    });

    [Fact]
    public void It_is_decoration_hidden_from_assistive_technology_and_never_takes_input() => headless.OnUiThread(() =>
    {
        var mark = new TactusMark();

        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(mark));
        Assert.False(mark.IsHitTestVisible);
        Assert.False(mark.Focusable);
    });

    [Theory]
    [InlineData(12, true)]
    [InlineData(16, true)]
    [InlineData(19.5, true)]
    [InlineData(20, false)]
    [InlineData(24, false)]
    public void Under_20_DIP_it_draws_the_hinted_drawing(double size, bool small) => headless.OnUiThread(() =>
        Assert.Equal(small, new TactusMark { GlyphSize = size }.IsSmall));

    [Fact]
    public void Its_geometry_is_the_Core_outline_at_scale() => headless.OnUiThread(() =>
    {
        foreach (var path in new[] { TactusGlyph.Draw(TactusPose.Rest), TactusGlyph.Draw(TactusPose.Press), TactusGlyph.SmallDrawing(TactusSmallState.Ready).Mark })
        {
            var geometry = TactusMark.Geometry(path, 2);
            using var expected = TactusMarkImage.Path(path, 2);
            var compared = 0;
            for (var y = 0.25; y < 48; y += 0.5)
            {
                for (var x = 0.25; x < 48; x += 0.5)
                {
                    // Points within 0.05 of an edge are left out: there the two rasterisers may round either way.
                    var inside = expected.Contains((float)x, (float)y);
                    if (expected.Contains((float)x - 0.05f, (float)y) != inside || expected.Contains((float)x + 0.05f, (float)y) != inside
                        || expected.Contains((float)x, (float)y - 0.05f) != inside || expected.Contains((float)x, (float)y + 0.05f) != inside) continue;
                    compared++;
                    Assert.True(geometry.FillContains(new Point(x, y)) == inside, $"({x},{y}) inside={inside}");
                }
            }
            Assert.True(compared > 9_000, "compared " + compared);
        }
    });

    [Fact]
    public void Working_grows_the_arc_in_from_its_head_runs_it_one_turn_in_2_2_s_and_Ready_takes_it_back() => headless.OnUiThread(() =>
    {
        using var rig = new TactusMarkRig(new TactusMark { InSlot = true });
        Assert.Equal(0, rig.Frames.Pending);

        rig.Mark.State = TactusMarkState.Working;
        rig.Run(80);
        Assert.InRange(rig.Mark.ArcPresence, 0.05, 0.95);
        rig.Run(800);
        Assert.Equal(1, rig.Mark.ArcPresence);
        Assert.Equal(1, rig.Frames.Pending);

        var before = rig.Mark.ArcAngle;
        rig.Run(552);
        var turned = (rig.Mark.ArcAngle - before + 360) % 360;
        Assert.InRange(turned, 90 - 6, 90 + 6);

        rig.Mark.State = TactusMarkState.Ready;
        rig.Run(1_200);
        Assert.Equal(0, rig.Mark.ArcPresence);
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    public void The_arc_and_ring_show_only_in_the_slot_or_the_dot() => headless.OnUiThread(() =>
    {
        using var rig = new TactusMarkRig(new TactusMark { State = TactusMarkState.Working });
        rig.Run(400);

        Assert.Equal(0, rig.Mark.ArcPresence);
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    public void Hands_off_grows_the_ring_and_draws_the_fill_the_caller_gives() => headless.OnUiThread(() =>
    {
        using var rig = new TactusMarkRig(new TactusMark { InSlot = true, State = TactusMarkState.HandsOff });
        rig.Run(800);
        Assert.Equal(1, rig.Mark.RingPresence);
        Assert.Equal(0, rig.Frames.Pending);

        rig.Mark.RingFill = 0.5;
        Assert.Equal(0.5, rig.Mark.RingFill);
        rig.Mark.RingFill = 7;
        Assert.Equal(1, rig.Mark.RingFill);
        rig.Mark.RingFill = double.NaN;
        Assert.Equal(0, rig.Mark.RingFill);
    });

    [Fact]
    public void Paused_springs_the_master_into_the_lift_pose() => headless.OnUiThread(() =>
    {
        using var rig = new TactusMarkRig(new TactusMark { InSlot = true });

        rig.Mark.State = TactusMarkState.Paused;
        Assert.True(rig.Frames.Pending > 0);
        rig.Run(1_500);

        Assert.Equal(TactusPose.Lift, rig.Mark.Motion.Base);
        Assert.Equal(TactusGlyph.Draw(TactusPose.Lift).ToSvg(), rig.Mark.Motion.Path.ToSvg());
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    public void A_press_asks_for_frames_until_the_mark_is_still_again() => headless.OnUiThread(() =>
    {
        using var rig = new TactusMarkRig(new TactusMark { InSlot = true });

        Assert.True(rig.Mark.Press(contactInMs: 200));
        Assert.False(rig.Mark.Press(), "a second press inside 450 ms");
        Assert.Equal(1, rig.Frames.Pending);
        rig.Run(220);
        Assert.True(rig.Mark.Motion.Values[TactusGlyph.Gap] < 2, "down at contact");
        rig.Run(1_000);

        Assert.False(rig.Mark.Motion.Moving);
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    public void A_typing_burst_holds_the_press() => headless.OnUiThread(() =>
    {
        using var rig = new TactusMarkRig(new TactusMark());

        rig.Mark.Hold(true);
        rig.Run(1_000);
        Assert.True(rig.Mark.Motion.Held);
        Assert.Equal(1.2, rig.Mark.Motion.Values[TactusGlyph.Gap], 2);

        rig.Mark.Hold(false);
        rig.Run(1_000);
        Assert.Equal(3, rig.Mark.Motion.Values[TactusGlyph.Gap], 6);
    });

    [Fact]
    public void With_Reduce_motion_nothing_presses_the_arc_stands_still_and_states_change_at_once() => headless.OnUiThread(() =>
    {
        using var rig = new TactusMarkRig(new TactusMark { InSlot = true }, reduceMotion: true);

        Assert.False(rig.Mark.Press());
        rig.Mark.State = TactusMarkState.Working;
        Assert.Equal(1, rig.Mark.ArcPresence);
        Assert.Equal(0, rig.Mark.ArcAngle);
        Assert.Equal(0, rig.Frames.Pending);

        rig.Mark.State = TactusMarkState.Paused;
        Assert.Equal(0, rig.Mark.ArcPresence);
        Assert.Equal(TactusGlyph.Draw(TactusPose.Lift).ToSvg(), rig.Mark.Motion.Path.ToSvg());
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    public void Turning_Reduce_motion_on_mid_flight_stops_every_motion() => headless.OnUiThread(() =>
    {
        using var rig = new TactusMarkRig(new TactusMark { InSlot = true });
        rig.Mark.State = TactusMarkState.Working;
        rig.Mark.Press(contactInMs: 0);
        rig.Run(40);

        EnvironmentValues.SetAccessibilityReduceMotion(rig.Window, true);
        Dispatcher.UIThread.RunJobs();
        rig.Run(40);

        Assert.False(rig.Mark.Motion.Moving);
        Assert.Equal(1, rig.Mark.ArcPresence);
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    public void The_master_paints_its_outline_in_its_one_colour() => headless.OnUiThread(() =>
    {
        var colour = Color.FromRgb(0xEC, 0xEC, 0xEE);
        using var rig = new TactusMarkRig(new TactusMark { Color = colour, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top });
        var pixels = rig.Capture(out var width);
        using var outline = TactusMarkImage.Path(TactusGlyph.Draw(TactusPose.Rest));

        var inside = 0;
        var outside = 0;
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 24; x++)
            {
                var (r, g, b) = pixels[(y * width) + x];
                var coverage = TactusCoverage.Of(outline, x, y);
                if (coverage == 1)
                {
                    inside++;
                    Assert.True(Math.Abs(r - 0xEC) <= 2 && Math.Abs(g - 0xEC) <= 2 && Math.Abs(b - 0xEE) <= 2, $"inside ({x},{y}) is {r},{g},{b}");
                }
                else if (coverage == 0)
                {
                    outside++;
                    Assert.True(r <= 2 && g <= 2 && b <= 2, $"outside ({x},{y}) is {r},{g},{b}");
                }
                else
                {
                    // An edge pixel: the black background blended with the mark by the outline's coverage.
                    Assert.True(Math.Abs(g - (coverage * 0xEC)) <= 64, $"edge ({x},{y}) is {g} for coverage {coverage}");
                }
            }
        }
        Assert.True(inside > 40, "inside pixels: " + inside);
        Assert.True(outside > 300, "outside pixels: " + outside);
    });
}

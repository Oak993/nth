using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// web W1 (DESIGN 4.2, canvas `.spl`): the one grip every splitter draws. A 4 x 28 DIP pill (28 x 4 across a horizontal
// divider) at 34 % white on dark and the darker variant on light, 36 DIP and brighter while dragged, fading in over
// 0.18 s from a 0.6 scale, following the pointer along a divider longer than 480 DIP; a corner draws a 9 DIP L.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w1-2")]
public sealed class SplitterGripTests(HeadlessSession headless)
{
    private static Color Solid(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [Fact]
    public void The_pill_is_four_by_28_dip_across_a_vertical_divider_and_28_by_four_across_a_horizontal_one() => headless.OnUiThread(() =>
    {
        var vertical = new SplitterGrip(SplitterOrientation.Vertical);
        Assert.Equal((4.0, 28.0), vertical.PillSize);
        Assert.Equal(4, vertical.Body.Width);
        Assert.Equal(28, vertical.Body.Height);
        Assert.Equal(new CornerRadius(3), vertical.Body.CornerRadius);
        Assert.Equal(2, vertical.Ring.BorderThickness.Left);
        var horizontal = new SplitterGrip(SplitterOrientation.Horizontal);
        Assert.Equal((28.0, 4.0), horizontal.PillSize);
        var corner = new SplitterGrip(SplitterOrientation.Both, SplitterCorner.BottomRight);
        Assert.Equal(9, corner.Body.Width);
        Assert.Equal(new Thickness(0, 0, 2, 2), corner.Body.BorderThickness); // an L in the bottom-right corner
        corner.SetCorner(SplitterCorner.TopLeft);
        Assert.Equal(new Thickness(2, 2, 0, 0), corner.Body.BorderThickness);
        Assert.False(vertical.IsHitTestVisible); // the grip never takes the pointer from the splitter
        Assert.Equal(0, vertical.Presence);
    });

    [Fact]
    public void The_colours_follow_the_canvas_on_dark_and_light() => headless.OnUiThread(() =>
    {
        var grip = new SplitterGrip(SplitterOrientation.Vertical);
        var window = new Window { Content = grip, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.FromArgb(0x57, 255, 255, 255), Solid(grip.Body.Background)); // rgba(255,255,255,.34)
            Assert.Equal(Color.FromArgb(0xDB, 255, 255, 255), Solid(grip.Bright.Background)); // .86 dragged
            window.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.FromArgb(0x99, 40, 40, 46), Solid(grip.Body.Background)); // rgba(40,40,46,.6)
            Assert.Equal(Color.FromArgb(0xE6, 30, 30, 36), Solid(grip.Bright.Background)); // rgba(30,30,36,.9)
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Showing_fades_in_over_0_18_seconds_while_the_scale_springs_from_0_6_to_1() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        var grip = rig.Splitter.Grip;
        Assert.Equal(SplitterGrip.HiddenScale, grip.Scale, 6);
        grip.SetShown(true);
        var presence = new List<double>();
        for (var i = 0; i < 18; i++)
        {
            rig.Frame(10);
            presence.Add(grip.Presence);
        }
        Assert.All(presence.Zip(presence.Skip(1)), pair => Assert.True(pair.Second >= pair.First));
        Assert.InRange(presence[8], 0.3, 0.95); // midway through the fade
        Assert.Equal(1, presence[^1], 3);
        rig.Settle();
        Assert.Equal(1, grip.Scale, 3);
        Assert.Equal(1, grip.Presence, 6);
        grip.SetShown(false);
        rig.Settle();
        Assert.Equal(0, grip.Presence, 6);
        Assert.Equal(SplitterGrip.HiddenScale, grip.Scale, 3);
    });

    [Fact]
    public void Along_a_divider_longer_than_480_dip_the_pill_follows_the_pointer_held_40_dip_from_either_end() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        var grip = rig.Splitter.Grip;
        var span = rig.Splitter.Bounds.Height;
        Assert.True(span > Splitter.FollowSpan);
        var top = rig.Splitter.TranslatePoint(new Point(rig.Splitter.Bounds.Width / 2, 0), rig.Window)!.Value;
        rig.Move(top + new Vector(0, 300));
        rig.Wait(200);
        rig.Settle();
        Assert.Equal(300, grip.Along, 1);
        rig.Move(top + new Vector(0, 10)); // near the end: held 40 DIP in
        rig.Settle();
        Assert.Equal(40, grip.Along, 1);

        // A short divider keeps the pill centred.
        rig.Splitter.Height = 400;
        rig.Splitter.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        Dispatcher.UIThread.RunJobs();
        rig.Move(rig.Splitter.TranslatePoint(new Point(6, 50), rig.Window)!.Value);
        rig.Settle();
        Assert.Equal(200, grip.Along, 1);
    });
}

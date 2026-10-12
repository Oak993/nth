// PORT-ONLY: the Tactus pointer's drawing (OD-0144, ED-NEW-pcctl-21; DESIGN.md 4.5; canvas `.tpt`): the pose moves and
// scales the body about its tip and drives the ripple, the pause badge and the glow through transforms and opacity only;
// it never takes input and is hidden from assistive technology; its badge is the app's icon or letter; and everything it
// draws, the ripple's glow and the shadows included, stays inside the rest box its window is sized to.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Core.Port.PcControl.Pointer;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-21")]
public sealed class TactusPointerTests(HeadlessSession headless)
{
    [Fact]
    public void The_pose_moves_and_scales_the_body_about_its_tip_and_drives_the_ripple_badge_and_glow() => headless.OnUiThread(() =>
    {
        var pointer = new TactusPointer { Origin = new Point(100, 200) };
        var transform = pointer.Body.RenderTransform;
        pointer.Pose = new PointerPose(new PillVector(130, 250), 0.5, 0.8, 0, 0, false, 0, 0);

        var body = pointer.Body.RenderTransform!.Value;
        Assert.Equal(new Point(30, 50), new Point(0, 0).Transform(body));
        Assert.Equal(new Point(35, 50), new Point(10, 0).Transform(body));
        Assert.Equal(0.8, pointer.Body.Opacity, 9);
        Assert.False(pointer.Ripple.IsVisible);
        Assert.False(pointer.PauseBadge.IsVisible);
        Assert.False(pointer.Glow.IsVisible);

        pointer.Pose = new PointerPose(new PillVector(130, 250), 1, 1, 0.6, 0.5, true, 0.65, 0.4);
        Assert.True(pointer.Ripple.IsVisible);
        Assert.Equal(0.4, pointer.Ripple.Opacity, 9);
        Assert.Equal(0.65, ((ScaleTransform)pointer.Ripple.RenderTransform!).ScaleX, 9);
        Assert.True(pointer.PauseBadge.IsVisible);
        Assert.Equal(0.6, ((ScaleTransform)pointer.PauseBadge.RenderTransform!).ScaleX, 9);
        Assert.Equal(0.375, pointer.Glow.Opacity, 9);
        // A frame reuses its transforms: nothing is allocated and nothing is laid out again.
        Assert.Same(transform, pointer.Body.RenderTransform);
    });

    [Fact]
    public void It_never_takes_input_and_is_hidden_from_assistive_technology() => headless.OnUiThread(() =>
    {
        var pointer = new TactusPointer();

        Assert.False(pointer.IsHitTestVisible);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(pointer));
    });

    [Fact]
    public void Its_badge_is_the_app_s_letter_when_it_has_no_icon() => headless.OnUiThread(() =>
    {
        var pointer = new TactusPointer();
        Assert.Null(pointer.BadgeLetter);

        pointer.BadgeLetter = "E";

        Assert.Equal("E", pointer.BadgeLetter);
        Assert.Null(pointer.BadgeImage);
        Assert.Equal(TactusPointer.BadgeAt, new Point(Canvas.GetLeft((Control)pointer.Body.Children[2]), Canvas.GetTop((Control)pointer.Body.Children[2])));
    });

    [Fact]
    public void Everything_it_draws_stays_inside_the_rest_box_its_window_is_sized_to() => headless.OnUiThread(() =>
    {
        var tip = new PillVector(120, 120);
        var pointer = new TactusPointer { BadgeLetter = "E" };
        pointer.Pose = new PointerPose(tip, 1, 1, 1, 1, true, 1, 1);
        var window = new Window { Width = 260, Height = 260, Background = Brushes.Black, Content = pointer };
        window.Show();
        try
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = RenderedPixels.Capture(window);
            var box = PointerTrack.Extent;
            var inked = 0;
            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                {
                    if (frame.Level(x, y) <= 2) continue;
                    inked++;
                    Assert.True(x >= tip.X + box.X && x < tip.X + box.Right && y >= tip.Y + box.Y && y < tip.Y + box.Bottom, $"ink at ({x}, {y}) outside the rest box");
                }
            }
            // The silver body is drawn just inside the tip.
            Assert.True(frame.Level((int)tip.X + 3, (int)tip.Y + 9) > 150);
            Assert.True(inked > 400);
        }
        finally
        {
            window.Close();
        }
    });
}

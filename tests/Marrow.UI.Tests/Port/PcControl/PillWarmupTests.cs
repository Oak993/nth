// PORT-ONLY: the Tactus pill's first frames (OD-0145, ED-NEW-pcctl-24; DESIGN.md 10.6 "First frames"): the content is
// drawn once into a 1 x 1 bitmap, and once in the overlay for two frames at 1 % opacity, never hit-testable and hidden from
// assistive technology, then removed; with no window it is removed at once. NP-16 measures the first expand natively.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-24")]
public sealed class PillWarmupTests(HeadlessSession headless)
{
    [Fact]
    public void Shape_lays_the_content_out_and_draws_it_off_screen() => headless.OnUiThread(() =>
    {
        var content = new TextBlock { Text = "Needs your OK. Outlook" };

        PillWarmup.Shape(content, new Size(440, 48));

        Assert.Equal(new Size(440, 48), content.Bounds.Size);
        Assert.Null(content.Parent);
    });

    [Fact]
    public void Draw_shows_the_content_invisibly_for_its_frames_then_removes_it() => headless.OnUiThread(() =>
    {
        var host = new Canvas();
        var window = new Window { Width = 600, Height = 200, Content = host };
        window.Show();
        try
        {
            var frames = new ManualFrameSource();
            using var bound = FrameClock.Source.Override(frames);
            var content = new Border { Width = 440, Height = 48 };
            var done = 0;

            PillWarmup.Draw(host, content, () => done++);

            Assert.Contains(content, host.Children);
            Assert.Equal(PillWarmup.Opacity, content.Opacity, 9);
            Assert.False(content.IsHitTestVisible);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(content));
            Assert.Equal(0, done);
            // an animation-frame callback runs before its frame is drawn: drawn in Frames frames, removed at the next callback
            for (var i = 0; i < PillWarmup.Frames; i++)
            {
                frames.Advance(TimeSpan.FromMilliseconds(16));
                Assert.Contains(content, host.Children);
                Assert.Equal(0, done);
            }
            frames.Advance(TimeSpan.FromMilliseconds(16));
            Assert.Equal(1, done);
            Assert.Equal(0, frames.Pending);
            Assert.DoesNotContain(content, host.Children);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Without_a_window_it_is_removed_at_once() => headless.OnUiThread(() =>
    {
        var host = new Canvas();
        var content = new Border();
        var done = 0;

        PillWarmup.Draw(host, content, () => done++);

        Assert.Equal(1, done);
        Assert.Empty(host.Children);
    });
}

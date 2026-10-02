using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// SystemReduceMotion (PORT-ONLY, ED-084): `\.accessibilityReduceMotion` follows Windows' "Animation effects" setting on
// every open window, re-read on WM_SETTINGCHANGE. The reader is a seam here; the real one reads SPI_GETCLIENTAREAANIMATION.
[Collection("Headless")]
public sealed class SystemReduceMotionTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-084")]
    public void A_followed_window_and_everything_in_it_read_the_system_setting() => headless.OnUiThread(() =>
    {
        var reduce = true;
        var motion = new SystemReduceMotion(() => reduce);
        var leaf = new Border();
        var window = new Window { Content = new StackPanel { Children = { leaf } } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.False(EnvironmentValues.GetAccessibilityReduceMotion(leaf));
            motion.Follow(window);
            Assert.True(EnvironmentValues.GetAccessibilityReduceMotion(window));
            Assert.True(EnvironmentValues.GetAccessibilityReduceMotion(leaf)); // inherited by the whole tree
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-084")]
    public void A_settings_change_reapplies_the_setting_to_every_open_window_and_forgets_closed_ones() => headless.OnUiThread(() =>
    {
        var reads = 0;
        var reduce = false;
        var motion = new SystemReduceMotion(() => { reads++; return reduce; });
        var first = new Window();
        var second = new Window();
        var closed = new Window();
        first.Show(); second.Show(); closed.Show();
        try
        {
            motion.Follow(first);
            motion.Follow(second);
            motion.Follow(closed);
            closed.Close();
            Assert.False(EnvironmentValues.GetAccessibilityReduceMotion(first));
            reduce = true;
            reads = 0;
            motion.Refresh();
            Assert.Equal(1, reads); // read once per change, not once per window
            Assert.True(EnvironmentValues.GetAccessibilityReduceMotion(first));
            Assert.True(EnvironmentValues.GetAccessibilityReduceMotion(second));
            Assert.False(EnvironmentValues.GetAccessibilityReduceMotion(closed)); // a closed window is no longer followed
            reduce = false;
            motion.Refresh();
            Assert.False(EnvironmentValues.GetAccessibilityReduceMotion(first));
            Assert.False(EnvironmentValues.GetAccessibilityReduceMotion(second));
        }
        finally { first.Close(); second.Close(); }
    });
}

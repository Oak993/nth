using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Port;

// ED-084: WM_SETTINGCHANGE, broadcast to every top-level window, makes the app re-read Windows' "Animation effects"
// setting into every open window's `\.accessibilityReduceMotion` (SystemReduceMotion, PORT-ONLY).
[Collection("Headless")]
public sealed class AppShellSettingChangeTests(HeadlessSession headless)
{
    [Theory]
    [Trait("Decision", "ED-084")]
    [InlineData(0x001Au, true)] // WM_SETTINGCHANGE
    [InlineData(0x001Cu, false)] // WM_ACTIVATEAPP: not a settings change
    public void A_settings_change_message_reapplies_the_animation_setting_to_the_open_windows(uint message, bool reapplied) => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        var recorder = new ShellRecorder();
        var shell = new AppShell(launch.Delegate, recorder.Exit, recorder.AfterWillTerminate);
        var reduce = false;
        var motion = new SystemReduceMotion(() => reduce);
        using var scope = SystemReduceMotion.Shared.Override(motion);
        var window = new Avalonia.Controls.Window();
        window.Show();
        try
        {
            motion.Follow(window);
            Assert.False(EnvironmentValues.GetAccessibilityReduceMotion(window));
            reduce = true;
            var handled = false;
            var result = shell.WndProc(0, message, 0, 0, ref handled);
            Assert.Equal((nint)0, result);
            Assert.False(handled); // the message goes on to Windows' own handling
            Assert.Equal(reapplied, EnvironmentValues.GetAccessibilityReduceMotion(window));
        }
        finally { window.Close(); }
    });
}

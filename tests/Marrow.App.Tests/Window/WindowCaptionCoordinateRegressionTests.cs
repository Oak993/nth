using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.App.Port;

namespace Marrow.App.Tests.Window;

[Collection("Headless")]
public sealed class WindowCaptionCoordinateRegressionTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(-32000, -32000)]
    [InlineData(-100, 100)]
    [InlineData(100, -100)]
    [Trait("Decision", "ED-063")]
    public void Settings_proxy_packs_negative_screen_and_captured_client_coordinates(int x, int y)
    {
        var pack = typeof(SettingsShellInput).GetMethod("Pack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var packed = Assert.IsType<nint>(pack.Invoke(null, [x, y]));
        Assert.Equal(x, unchecked((short)(packed.ToInt64() & 0xffff)));
        Assert.Equal(y, unchecked((short)((packed.ToInt64() >> 16) & 0xffff)));
    }

    [Theory]
    [InlineData(-32000, -32000)]
    [InlineData(-100, 100)]
    [InlineData(100, -100)]
    [Trait("Decision", "ED-063")]
    public void Signed_screen_coordinates_are_safe_for_offscreen_and_left_hand_windows(int x, int y) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness(withRuntime: false);
        var manager = new WindowManager { MainContent = () => new Border() };
        manager.ShowMain();
        try
        {
            var handled = false;
            var point = unchecked((nint)((uint)(ushort)x | ((uint)(ushort)y << 16)));
            Assert.Equal((nint)0, manager.HandleNativeWindowMessage(manager.MainWindow!, 0, 0x84, 0, point, ref handled));
            Assert.False(handled);
        }
        finally { manager.MainWindow!.Hide(); }
    });
}

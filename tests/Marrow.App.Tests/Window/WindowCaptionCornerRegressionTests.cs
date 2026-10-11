using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;
using Marrow.UI.Common;

namespace Marrow.App.Tests.Window;

[Collection("Headless")]
public sealed class WindowCaptionCornerRegressionTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Theory]
    [InlineData(false, 100)]
    [InlineData(false, 125)]
    [InlineData(false, 150)]
    [InlineData(true, 100)]
    [InlineData(true, 125)]
    [InlineData(true, 150)]
    [Trait("Decision", "ED-063")]
    public void The_full_zoomed_caption_cells_hover_and_click_at_each_corner(bool settings, int percent) => WithManager((_, manager) =>
    {
        var closes = 0; manager.MainWindowClose = () => closes++;
        manager.Model.Settings.AppZoom = percent;
        manager.ShowMain();
        if (settings) manager.ShowSettings();
        var window = settings ? Settings(manager) : MainOf(manager);
        WindowChrome.PlaceTrafficLights(window, false, false); Drain(window);
        var scale = percent / 100.0 * AppZoom.BaseScale; // the zoom over the base scale (ED-NEW-textsize-2)
        foreach (var kind in new[] { CaptionButton.Kind.Minimize, CaptionButton.Kind.Maximize, CaptionButton.Kind.Close })
        {
            var button = window.GetVisualDescendants().OfType<CaptionButton>().Single(b => b.ButtonKind == kind);
            var a = button.TranslatePoint(default, window)!.Value;
            var b = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), window)!.Value;
            Assert.InRange(b.X - a.X, 46 * scale - .01, 46 * scale + .01);
            Assert.InRange(b.Y - a.Y, 40 * scale - .01, 40 * scale + .01);
            foreach (var local in new[] { new Point(1, 1), new Point(45, 1), new Point(1, 39), new Point(45, 39) })
            {
                window.Show(); window.WindowState = WindowState.Normal; Drain(window);
                var point = button.PointToScreen(local);
                var packed = unchecked((nint)((uint)(ushort)point.X | ((uint)(ushort)point.Y << 16)));
                var handled = false;
                var hit = manager.HandleNativeWindowMessage(window, 0, 0x84, 0, packed, ref handled);
                Assert.True(handled);
                var expected = kind == CaptionButton.Kind.Minimize ? 8 : kind == CaptionButton.Kind.Close ? 20 : 9;
                Assert.Equal((nint)expected, hit);
                manager.HandleNativeWindowMessage(window, 0, 0xa0, hit, packed, ref handled);
                Assert.Equal(expected != 1, button.IsHovered);
                var before = closes;
                manager.HandleNativeWindowMessage(window, 0, 0xa1, hit, packed, ref handled);
                Assert.Equal(expected != 1, button.IsPressed);
                manager.HandleNativeWindowMessage(window, 0, 0xa2, hit, packed, ref handled); Drain(window);
                Assert.False(button.IsPressed);
                if (kind == CaptionButton.Kind.Minimize) Assert.Equal(WindowState.Minimized, window.WindowState);
                else if (kind == CaptionButton.Kind.Maximize) Assert.Equal(WindowState.Maximized, window.WindowState);
                else if (settings) Assert.False(window.IsVisible);
                else Assert.Equal(before + 1, closes);
            }
        }
    }, realSettings: false);
}

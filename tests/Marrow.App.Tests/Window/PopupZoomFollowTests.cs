using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Xunit;

namespace Marrow.App.Tests.Window;

// ED-NEW-perf-motion-7: a popover open while the zoom moves (Ctrl+Plus with a menu or a popover showing) follows its window's scale on
// every frame of the motion (ED-NEW-textsize-3), its host's sizes with it, and rests on the new scale with its window; before, it stayed
// at the scale it opened with (PopupZoom's scale was fixed at the open, ED-NEW-textsize-1).
[Collection("Headless")]
public sealed class PopupZoomFollowTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    private const double At100 = AppZoom.BaseScale;
    private const double At125 = 1.25 * AppZoom.BaseScale;

    private static double DrawnSize(TextBlock text, Visual window) => text.FontSize * Assert.IsType<Matrix>(text.TransformToVisual(window)).M11;

    private static Control? Host(Popup popup) => typeof(Popup).GetProperty("Host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup) as Control;

    [Fact]
    [Trait("Decision", "ED-NEW-perfmotion-7")]
    public void A_popover_open_while_the_zoom_moves_takes_its_windows_scale_on_every_frame() => WithManager((_, manager) =>
    {
        var time = new ManualTimeProvider();
        using var clock = SwiftClock.Provider.Override(time);
        var words = new TextBlock { Text = "Opus 5.5", FontSize = 13 };
        var popup = new Popup { Placement = PlacementMode.Bottom, ShouldUseOverlayLayer = true, Child = words };
        manager.MainContent = () =>
        {
            var anchor = new Button { Content = "Model", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            popup.PlacementTarget = anchor;
            return new Grid { Children = { anchor, popup } };
        };
        var main = manager.PrepareMain();
        SettingsProviderPopupLayers.Install(main);
        manager.ShowMain();
        RunFrames(time, main);
        try
        {
            popup.IsOpen = true;
            var host = Assert.IsType<OverlayPopupHost>(Host(popup));
            host.Template = new FuncControlTemplate<OverlayPopupHost>((owner, scope) => new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] }.RegisterInNameScope(scope));
            host.ApplyTemplate();
            host.InvalidateMeasure();
            RunFrames(time, main, seconds: 0.1);
            var zoom = Assert.IsType<PopupZoom>(host.Content);
            Assert.Equal(At100, zoom.Scale, 9);
            Assert.Equal(13 * At100, DrawnSize(words, main), 6);

            var frames = new List<(double Window, double Popup)>();
            manager.Model.Settings.AppZoom = 125;
            RunFrames(time, main, seconds: 0.5, each: () => frames.Add((AppZoom.ScaleOfWindow(main), zoom.Scale)));
            Assert.Contains(frames, frame => frame.Window > At100 && frame.Window < At125);
            Assert.All(frames, frame => Assert.Equal(frame.Window, frame.Popup, 9));
            Assert.Equal(At125, zoom.Scale, 9);
            Assert.Equal(13 * At125, DrawnSize(words, main), 6);
            Assert.True(host.Bounds.Width >= words.DesiredSize.Width * At125 - 0.01, $"host {host.Bounds.Size} is smaller than the drawn words {words.DesiredSize * At125}");
        }
        finally
        {
            popup.IsOpen = false;
            RunFrames(time, main, seconds: 0.1);
        }
    }, realSettings: false);
}

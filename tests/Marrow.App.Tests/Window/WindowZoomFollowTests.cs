using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Tour;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Xunit;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// ED-NEW-perf-motion-12 (the lead's ruling 2026-10-13 on finding 7): a window of its own open while the zoom moves (the welcome tour, a
// floating guide panel, an alert) follows the main window's scale on every frame of the glide, its size and place with it, and rests on
// the new scale with the main window; before, it stayed at the scale it opened with (AppZoom.Zoomed at its open, ED-NEW-textsize-1).
[Collection("Headless")]
public sealed class WindowZoomFollowTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    private const double At100 = AppZoom.BaseScale;
    private const double At125 = 1.25 * AppZoom.BaseScale;

    private static MCPCatalogEntry KeyEntry() =>
        new("notion", "Notion", "Notion", "Notes", MCPCategory.Developer, "notion",
            0, new MCPTransport.Stdio("notion", ValueList<string>.Empty), "https://notion.example/docs")
        {
            Fields = new ValueList<MCPField>([new MCPField("api_key", "API key", "Paste the key", MCPField.Kind.Secret, null, IsRequired: false)]),
        };

    private static FieldInfo PanelField => typeof(FloatingGuidePanel).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static PropertyInfo MainSeam => typeof(TourWindowController).GetProperty("MainWindowForTesting", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-12")]
    public void A_guide_panel_open_while_the_zoom_moves_takes_the_main_windows_scale_and_width_on_every_frame() => WithManager((_, manager) =>
    {
        var time = new ManualTimeProvider();
        using var clock = SwiftClock.Provider.Override(time);
        var main = manager.PrepareMain();
        manager.ShowMain();
        RunFrames(time, main);
        var shared = MCPKeyPanel.Shared;
        shared.Close();
        PanelField.SetValue(shared, null); // a panel window of its own (FloatingGuidePanelCornerTests)
        try
        {
            shared.Present(KeyEntry(), manager.Model);
            Dispatcher.UIThread.RunJobs();
            var panel = Assert.IsAssignableFrom<HostWindow>(PanelField.GetValue(shared));
            Assert.Equal(At100, AppZoom.ScaleOfWindow(panel), 9);
            var points = panel.Width / At100; // the source's width (FloatingGuidePanel.swift:10), drawn at the zoom
            var tall = panel.Height / At100;

            var frames = new List<(double Main, double Panel, double Width, double Height)>();
            manager.Model.Settings.AppZoom = 125;
            RunFrames(time, main, seconds: 0.5, each: () => frames.Add((AppZoom.ScaleOfWindow(main), AppZoom.ScaleOfWindow(panel), panel.Width, panel.Height)));
            Assert.Contains(frames, frame => frame.Main > At100 && frame.Main < At125);
            Assert.All(frames, frame =>
            {
                Assert.Equal(frame.Main, frame.Panel, 9);
                // The platform sizes a window to whole pixels: the width and height set for the scale, to the pixel.
                Assert.InRange(frame.Width, points * frame.Main - 1, points * frame.Main + 1);
                Assert.InRange(frame.Height, tall * frame.Main - 2.3, tall * frame.Main + 2.01); // the fitting height, rounded up to the next point
            });
            Assert.Equal(At125, AppZoom.ScaleOfWindow(panel), 9);
            Assert.InRange(panel.Width, points * At125 - 1, points * At125 + 1);
            if (panel.Screens.Primary is { } screen)
            {
                // Still parked 24 points in from the main screen's visible top right (FloatingGuidePanel.swift:21-23).
                var right = screen.WorkingArea.X + screen.WorkingArea.Width - 24 * screen.Scaling;
                Assert.InRange(panel.Position.X + panel.Width * screen.Scaling, right - 1, right + 1);
            }
        }
        finally
        {
            shared.Close();
            if (PanelField.GetValue(shared) is HostWindow built) built.Close();
            PanelField.SetValue(shared, null);
            Dispatcher.UIThread.RunJobs();
        }
    }, realSettings: false);

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-12")]
    public void The_tour_open_while_the_zoom_moves_takes_the_main_windows_scale_and_stays_centred_on_every_frame() => WithManager((_, manager) =>
    {
        var time = new ManualTimeProvider();
        using var clock = SwiftClock.Provider.Override(time);
        var main = manager.PrepareMain();
        manager.ShowMain();
        RunFrames(time, main);
        var controller = TourWindowController.Shared;
        controller.Close();
        MainSeam.SetValue(controller, main);
        try
        {
            var tour = controller.Present([]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(At100, AppZoom.ScaleOfWindow(tour), 9);
            var points = tour.Width / At100;
            var tall = tour.Height / At100;
            PixelPoint Centred(double scale)
            {
                var frame = main.FrameSize ?? main.Bounds.Size;
                var scaling = main.RenderScaling;
                return new PixelPoint((int)Math.Round(main.Position.X + (frame.Width - points * scale) * scaling / 2),
                    (int)Math.Round(main.Position.Y + (frame.Height - tall * scale) * scaling / 2));
            }
            Assert.Equal(Centred(At100), tour.Position);

            var frames = new List<(double Main, double Tour, double Width, double Height, PixelPoint At)>();
            manager.Model.Settings.AppZoom = 125;
            RunFrames(time, main, seconds: 0.5, each: () => frames.Add((AppZoom.ScaleOfWindow(main), AppZoom.ScaleOfWindow(tour), tour.Width, tour.Height, tour.Position)));
            Assert.Contains(frames, frame => frame.Main > At100 && frame.Main < At125);
            Assert.All(frames, frame =>
            {
                Assert.Equal(frame.Main, frame.Tour, 9);
                Assert.InRange(frame.Width, points * frame.Main - 1, points * frame.Main + 1); // to the pixel, as the platform sizes it
                Assert.InRange(frame.Height, tall * frame.Main - 1, tall * frame.Main + 1);
                Assert.Equal(Centred(frame.Main), frame.At);
            });
            Assert.Equal(At125, AppZoom.ScaleOfWindow(tour), 9);
        }
        finally
        {
            MainSeam.SetValue(controller, null);
            controller.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }, realSettings: false);

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-12")]
    public void A_dialog_sized_to_its_content_follows_the_zoom_on_every_frame_about_its_centre() => WithManager((_, manager) =>
    {
        var time = new ManualTimeProvider();
        using var clock = SwiftClock.Provider.Override(time);
        var main = manager.PrepareMain();
        manager.ShowMain();
        RunFrames(time, main);
        // What AlertPresenter.Show and ConversationTransferPrompt do: a window sized to its content, drawn at the zoom of the window it is
        // asked over, following it.
        var message = new TextBlock { Text = "Send this conversation?", FontSize = 13 };
        var dialog = new HostWindow { SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, Position = new PixelPoint(300, 200) };
        dialog.Content = AppZoom.Zoomed(message, AppZoom.ScaleOfWindow(main));
        using var follow = WindowZoom.Follow(dialog, message, main, WindowZoom.KeepingCentre(dialog));
        dialog.Show();
        try
        {
            Drain(dialog);
            var size = dialog.ClientSize;
            var scaling = dialog.RenderScaling;
            var centre = (X: dialog.Position.X + size.Width * scaling / 2, Y: dialog.Position.Y + size.Height * scaling / 2);
            var frames = new List<(double Main, double Dialog, Size Size, PixelPoint At)>();
            manager.Model.Settings.AppZoom = 125;
            RunFrames(time, main, seconds: 0.5, each: () => frames.Add((AppZoom.ScaleOfWindow(main), AppZoom.ScaleOfWindow(dialog), dialog.ClientSize, dialog.Position)));
            Assert.Contains(frames, frame => frame.Main > At100 && frame.Main < At125);
            Assert.All(frames, frame =>
            {
                Assert.Equal(frame.Main, frame.Dialog, 9);
                Assert.InRange(frame.At.X + frame.Size.Width * scaling / 2, centre.X - 1, centre.X + 1);
                Assert.InRange(frame.At.Y + frame.Size.Height * scaling / 2, centre.Y - 1, centre.Y + 1);
            });
            Drain(dialog);
            Assert.Equal(13 * At125, message.FontSize * Assert.IsType<Avalonia.Matrix>(message.TransformToVisual(dialog)).M11, 6);
            Assert.True(dialog.ClientSize.Width > size.Width * 1.2, $"the dialog grew with its words ({size.Width} to {dialog.ClientSize.Width})");
        }
        finally
        {
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }, realSettings: false);
}

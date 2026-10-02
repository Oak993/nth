using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views;

// Source: DroppyCode/App/Views/RootView.swift (B3 52bc792), :4-92 body (the window's backdrop, glass under a scrim, behind a
// detail pane that draws no base of its own), :155-165 DetailView's empty states, :299-450 WelcomeView and NoThreadView;
// UI/Chrome/Chrome.swift:201-273 WindowBackdrop.
//
// A native screenshot run showed the dark empty states (the welcome page, a project with no thread) as flat near-black windows
// while the thread view showed the blurred backdrop. The empty views draw no base of their own, so the window's own material
// (the Acrylic the window shows over the desktop, OD-0005) is all that sits behind them. Over a white stand-in for that material
// the app's own drawing must stay translucent in the dark theme, empty states included.
[Collection("Headless")]
public sealed class RootViewEmptyStateBackdropTests(HeadlessSession headless)
{
    private void Run(bool withProject)
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("project");
            harness.Main.OnMain(() =>
            {
                if (withProject) model.AddProject(new Project("Project", directory).Url);
                var host = new Grid { Background = Brushes.White }; // the bright material the window sits on, under the app's own drawing
                AppEnvironment.SetAppModel(host, model);
                host.Children.Add(new RootView());
                var window = new AvaloniaWindow
                {
                    Content = host, Width = 900, Height = 700, RequestedThemeVariant = ThemeVariant.Dark,
                };
                try
                {
                    window.Show();
                    for (var pass = 0; pass < 4; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
                    // Empty places of the window, away from every control: the black scrim and the tint leave about 0.44 of the white
                    // (an opaque dark layer would leave about 0.04).
                    var levels = FramePixels.Levels(window, (850, 650), (700, 100), (60, 650));
                    foreach (var level in levels)
                        Assert.True(level > 0.3, $"the empty window is drawn near-black ({level.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} over white): an opaque dark layer sits over the material");
                }
                finally { window.Close(); }
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-077")] // App/Views/RootView.swift:155-165 the welcome page, an empty library
    public void The_dark_welcome_page_leaves_the_window_material_showing() => Run(withProject: false);

    [Fact]
    [Trait("Req", "UI-WIN-070")] // App/Views/RootView.swift:424-450 a project with no thread open
    public void The_dark_no_thread_page_leaves_the_window_material_showing() => Run(withProject: true);
}


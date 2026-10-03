using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views;

// Source: DroppyCode/App/Views/RootView.swift:64-68 (the backdrop is clipped to the window's 26 pt shape), UI/Chrome/Chrome.swift:8, :211-224;
// OD-0075 (owner, 2026-10-02: "the wedge is filled with the window's scrim"; settles OD-0072); ED-236 UNRESOLVED (a).
//
// The real window is a rectangle of system Acrylic (DWM rounds it by 8 px), the content a 26 pt shape: between them the blur showed without
// the scrim (luma 126 beside 93). OD-0075 fills that wedge with the window's scrim. The window here is a headless one with a known grey
// background, so a corner's colour is the scrim over the grey; what the real Acrylic shows through it is a native check.
[Collection("Headless")]
public sealed class RootViewCornerWedgeTests(HeadlessSession headless)
{
    private const int Width = 900, Height = 600;
    private static readonly Color Grey = Color.FromRgb(128, 128, 128);

    private static (AvaloniaWindow Window, RootView Root) Mount(AppModel model, ThemeVariant theme)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var root = new RootView();
        host.Child = root;
        var window = new AvaloniaWindow { Width = Width, Height = Height, RequestedThemeVariant = theme, Content = host };
        window.Show();
        Settle(window);
        // The window's chrome has made it clear (WindowChrome.Configure: the system's Acrylic is behind it): the known grey stands in for that blur.
        window.Background = new SolidColorBrush(Grey);
        Settle(window);
        return (window, root);
    }

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // The scrim over the grey: black in the dark theme, white in the light one (Chrome.swift:256-257), as a fraction of 255.
    private static double Over(double alpha, bool dark) => (Grey.R * (1 - alpha) + (dark ? 0 : 255) * alpha) / 255.0;

    private static readonly (int X, int Y)[] Corners =
    [
        (0, 0), (1, 1), (5, 5), (Width - 1, 0), (Width - 2, 1), (Width - 6, 5),
        (0, Height - 1), (1, Height - 2), (5, Height - 6), (Width - 1, Height - 1), (Width - 2, Height - 2), (Width - 6, Height - 6),
    ];

    private static void AssertCorners(AvaloniaWindow window, double expected, string when)
    {
        var levels = FramePixels.Levels(window, Corners);
        for (var index = 0; index < levels.Length; index++)
            Assert.True(Math.Abs(levels[index] - expected) <= 0.012, $"{when}: corner pixel {Corners[index]} is {levels[index]:0.###}, the scrim over the grey is {expected:0.###}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-WIN-062")] // App/Views/RootView.swift:64-68; UI/Chrome/Chrome.swift:8, :211-215
    [Trait("Req", "UI-LAUNCH-001")] // App/Window/WindowManager.swift:62-69 the main window's surface; OD-0075
    public void The_main_windows_four_corners_outside_the_26_point_curve_wear_its_scrim(bool dark)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                model.Settings.BackdropOpacity = 0.5;
                var (window, _) = Mount(model, dark ? ThemeVariant.Dark : ThemeVariant.Light);
                try
                {
                    AssertCorners(window, Over(WindowBackdrop.Scrim(0.5, dark), dark), "at the default glass");
                    // The shape's own edges are not covered: the window is not a square of scrim.
                    var middle = FramePixels.Levels(window, (Width / 2, 0), (0, Height / 2));
                    Assert.All(middle, level => Assert.NotEqual(Over(WindowBackdrop.Scrim(0.5, dark), dark), level, 0.01));
                }
                finally { window.Close(); }
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-062")] // UI/Chrome/Chrome.swift:211-224
    [Trait("Req", "UI-LAUNCH-001")] // OD-0075
    public void The_corner_scrim_follows_the_backdrop_opacity_the_theme_and_the_solid_window()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            AvaloniaWindow window = null!;
            // The model's changes reach the view on the next turn of the main actor: each step is its own turn.
            void Step(Action change, double expected, string when)
            {
                harness.Main.OnMain(change);
                harness.Main.OnMain(() => { Settle(window); AssertCorners(window, expected, when); });
            }
            harness.Main.OnMain(() =>
            {
                model.Settings.BackdropOpacity = 0.25;
                (window, _) = Mount(model, ThemeVariant.Dark);
            });
            try
            {
                harness.Main.OnMain(() => AssertCorners(window, Over(WindowBackdrop.Scrim(0.25, true), true), "at 0.25 dark"));
                Step(() => model.Settings.BackdropOpacity = 0.75, Over(WindowBackdrop.Scrim(0.75, true), true), "at 0.75 dark");
                Step(() => window.RequestedThemeVariant = ThemeVariant.Light, Over(WindowBackdrop.Scrim(0.75, false), false), "at 0.75 light");
                Step(() => model.Settings.BackdropOpacity = 1, 1.0, "solid light: the window is white, corners too");
                Step(() => window.RequestedThemeVariant = ThemeVariant.Dark, 0.0, "solid dark: the window is black, corners too");
            }
            finally { harness.Main.OnMain(window.Close); }
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-062")] // App/Views/RootView.swift:66-68 (the glass and its edge stop at the window's own curve)
    public void The_wedge_sits_beneath_the_windows_clip_so_the_glass_still_stops_at_the_curve_and_takes_no_pointer()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (window, root) = Mount(model, ThemeVariant.Dark);
                try
                {
                    var wedge = root.CornerWedge;
                    var clip = root.WindowClip;
                    var layers = Assert.IsAssignableFrom<Panel>(clip.GetVisualParent());
                    Assert.Same(layers, wedge.GetVisualParent());
                    Assert.True(wedge.ZIndex < clip.ZIndex, "the wedge is painted first, under the window's clipped content");
                    Assert.Equal(4, layers.Children.IndexOf(wedge)); // the source's four layers (clipped body, genie, row glide, palette) keep their places
                    Assert.False(wedge.IsHitTestVisible);
                    Assert.Equal(clip.Bounds.Size, wedge.Bounds.Size); // the same rectangle the clip's shape is cut from
                    Assert.Equal(new Point(0, 0), wedge.TranslatePoint(new Point(0, 0), clip));
                    // The clip's geometry and the wedge's are one shape and its complement (UI.Tests proves the complement itself).
                    Assert.NotNull(clip.Clip);
                    Assert.Equal(clip.Clip.Bounds, WindowCornerWedge.WedgeGeometry(wedge.Bounds.Size).Bounds);
                }
                finally { window.Close(); }
            });
        });
    }
}

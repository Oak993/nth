using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Xunit;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// ED-NEW-settingsinapp-4: the panel-level replacement for WindowManagerCornerWedgeTests and the corner case of SettingsRiseTests, which went
// with the Settings window (OD-0151). Those proved, in pixels, that the window's corners outside its 26 pt curve were the body just inside it
// (OD-0075: DWM rounded the window's rectangle) and that the corners faded in with the Rise's glass; ED-NEW-textsize-2 drew that curve at the
// base scale. The panel draws its own 26 pt continuous shape inside the main window (SettingsView.swift:118-122), so the same corner pixels now
// prove the opposite side of the seam: outside the curve, drawn at the base scale, the panel draws nothing (what shows is the dim over the main
// window, exactly as with the panel's card not drawn), inside it the panel's glass is drawn, at every backdrop opacity, in both appearances,
// and through the open, because the corners are the glass's own clip and no layer of their own.
[Collection("Headless")]
[Trait("Decision", "OD-0151")]
[Trait("Decision", "ED-NEW-settingsinapp-4")]
public sealed class SettingsPanelCornerTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    private static readonly Color Grey = Color.FromRgb(128, 128, 128);

    private static void Frames(SettingsRiseRig rig, HostWindow window, int count)
    {
        for (var i = 0; i < count; i++)
        {
            rig.Frame(1.0 / 60);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        Drain(window);
    }

    // The panel's corner pairs on the window, as drawn: its 26 pt shape at the scale it is drawn at (the base scale at 100 %, and the open's
    // scale while it opens), the wedge pixels on the edge rows clear of its 1 pt hairline.
    private static CornerSeam.Pair[] Pairs(SettingsOverlay overlay, HostWindow window)
    {
        var drawn = ZoomDrawn.On(overlay.Card, window);
        var origin = overlay.Card.TranslatePoint(default, window)!.Value;
        var radius = Chrome.WindowCornerRadius * drawn.Width / overlay.Card.Bounds.Width;
        var (x, y) = ((int)Math.Round(origin.X), (int)Math.Round(origin.Y));
        CornerSeam.Pair Moved(CornerSeam.Pair pair) =>
            pair with { Wedge = (pair.Wedge.X + x, pair.Wedge.Y + y), Body = (pair.Body.X + x, pair.Body.Y + y) };
        var (width, height) = ((int)Math.Round(drawn.Width), (int)Math.Round(drawn.Height));
        return CornerSeam.Pairs(width, height, radius, [2, 3, 4, 5, 6, 8, 10], edgeInset: 2)
            .Concat(CornerSeam.Pairs(width, height, radius, [3, 4, 5, 6, 7], edgeInset: 3))
            .Select(Moved).ToArray();
    }

    // The pixels of the pairs drawn with the panel's card, and again without it (the dim, the shadow and the main window stay).
    private static ((int R, int G, int B)[] Wedge, (int R, int G, int B)[] Body, (int R, int G, int B)[] BareWedge, (int R, int G, int B)[] BareBody) Read(SettingsOverlay overlay, HostWindow window, CornerSeam.Pair[] pairs)
    {
        Drain(window);
        var wedge = CornerSeam.Rgb(window, pairs.Select(pair => pair.Wedge).ToArray());
        var body = CornerSeam.Rgb(window, pairs.Select(pair => pair.Body).ToArray());
        overlay.Card.Opacity = 0;
        Drain(window);
        var bareWedge = CornerSeam.Rgb(window, pairs.Select(pair => pair.Wedge).ToArray());
        var bareBody = CornerSeam.Rgb(window, pairs.Select(pair => pair.Body).ToArray());
        overlay.Card.Opacity = 1;
        Drain(window);
        return (wedge, body, bareWedge, bareBody);
    }

    private static void AssertCut(SettingsOverlay overlay, HostWindow window, string when, bool solid)
    {
        var pairs = Pairs(overlay, window);
        Assert.True(pairs.Length >= 4 * 8, $"{when}: {pairs.Length} corner pairs");
        var (wedge, body, bareWedge, bareBody) = Read(overlay, window, pairs);
        for (var index = 0; index < pairs.Length; index++)
        {
            Assert.True(CornerSeam.Near(wedge[index], bareWedge[index]),
                $"{when}: the {pairs[index].Corner} pixel {pairs[index].Wedge} outside the panel's curve is {wedge[index]} with the panel drawn and {bareWedge[index]} without it (within {CornerSeam.Tolerance} on each channel)");
            if (solid)
                Assert.False(CornerSeam.Near(body[index], bareBody[index], 8),
                    $"{when}: the {pairs[index].Corner} pixel {pairs[index].Body} just inside the panel's curve is {body[index]}, what is under the panel ({bareBody[index]}): the glass is not drawn there");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Decision", "OD-0075")] // App/Views/Settings/SettingsView.swift:118-122; UI/Chrome/Chrome.swift:211-224
    public void The_panel_s_four_corners_are_cut_at_its_26_point_curve_drawn_at_the_base_scale_at_every_backdrop_opacity(bool dark) => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        manager.Model.Settings.BackdropOpacity = 0.25;
        manager.ShowSettings(SettingsPage.Archive);
        Dispatcher.UIThread.RunJobs();
        var window = Settings(manager);
        window.Width = 1296; window.Height = 1125;
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        window.Background = new SolidColorBrush(Grey); // the system's Acrylic stood in for by a known grey
        Frames(rig, window, 40); // the open and the appearance's 0.32 s turnover (OD-0088) run out
        var overlay = Overlay(manager);
        // ED-NEW-textsize-2: the panel's points are drawn at the base scale, its 26 pt shape with them.
        Assert.InRange(ZoomDrawn.On(overlay.Card, window).Width - overlay.Card.Bounds.Width * AppZoom.BaseScale, -0.01, 0.01);
        var theme = dark ? "dark" : "light";
        foreach (var opacity in new[] { 0.25, 0.5, 0.75, 1.0 })
        {
            manager.Model.Settings.BackdropOpacity = opacity;
            Frames(rig, window, 2);
            AssertCut(overlay, window, $"{theme} at {opacity}", solid: opacity >= 1);
        }
    });

    [Fact]
    [Trait("Decision", "OD-0074")] // the corners belong to the glass, which fades in with the open
    public void The_corners_open_with_the_panel_s_glass_because_they_are_its_clip() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        manager.ShowMain();
        var window = MainOf(manager);
        window.Width = 1296; window.Height = 1125;
        window.Background = new SolidColorBrush(Grey);
        Drain(window);
        manager.Model.Settings.BackdropOpacity = 1;
        manager.ShowSettings(SettingsPage.Archive);
        var overlay = Overlay(manager);
        // The glass is inside the panel's clipped shape, one layer with the content: no separate corner layer to show before it or after it.
        Assert.Contains(overlay.Backdrop, overlay.Card.GetVisualDescendants());
        rig.Frame(0.0); Drain(window);
        rig.Frame(0.1); Drain(window);
        var host = Assert.IsAssignableFrom<Control>(overlay.Card.Parent);
        Assert.InRange(host.Opacity, 0.3, 0.99); // part way through the open
        AssertCut(overlay, window, "while it opens", solid: false);
        Frames(rig, window, 30);
        Assert.Equal(1, host.Opacity);
        AssertCut(overlay, window, "opened", solid: true);
        // The glass reaches the panel's edge, drawn at the base scale.
        var drawn = ZoomDrawn.On(overlay.Card, window);
        ZoomDrawn.Covers(new Size(Math.Floor(drawn.Width), Math.Floor(drawn.Height)), overlay.Backdrop, window);
    });
}

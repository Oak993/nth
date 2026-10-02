using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Theme;
using Marrow.Core.Support;

namespace Marrow.App.Tests.Views.Settings;

// Defect: in the real Settings window the Windows caption buttons (top right, OD-0044) sat on top of the first row of a
// page that has no chrome controls (General's Token activity selector, Hydra's switch). The source puts the traffic
// lights over the sidebar's empty top (SettingsView.swift:137-140), never over the detail pane; the port moves them to the
// top right (OD-0044, OD-0046), so the detail pane must clear them wherever a page's content starts.
// Source: App/Views/Settings/SettingsView.swift:220-227 (pages with chrome controls start Chrome.chromeTopPadding +
// Chrome.capsuleHeight + 16 below the sheet edge, "the others clear their capsules"; the caption cluster takes the capsules' row on every page).
[Collection("Headless")]
public sealed class SettingsCaptionClearanceTests(HeadlessSession headless)
{
    private static void Drain(Avalonia.Controls.Window window) { for (var i = 0; i < 5; i++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } }

    // The caption cluster's rectangle in the window's own coordinates (OD-0044: three 46 x 40 buttons, 6 from the right
    // edge, CaptionTop from the top), for the Settings window's 780 x 580 content (SettingsView.swift:122).
    private static Rect CaptionCluster(double windowWidth) =>
        new(windowWidth - WindowChrome.CaptionTrailingInset - WindowChrome.CaptionClusterWidth, WindowChrome.CaptionTop,
            WindowChrome.CaptionClusterWidth, CaptionButton.ButtonHeight);

    private void AtRest(SettingsPage page, Action<SettingsView, Avalonia.Controls.Window> check) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var wallpaper = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var updates = UpdateChecker.OverrideShared(new UpdateChecker(AppInfo.Current, [], ""));
        SettingsNavigation.Shared.RequestedPage = page;
        var model = new AppModel();
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var view = new SettingsView();
        host.Child = view;
        var window = new Avalonia.Controls.Window { Content = host, Width = 780, Height = 580 };
        try { window.Show(); Drain(window); check(view, window); }
        finally { window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); SettingsNavigation.Shared.RequestedPage = null; }
    });

    // What the page itself draws (its text, glyphs, controls and cards), without the veil, the backdrop and the chrome row,
    // which have their own trailing reserve.
    private static IEnumerable<Control> PageInk(SettingsView view)
    {
        var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Bounds.Width > 400 && !s.GetVisualAncestors().OfType<ScrollViewer>().Any()); // the detail pane's own scroll, not the sidebar's
        return scroll.GetVisualDescendants().OfType<Control>().Where(c =>
            c.IsVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0
            && (c is TextBlock { Text.Length: > 0 } or SwiftImage or Button or TextBox or Slider or ChromeCard or ChromeSection or ChromeRow));
    }

    [Theory]
    [Trait("Req", "UI-SETTINGS-078")] // SettingsView.swift:220-227, :137-140; OD-0044, OD-0046
    [InlineData(SettingsPage.General)]
    [InlineData(SettingsPage.Appearance)]
    [InlineData(SettingsPage.Hydra)]
    [InlineData(SettingsPage.SourceControl)]
    [InlineData(SettingsPage.Shortcuts)]
    [InlineData(SettingsPage.Providers)]
    [InlineData(SettingsPage.Models)]
    [InlineData(SettingsPage.Mcp)]
    [InlineData(SettingsPage.About)]
    public void No_page_draws_under_the_caption_buttons_when_it_rests_at_the_top(SettingsPage page) =>
        AtRest(page, (view, window) =>
        {
            var cluster = CaptionCluster(780);
            var ink = PageInk(view).ToList();
            Assert.NotEmpty(ink);
            foreach (var control in ink)
            {
                var origin = control.TranslatePoint(default, window)!.Value;
                var box = new Rect(origin, control.Bounds.Size);
                Assert.False(box.Intersects(cluster),
                    $"{page}: {control.GetType().Name} {(control as TextBlock)?.Text} at {box} is under the caption buttons {cluster}");
            }
        });
}

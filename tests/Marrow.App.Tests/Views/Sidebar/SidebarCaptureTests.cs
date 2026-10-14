using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Sidebar;

// Headless renders of the whole sidebar, laid beside the owner's reference video of the real Mac app 1.7.0
// (E:\Droppy\work\reference-video\2-chat-use.mov; about 1 px = 1 pt). The library below mirrors the reference recording's sidebar at
// 162 s in shape only (activity layout, a folded "4 heads" stub, a settled thread, the two footer rows); its titles,
// dates and settings are the reference recording's own and are not defaults. With MARROW_SIDEBAR_CAPTURE set to a folder the frame
// is written there as sidebar-port.png for the side-by-side (evidence kept under E:\Droppy\work\parity\evidence\
// SidebarView\, never in git); the assertions here are the numbers read off that comparison.
[Collection("Headless")]
public sealed class SidebarCaptureTests(HeadlessSession headless)
{
    // The manual clock's start (Marrow.Testing ManualTimeProvider): 2026-09-22 12:00 UTC, a Tuesday.
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static ChatThread ThreadAt(Project project, string title, DateTimeOffset at) =>
        AppModelSamples.Thread(project) with { Title = title, CreatedAt = at, UpdatedAt = at };

    // The reference window's content starts at screen y 29 (its top rim) and its bottom edge is at y 1356, so the
    // sidebar column is 1327 pt tall; the band is 268 pt wide (CATALOGUE static.main-window-light-vs-dark).
    internal static void WithReferenceScene(AppModelHarness harness, ManualFrameSource frames, Action<SidebarReferenceScene> body)
    {
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var project = AppModelSamples.Project("Atlas-Migration", harness.NewFolder("Atlas-Migration"));
            var lead = ThreadAt(project, "Test four attention heads in a row", Now) with { ExpandsHelpers = false };
            var heads = Enumerable.Range(1, 4).Select(i => ThreadAt(project, "Head" + i, Now.AddMinutes(-i)) with
            {
                ParentThreadID = lead.Id,
                IsInPanel = false,
                Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) with { Index = i },
            }).ToArray();
            var friday = ThreadAt(project, "Establish Atlas repo baseline", Now.AddDays(-4));
            var draft = ThreadAt(project, "New thread", Now.AddDays(-4).AddMinutes(-5));
            var older = ThreadAt(project, "I need the actual task list", Now.AddDays(-5));
            var settled = ThreadAt(project, "hi", Now.AddDays(-6)) with { IsSettled = true };
            harness.WriteLibrary([project], [lead, .. heads, friday, draft, older, settled]);
            var model = harness.MakeModel();
            SidebarReferenceScene scene = null!;
            harness.Main.OnMain(() =>
            {
                model.Settings.SidebarActivityView = true;
                model.Settings.ActivityThreadStyle = ActivityThreadStyle.Icon;
                model.Settings.SettledCollapsed = false;
                model.SelectedThreadID = lead.Id;
                var host = new Border { Background = new SolidColorBrush(Color.FromRgb(58, 60, 52)) };
                AppEnvironment.SetAppModel(host, model);
                var view = new SidebarView(false, null);
                host.Child = view;
                var window = new AvaloniaWindow { Content = host, Width = 268, Height = 1327, RequestedThemeVariant = ThemeVariant.Dark };
                window.Show();
                window.UpdateLayout();
                scene = new SidebarReferenceScene(window, view, model);
            });
            try
            {
                for (var i = 0; i < 20; i++) Tick(harness, frames, scene.Window, 80); // every row's delayed insertion fade (:317-325) has finished
                harness.Main.OnMain(() => body(scene));
            }
            finally { harness.Main.OnMain(() => scene.Window.Close()); }
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    internal static void Tick(AppModelHarness harness, ManualFrameSource frames, AvaloniaWindow window, int milliseconds)
    {
        harness.Main.AdvanceMilliseconds(milliseconds);
        harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
    }

    internal static void SaveIfAsked(AvaloniaWindow window, string name)
    {
        var folder = Environment.GetEnvironmentVariable("MARROW_SIDEBAR_CAPTURE");
        if (string.IsNullOrEmpty(folder)) return;
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
#pragma warning disable CS0618 // the PNG default is all the side-by-side needs
        frame.Save(Path.Combine(folder, name));
#pragma warning restore CS0618
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")] // capture rig only; the proofs of the ledger entries cite the numbers it produced
    public void The_reference_scene_renders_and_can_be_saved_for_the_side_by_side() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var seam = FrameClock.Source.Override(frames);
        WithReferenceScene(harness, frames, scene =>
        {
            SaveIfAsked(scene.Window, "sidebar-port.png");
            var dump = Environment.GetEnvironmentVariable("MARROW_SIDEBAR_CAPTURE");
            if (!string.IsNullOrEmpty(dump))
                File.WriteAllLines(Path.Combine(dump, "layout-port.txt"), Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(scene.View).OfType<Control>()
                    .Where(c => c.IsVisible && c.Bounds.Height > 0 && c.Bounds.Height < 200)
                    .Select(c => (c, p: c.TranslatePoint(new Point(0, 0), scene.Window)))
                    .Where(t => t.p is { } q && q.Y < 420)
                    .Select(t => $"{t.p!.Value.Y,7:0.##} {t.c.Bounds.Height,6:0.##} x{t.p!.Value.X,6:0.##} w{t.c.Bounds.Width,6:0.##} {t.c.GetType().Name} {(t.c as TextBlock)?.Text}"));
            Assert.True(scene.View.Bounds.Height > 0);
        });
    });
}

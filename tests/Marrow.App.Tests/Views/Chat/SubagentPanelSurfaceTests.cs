using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/SubagentPanel.swift (B3 52bc792), :268-356 body.
[Collection("Headless")]
public sealed class SubagentPanelSurfaceTests(HeadlessSession headless)
{
    private static SubagentPanel MakePanel(ChatThread thread, Project project, Size size)
    {
        var constructor = typeof(SubagentPanel).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(value => value.GetParameters().Length == 8);
        return (SubagentPanel)constructor.Invoke([thread, size, project.Path, project.Name,
            (Action<Size>)(_ => { }), (Action)(() => { }), (Action)(() => { }), (Func<TimelineScrollState, Control>)(_ => new Border())]);
    }

    private void Run(Action<SubagentPanel, Size> check, bool head = true) => Run((panel, size, _) => check(panel, size), head);

    private void Run(Action<SubagentPanel, Size, Avalonia.Controls.Window> check, bool head = true, ThemeVariant? theme = null)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness(withRuntime: false);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var project = AppModelSamples.Project("Panel", h.NewFolder("panel"));
            var thread = AppModelSamples.Thread(project) with { Hydra = head ? AppModelSamples.Head(HydraHeadInfo.Status.Running) : null };
            h.WriteLibrary([project], [thread]);
            AppModelHarness.WriteDocument(thread.Id);
            Marrow.Services.Store.DocumentPrefetch.Shared.Warm([thread.Id], urgent: true)?.Completion.GetAwaiter().GetResult();
            var manager = new WindowManager();
            using var shared = WindowManager.OverrideShared(manager);
            var model = manager.Model;
            model.Settings.HydraShowsHeadDetails = false;
            var runtime = model.Runtime(thread.Id);
            var size = new Size(400, 280);
            var panel = MakePanel(thread, project, size);
            var host = new Border { Child = panel, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            AppEnvironment.SetAppModel(host, model);
            var window = new Avalonia.Controls.Window { Content = host, Width = 900, Height = 700, Background = Brushes.White };
            if (theme is not null) window.RequestedThemeVariant = theme;
            AppEnvironment.SetWindowLiveResize(host, new WindowLiveResize(window));
            window.Show();
            for (var pass = 0; pass < 4; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
            try { check(panel, size, window); }
            finally
            {
                window.Close();
                runtime.CancelPersistence();
                var preparation = new FileIndex().Prepare(project.Path);
                HeadlessSession.RunDispatcherUntil(preparation, AppModelHarness.Bound);
                preparation.GetAwaiter().GetResult();
            }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-HYDRA-230")] // App/Views/Chat/SubagentPanel.swift:268-296 the panel's chat box is the bottom inset, under the top strip
    public void The_panels_own_chat_box_sits_at_its_bottom_edge_below_the_top_strip(bool head) => Run((panel, size) =>
    {
        var composer = panel.GetVisualDescendants().OfType<ComposerArea>().Single();
        var top = composer.TranslatePoint(default, panel)!.Value.Y;
        Assert.Equal(size.Height, top + composer.Bounds.Height, 1);
        Assert.True(top > Marrow.UI.Chrome.Chrome.ChromeTopPadding + Marrow.UI.Chrome.Chrome.CapsuleHeight, $"the chat box starts at {top}, inside the top strip");
    }, head);
    private static Border Scrim(SubagentPanel panel) => Assert.Single(panel.GetVisualDescendants().OfType<Border>(), b => b.BoxShadow.Count > 0);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-HYDRA-229")] // SubagentPanel.swift:347-355 shape.fill((isDark ? Color.black : Color.white).opacity(isDark ? 0.3 : 0.34)).shadow(color: .black.opacity(isDark ? 1 : 0.65), radius: 28, y: 10)
    public void A_separate_unclipped_scrim_under_the_clipped_glass_carries_the_one_shadow(bool dark) => Run((panel, size, _) =>
    {
        var scrim = Scrim(panel);
        Assert.Equal(new CornerRadius(22), scrim.CornerRadius);
        Assert.Equal(size, scrim.Bounds.Size);
        Assert.False(scrim.ClipToBounds); // the shadow reaches past the panel's edge
        var fill = Assert.IsAssignableFrom<ISolidColorBrush>(scrim.Background).Color;
        Assert.Equal(dark ? Color.FromArgb(77, 0, 0, 0) : Color.FromArgb(87, 255, 255, 255), fill); // 0.3 and 0.34 of 255
        Assert.Equal(1, scrim.BoxShadow.Count);
        var shadow = scrim.BoxShadow[0];
        Assert.Equal((28 - 0.5) / 0.288675, shadow.Blur, 6); // radius 28 drawn as sigma 28 (ED-NEW-rec-shadow; measured by SwiftShadowHeadlessTests)
        Assert.Equal(10, shadow.OffsetY);
        Assert.Equal(0, shadow.OffsetX);
        Assert.Equal(dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(166, 0, 0, 0), shadow.Color); // 1.0 and 0.65 of 255
        // :345-349 the glass is clipped to its shape and the scrim is its background: a sibling drawn first, not its parent.
        var host = Assert.IsType<Grid>(scrim.Parent);
        Assert.Same(panel, host.Parent);
        var glass = Assert.IsType<Border>(Assert.Single(host.Children, x => x != scrim));
        Assert.True(host.Children.IndexOf(scrim) < host.Children.IndexOf(glass));
        Assert.True(glass.ClipToBounds);
        Assert.False(panel.ClipToBounds);
        Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Border>(), b => b != scrim && b.BoxShadow.Count > 0);
    }, theme: dark ? ThemeVariant.Dark : ThemeVariant.Light);

    [Fact]
    [Trait("Req", "PR-HYDRA-229")] // SubagentPanel.swift:351 isDark is read from the environment on every draw: the scrim and its shadow follow a theme change
    public void The_scrim_and_its_shadow_follow_a_theme_change() => Run((panel, _, window) =>
    {
        Assert.Equal(Color.FromArgb(87, 255, 255, 255), Assert.IsAssignableFrom<ISolidColorBrush>(Scrim(panel).Background).Color);
        Assert.Equal(Color.FromArgb(166, 0, 0, 0), Scrim(panel).BoxShadow[0].Color);
        window.RequestedThemeVariant = ThemeVariant.Dark;
        for (var pass = 0; pass < 4; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        Assert.Equal(Color.FromArgb(77, 0, 0, 0), Assert.IsAssignableFrom<ISolidColorBrush>(Scrim(panel).Background).Color);
        Assert.Equal(Color.FromArgb(255, 0, 0, 0), Scrim(panel).BoxShadow[0].Color);
    }, theme: ThemeVariant.Light);

    [Fact]
    [Trait("Req", "PR-HYDRA-229")] // SubagentPanel.swift:354 the shadow is drawn: it darkens the window below the panel's bottom edge (y: 10), fading with distance
    public void The_panels_shadow_is_drawn_past_its_bottom_edge_and_fades_with_distance() => Run((panel, size, window) =>
    {
        var bottom = panel.TranslatePoint(new Point(size.Width / 2, size.Height), window)!.Value;
        var x = (int)bottom.X;
        var y = (int)Math.Ceiling(bottom.Y);
        var levels = FramePixels.Levels(window, (x, y + 4), (x, y + 30), (x, y + 60), (x, y + 300));
        // Light theme: black at 0.65 with sigma 28 under a white window. Far below it the window is untouched.
        Assert.True(levels[3] > 0.99, $"the window far below the panel reads {levels[3]:F3}, not white");
        Assert.True(levels[0] < levels[3] - 0.15, $"just below the panel the window reads {levels[0]:F3}: no shadow is drawn");
        Assert.True(levels[0] < levels[1] && levels[1] < levels[2] && levels[2] < levels[3], $"the shadow does not fade with distance: {string.Join(", ", levels.Select(l => l.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}");
    }, theme: ThemeVariant.Light);
}

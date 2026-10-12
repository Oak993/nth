using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :23 (GlassEffectContainer(spacing: 8)), :186-187 .modifier(ComposerSurface()),
// :1293-1307 (in the main chat the pill is .glassEffect(.regular, in: RoundedRectangle(cornerRadius: 22, style: .continuous))). UI-COMPOSER-002: the
// existing tests read the attached effect (ComposerViewTests.In_the_main_chat_the_pill_is_regular_glass_...) and the container's material in the
// composition tree (ComposerViewGlassPlacementTests, ED-320); this proves what it draws. Fine black and white stripes (six pixels, the
// GlassWindowNativeTests pattern) lie under the real ComposerArea as the main chat mounts it: once the scene has settled, a line inside the pill,
// above its controls, shows them frosted; the same line with the glass taken off shows them sharp, so the frost is the material's.
[Collection("Headless")]
public sealed class ComposerAreaGlassFrostTests(HeadlessSession headless)
{
    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 8; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    }

    private static double Contrast(AvaloniaWindow window, int y, int from, int to) =>
        FramePixels.Levels(window, Enumerable.Range(from, to - from).Select(x => (x, y)).ToArray()) is var levels ? levels.Max() - levels.Min() : 0;

    [Fact]
    [Trait("Req", "UI-COMPOSER-002")] // App/Views/Composer/ComposerView.swift:1304 .glassEffect(.regular, in: RoundedRectangle(cornerRadius: 22, style: .continuous))
    public void In_the_main_chat_the_stripes_behind_the_pill_are_frosted_once_the_scene_settles_and_sharp_without_the_glass() => headless.OnUiThread(() =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = new ThreadRuntimePart12Harness();
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        var area = h.Main.OnMain(() => { var area = new ComposerArea(h.Runtime, workingDirectory: null); AppEnvironment.SetAppModel(area, h.App); return area; });
        var host = new Border { Child = area, Width = 700, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        AppEnvironment.SetAppModel(host, h.App);
        var window = new AvaloniaWindow { Content = new Panel { Children = { new Stripes(), host } }, Width = 800, Height = 300 };
        h.Main.OnMain(() => { window.Show(); Settle(window); });
        for (var round = 0; round < 3; round++) h.Main.OnMain(() => Settle(window));
        h.Main.OnMain(() =>
        {
            var pill = Assert.Single(area.GetVisualDescendants().OfType<Control>(),
                control => SwiftGlass.GetEffect(control) is { } effect && effect.Shape.Equals(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true)));
            var container = Assert.Single(area.GetVisualDescendants().OfType<Control>(), control => SwiftGlass.GetContainerSpacing(control) == 8);
            // OD-0147: this measure is the release material's (its frost reaches 3 pt inside the edge); the shipped canvas keeps its outer rim clear
            // and is measured past it (the canvas case below), so the pill is drawn here with the release look
            SwiftGlass.SetEffect(pill, SwiftGlass.GetEffect(pill)!.WithLook(GlassLook.Release));
            Settle(window);
            var at = pill.TranslatePoint(default, window)!.Value;
            Assert.True(pill.Bounds.Height >= 44, $"the pill is laid out: {pill.Bounds}");
            // a line 3 points below the pill's top edge (above the 22 pt text row and the controls it centres), clear of the 22 pt corners
            var (y, from, to) = ((int)Math.Ceiling(at.Y + 3), (int)Math.Ceiling(at.X + 24), (int)Math.Floor(at.X + pill.Bounds.Width - 24));
            Assert.True(Contrast(window, 20, from, to) > 0.9, $"the bare stripes above the composer keep only {Contrast(window, 20, from, to):F2}");
            var frosted = Contrast(window, y, from, to);
            Assert.True(frosted < 0.25, $"the stripes behind the pill keep {frosted:F2} of their contrast");

            SwiftGlass.SetContainerSpacing(container, null);
            SwiftGlass.SetEffect(pill, null);
            Settle(window);
            var bare = Contrast(window, y, from, to);
            Assert.True(bare > 0.5, $"without the glass the pill's line keeps only {bare:F2}, so the line does not measure the glass");
            window.Close();
        });
    });

    [Fact]
    [Trait("Decision", "OD-0147")] // the shipped canvas glass: the stripes behind the pill's body are frosted (the same bound), its outer rim clearer (the canvas keeps the rim sharp so it bends what crosses it), and sharp without the glass
    public void In_the_main_chat_the_canvas_pill_frosts_the_stripes_behind_its_body_and_keeps_its_rim_clear() => headless.OnUiThread(() =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = new ThreadRuntimePart12Harness();
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        var area = h.Main.OnMain(() => { var area = new ComposerArea(h.Runtime, workingDirectory: null); AppEnvironment.SetAppModel(area, h.App); return area; });
        var host = new Border { Child = area, Width = 700, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        AppEnvironment.SetAppModel(host, h.App);
        var window = new AvaloniaWindow { Content = new Panel { Children = { new Stripes(), host } }, Width = 800, Height = 300 };
        h.Main.OnMain(() => { window.Show(); Settle(window); });
        for (var round = 0; round < 3; round++) h.Main.OnMain(() => Settle(window));
        h.Main.OnMain(() =>
        {
            var pill = Assert.Single(area.GetVisualDescendants().OfType<Control>(),
                control => SwiftGlass.GetEffect(control) is { } effect && effect.Shape.Equals(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true)));
            var container = Assert.Single(area.GetVisualDescendants().OfType<Control>(), control => SwiftGlass.GetContainerSpacing(control) == 8);
            Assert.Equal(GlassKind.Composer, GlassLooks.Of(SwiftGlass.GetEffect(pill)!).Canvas!.Kind); // the shipped material, the canvas's composer
            // the pill's glass alone: its text and controls set aside, so a line through its middle reads the glass
            GlassAloneScene.Hide(area, [pill]);
            Settle(window);
            var at = pill.TranslatePoint(default, window)!.Value;
            Assert.True(pill.Bounds.Height >= 44, $"the pill is laid out: {pill.Bounds}");
            var (y, from, to) = ((int)Math.Round(at.Y + pill.Bounds.Height / 2), (int)Math.Ceiling(at.X + 24), (int)Math.Floor(at.X + pill.Bounds.Width - 24));
            Assert.True(Contrast(window, 20, from, to) > 0.9, $"the bare stripes above the composer keep only {Contrast(window, 20, from, to):F2}");
            var frosted = Contrast(window, y, from, to);
            Assert.True(frosted < 0.25, $"the stripes behind the pill's body keep {frosted:F2} of their contrast");
            var rim = Contrast(window, (int)Math.Ceiling(at.Y + 3), from, to);
            Assert.True(rim > frosted + 0.1, $"the canvas keeps the rim clear: {rim:F2} 3 pt inside the edge against {frosted:F2} in the body");

            SwiftGlass.SetContainerSpacing(container, null);
            SwiftGlass.SetEffect(pill, null);
            Settle(window);
            var bare = Contrast(window, y, from, to);
            Assert.True(bare > 0.5, $"without the glass the pill's middle keeps only {bare:F2}, so the line does not measure the glass");
            window.Close();
        });
    });
}

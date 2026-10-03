using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.ComposerLights;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// DEVIATION OD-0089 (owner-ruled addition; ED-251): every composer wears the light the app's settings choose, and a change
// applies to all composers at once; the light sits over the composer group's glass, under the tab and the pill's content,
// on the pill's own rectangle; it follows the composer's state (ENGINEERING DECISION: typing while the draft holds
// anything, running while a turn runs). Built as ComposerViewTests builds the area (a runtime the model does not resolve,
// so the whole area lays out).
[Collection("Headless")]
public sealed class ComposerLightSlotTests(HeadlessSession headless)
{
    private static ThreadRuntimePart12Harness Harness()
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var h = new ThreadRuntimePart12Harness();
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        return h;
    }

    private static ThreadRuntime Unresolved(ThreadRuntimePart12Harness h)
    {
        var runtime = h.Main.OnMain(() => new ThreadRuntime(Guid.NewGuid(), h.App));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return runtime;
    }

    private static AvaloniaWindow Show(Control content, AppModel app)
    {
        var host = new StackPanel { Width = 1000, VerticalAlignment = VerticalAlignment.Bottom };
        host.Children.Add(content);
        AppEnvironment.SetAppModel(host, app);
        var window = new AvaloniaWindow { Content = host, Width = 1000, Height = 700 };
        window.Show();
        Settle(window);
        return window;
    }

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Pump(ThreadRuntimePart12Harness h, AvaloniaWindow window)
    {
        for (var round = 0; round < 4; round++) h.Main.OnMain(() => Settle(window));
    }

    private static T One<T>(ILogical root) where T : class => root.GetLogicalDescendants().OfType<T>().Single();

    [Fact]
    [Trait("Decision", "ED-251")] // OD-0089: the user's choice applies to every composer at once
    public void Every_composer_wears_the_apps_light_and_follows_a_change()
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            var r1 = Unresolved(h); var r2 = Unresolved(h);
            var first = h.Main.OnMain(() => new ComposerArea(r1, workingDirectory: null));
            var second = h.Main.OnMain(() => new ComposerArea(r2, workingDirectory: null));
            var both = h.Main.OnMain(() => new StackPanel { Children = { first, second } });
            var window = h.Main.OnMain(() => Show(both, h.App));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var a = One<ComposerLightView>(first); var b = One<ComposerLightView>(second);
                Assert.Equal(ComposerLightSettings.Default, a.Settings);
                Assert.Equal(ComposerLightSettings.Default, b.Settings);
                h.App.Settings.ComposerLight = ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura };
            });
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Equal(ComposerLightStyle.Aura, One<ComposerLightView>(first).Settings.Style);
                Assert.Equal(ComposerLightStyle.Aura, One<ComposerLightView>(second).Settings.Style);
                window.Close();
            });
        });
    }

    [Fact]
    [Trait("Decision", "ED-251")] // the light's composer is the pill: its rectangle, under the group (drawn before it), over the glass
    public void The_light_sits_on_the_pill_under_its_content()
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            var runtime = Unresolved(h);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var light = One<ComposerLightView>(area);
                var pill = One<ComposerView>(area);
                var frame = new Rect(pill.TranslatePoint(default, light)!.Value, pill.Bounds.Size);
                Assert.Equal(frame, light.ComposerRect);
                Assert.True(frame.Width > 100 && frame.Height >= 40);
                var slot = One<ComposerLightSlot>(area);
                var layers = (Panel)slot.Parent!;
                var group = layers.Children.Last();
                Assert.True(layers.Children.IndexOf(slot) < layers.Children.IndexOf(group), "the light is drawn before the tab and the pill");
                Assert.False(slot.IsHitTestVisible);
                Assert.NotNull(light.FadeBottom);
                window.Close();
            });
        });
    }

    [Fact]
    [Trait("Decision", "ED-251")] // the playground's states: at rest, typing (ENGINEERING DECISION: the draft holds anything)
    public void The_light_follows_the_draft()
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            var runtime = Unresolved(h);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Equal(ComposerLightPhase.Rest, One<ComposerLightView>(area).Phase);
                runtime.Draft = runtime.Draft with { Text = "Add a keyboard shortcut" };
            });
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Equal(ComposerLightPhase.Typing, One<ComposerLightView>(area).Phase);
                runtime.Draft = runtime.Draft with { Text = "" };
            });
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Equal(ComposerLightPhase.Rest, One<ComposerLightView>(area).Phase);
                window.Close();
            });
        });
    }

    [Fact]
    [Trait("Decision", "ED-251")] // logic.js MODELS: the Maker scheme's colours are the maker's liquid stops (OD-0063): heart and second dark, the deeper pair light; no thread's maker: Purple
    public void The_palette_is_the_makers_liquid_stops()
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            var runtime = Unresolved(h);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var p = One<ComposerLightView>(area).Palette;
                Assert.Equal(("#8C57F7", "#B48CFF", "#7A4BE0", "#8C57F7"), (p.MakerHeartDark, p.MakerSecondDark, p.MakerHeartLight, p.MakerSecondLight));
                Assert.Matches("^#[0-9A-F]{6}$", p.AccentDark);
                window.Close();
            });
        });
    }
}

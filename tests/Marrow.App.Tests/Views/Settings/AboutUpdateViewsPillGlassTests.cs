using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :638-682 (the ready pill) and :675 (.chromeGlassCapsule(), whose
// glass is .interactive(): Chrome.swift:180). F.GlassMaterial (GlassMaterial.cs) draws the interactive response: a light that follows the
// pointer, brighter while the button is held.
[Collection("Headless")]
public sealed class AboutUpdateViewsPillGlassTests(HeadlessSession headless)
{
    private static void Drain()
    {
        for (var i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-005")] // App/Views/Settings/AboutUpdateViews.swift:675,638-682: "Pressed: the shared chromeGlassCapsule surface provides the press response" on the ready pill
    [Trait("Decision", "ED-180")] // interactive glass: F.GlassMaterial renders hover and press (its rendering is Marrow.UI.Tests GlassMaterialTests.InteractiveGlassRespondsToHoverAndPress)
    public void A_press_on_the_ready_pill_reaches_its_interactive_glass_host() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], ""));
        typeof(UpdateChecker).GetProperty(nameof(UpdateChecker.Update), BindingFlags.Instance | BindingFlags.Public)!.SetValue(checker, AboutUpdateViewsProofLifecycleTests.Release());
        using var shared = UpdateChecker.OverrideShared(checker);
        var boundary = new FakeUpdateInstallBoundary { VersionToReturn = "9.0.0" }; var updater = new AppUpdater(boundary);
        var ambient = (Ambient<AppUpdater>)typeof(AppUpdater).GetField("running", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        using var updaterScope = ambient.Override(updater); // a stray release would reach a fake boundary, never a real install
        var pill = new AboutVersionPill();
        var window = new Avalonia.Controls.Window { Width = 300, Height = 100, UseLayoutRounding = false, RequestedThemeVariant = ThemeVariant.Dark,
            Content = new Border { Child = pill, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(20) } };
        window.Show(); Drain();
        // :675 .chromeGlassCapsule(): the glass is the interactive capsule (Chrome.swift:180), on the border that sits behind the button.
        var fill = Assert.Single(pill.GetVisualDescendants().OfType<Border>(), b => SwiftGlass.GetEffect(b) is { IsInteractive: true });
        var host = Assert.IsType<Panel>(fill.GetVisualParent()); // GlassMaterial lights the region from presses on this host: the fill takes none itself
        Assert.False(fill.IsHitTestVisible);
        var presses = 0; var releases = 0;
        host.AddHandler(InputElement.PointerPressedEvent, (_, _) => presses++, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerReleasedEvent, (_, _) => releases++, RoutingStrategies.Tunnel, handledEventsToo: true);
        var origin = pill.TranslatePoint(default, window)!.Value;
        var centre = new Point(origin.X + pill.Bounds.Width / 2, origin.Y + pill.Bounds.Height / 2);
        window.MouseMove(centre, RawInputModifiers.None); Drain();
        window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None); Drain();
        Assert.Equal(1, presses); // the press reaches the host the material listens on (the hit-testable button inside it)
        // Let go outside: no click, so no install starts and the ready face stays.
        window.MouseMove(new Point(290, 90), RawInputModifiers.None); Drain();
        window.MouseUp(new Point(290, 90), MouseButton.Left, RawInputModifiers.None); Drain();
        Assert.Empty(boundary.Calls); Assert.Contains(pill.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Update & restart");
        window.Close();
    });
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class ShortcutsSettingsPageReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-SETTINGS-218")] // App/Views/Settings/ShortcutsSettingsPage.swift:596-619; ED-099 scan codes
    public void Real_key_events_record_the_physical_N_as_031_even_when_the_logical_key_is_A() => headless.OnUiThread(() =>
    {
        using var rt = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var anchor = new TextBox();
        var w = Show(anchor);
        anchor.Focus();
        var session = new ShortcutRecordingSession();
        KeyChord? saved = null;
        session.Begin(AppShortcut.NewThread, [], ChordModifiers.None, () => { }, chord => saved = chord);
        var input = new ShortcutInputSource(anchor);
        input.Attach(session);
        w.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.N, null);
        Assert.Equal("N", session.Caps.Last().Symbol);
        w.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.N, null);
        w.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
        Dispatcher.UIThread.RunJobs();
        rt.Advance(TimeSpan.FromSeconds(0.3));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new KeyChord(0x31, ChordModifiers.Control), saved);
        input.Detach();
        w.Close();
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Req", "PR-SETTINGS-217")] // App/Views/Settings/ShortcutsSettingsPage.swift:430,437,464-465
    public void Caps_animate_with_the_source_spring_or_reduced_motion_ease_out(bool reduced) => headless.OnUiThread(() =>
    {
        using var rt = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var session = new ShortcutRecordingSession();
        session.Begin(AppShortcut.NewThread, [], ChordModifiers.Control, () => { }, _ => { });
        var c = new ShortcutRecordingPopoverContent(session);
        EnvironmentValues.SetAccessibilityReduceMotion(c, reduced);
        var w = Show(c);
        Tick(rt, frames, 1);
        session.HandleFlagsChanged(ChordModifiers.Control | ChordModifiers.Alt);
        Dispatcher.UIThread.RunJobs();
        var cap = c.GetVisualDescendants().OfType<KeycapView>().Single(v => v.RepresentedCap.Id == "option");
        Assert.Equal(0, cap.Opacity);
        Tick(rt, frames, 0.06);
        var curve = reduced ? Animation.EaseOut(0.12) : Animation.Spring(0.3, 0.8, 0);
        Assert.Equal(curve.Progress(0.06), cap.Opacity, 7);
        Tick(rt, frames, 1);
        Assert.Equal(1, cap.Opacity);
        w.Close();
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-217")] // App/Views/Settings/ShortcutsSettingsPage.swift:401-415,465
    public void Verdict_ink_and_lip_fade_over_014_seconds() => headless.OnUiThread(() =>
    {
        using var rt = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var session = new ShortcutRecordingSession();
        session.Begin(AppShortcut.NewThread, [], ChordModifiers.Control, () => { }, _ => { });
        var c = new ShortcutRecordingPopoverContent(session);
        var w = Show(c);
        Tick(rt, frames, 1);
        var ctrl = c.GetVisualDescendants().OfType<KeycapView>().Single(v => v.RepresentedCap.Id == "control");
        var ink = ctrl.GetVisualDescendants().OfType<TextBlock>().First();
        var before = Assert.IsAssignableFrom<ISolidColorBrush>(ink.Foreground).Color;
        session.HandleKeyDown(0x31, ChordModifiers.Control, false);
        Dispatcher.UIThread.RunJobs();
        var target = KeycapColors.Accepted.Resolve(ctrl.ActualThemeVariant);
        Assert.Equal(before, Assert.IsAssignableFrom<ISolidColorBrush>(ink.Foreground).Color);
        Tick(rt, frames, 0.07);
        Assert.Equal(Mix(before, target, Animation.EaseOut(0.14).Progress(0.07)),
            Assert.IsAssignableFrom<ISolidColorBrush>(ink.Foreground).Color);
        Tick(rt, frames, 0.07);
        Assert.Equal(target, Assert.IsAssignableFrom<ISolidColorBrush>(ink.Foreground).Color);
        var lip = ctrl.GetVisualDescendants().OfType<Border>().Single(b => b.Height == 1.5);
        Assert.Equal(KeycapColors.Accepted.Opacity(0.9).Resolve(ctrl.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(lip.Background).Color);
        w.Close();
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-215")] // App/Views/Settings/ShortcutsSettingsPage.swift:184-185
    public void Chord_changes_use_the_03_response_08_damping_spring_instead_of_hover() => headless.OnUiThread(() =>
    {
        using var rt = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var store = FreshStore();
        using var shared = ShortcutStore.Shared.Override(store);
        var c = new ShortcutRecorder(AppShortcut.ShowMainWindow);
        var w = Show(c);
        Tick(rt, frames, 1);
        var surface = c.GetVisualDescendants().OfType<Border>().First(b => b.Background is SolidColorBrush);
        var from = Chrome.Overlay(0.07).Resolve(surface.ActualThemeVariant);
        var to = Chrome.Overlay(0.24).Resolve(surface.ActualThemeVariant);
        store.Set(new KeyChord(0x31, ChordModifiers.Control), AppShortcut.ShowMainWindow);
        Dispatcher.UIThread.RunJobs();
        Tick(rt, frames, 0.1);
        Assert.Equal(Mix(from, to, Animation.Spring(0.3, 0.8, 0).Progress(0.1)),
            Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
        w.Close();
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-043")] // App/Views/Settings/ShortcutsSettingsPage.swift:24 HStack(alignment: .center, spacing: 8)
    public void Header_title_sits_on_the_centre_line_of_its_20_point_badge() => headless.OnUiThread(() =>
    {
        using var rt = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var header = new ShortcutSectionHeader("Threads", "bubble.left", null);
        var window = Show(header);
        var row = header.GetVisualDescendants().OfType<StackPanel>().Single();
        var badge = header.GetVisualDescendants().OfType<SidebarIconBadge>().Single();
        var label = header.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Threads");
        // The 16-point title is shorter than the 20-point badge; the HStack (alignment: .center) puts it on the badge's centre line.
        Assert.Equal(label.DesiredSize.Height, label.Bounds.Height, 0.5);
        Assert.Equal(badge.Bounds.Center.Y, label.Bounds.Center.Y, 0.5);
        Assert.Same(row, label.Parent);
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-043")] // App/Views/Settings/ShortcutsSettingsPage.swift:24-35,272-276
    [Trait("Req", "UI-SETTINGS-047")] // App/Views/Settings/ShortcutsSettingsPage.swift:450-481
    public void Header_and_action_pills_have_source_geometry_colors_and_instant_press_scales() => headless.OnUiThread(() =>
    {
        using var rt = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var h = new ShortcutSectionHeader("Threads", "bubble.left", null);
        Assert.Equal(22, Assert.IsType<Grid>(h.Child).Height);
        var row = h.GetVisualDescendants().OfType<StackPanel>().Single();
        Assert.Equal(8, row.Spacing);
        var label = h.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Threads");
        Assert.Equal(13, label.FontSize);
        Assert.Equal(FontWeight.SemiBold, label.FontWeight);
        Assert.Equal(Chrome.SecondaryText.Resolve(label.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
        var badge = h.GetVisualDescendants().OfType<SidebarIconBadge>().Single();
        Assert.Equal(20, Assert.IsType<Panel>(badge.Child).Width);
        Assert.Equal(20, Assert.IsType<Panel>(badge.Child).Height);
        var reset = new ShortcutResetPill(AppShortcutSection.Threads);
        var resetWindow = Show(reset);
        var resetBorder = reset.GetVisualDescendants().OfType<Border>().Single(b => b.Height == 22);
        Assert.Equal(new Thickness(10, 0), resetBorder.Padding);
        resetWindow.Close();
        var session = new ShortcutRecordingSession();
        session.Begin(AppShortcut.NewThread, [
            new ShortcutRecordingAction("clear", "Clear", true, () => { }),
            new ShortcutRecordingAction("reset", "Reset to default", false, () => { }),
        ], ChordModifiers.None, () => { }, _ => { });
        var c = new ShortcutRecordingPopoverContent(session);
        var w = Show(c);
        foreach (var title in new[] { "Clear", "Reset to default" })
        {
            var text = c.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == title);
            var expected = title == "Clear" ? KeycapColors.Rejected : Chrome.PrimaryText;
            Assert.Equal(expected.Resolve(text.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            var pad = text.GetVisualAncestors().OfType<Border>().First();
            Assert.Equal(26, pad.Height);
            Assert.Equal(new Thickness(12, 0), pad.Padding);
            var surface = text.GetVisualAncestors().OfType<Control>().First(v => v.RenderTransform is ScaleTransform);
            Assert.True(((ScaleTransform)surface.RenderTransform!).Transitions is null or { Count: 0 });
        }
        w.Close();
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Req", "PR-SETTINGS-216")] // App/Views/Settings/ShortcutsSettingsPage.swift:242-251
    public void Recorder_set_and_unset_rest_hover_press_fills_and_ink_match_the_source(bool set) => headless.OnUiThread(() =>
    {
        using var rt = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var popupScope = Presentation.Presenter.Override(new RecordingPresenter());
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var c = new ShortcutRecorder(set ? AppShortcut.NewThread : AppShortcut.ShowMainWindow);
        var w = Show(c);
        Tick(rt, frames, 1);
        var surface = c.GetVisualDescendants().OfType<Border>().First(b => b.Background is SolidColorBrush);
        var text = c.GetVisualDescendants().OfType<TextBlock>().First();
        Assert.Equal(Chrome.Overlay(set ? 0.24 : 0.07).Resolve(surface.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
        Assert.Equal((set ? Chrome.PrimaryText : Chrome.SecondaryText).Resolve(text.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
        var point = surface.TranslatePoint(new Point(84, 12), w)!.Value;
        w.MouseMove(point);
        Dispatcher.UIThread.RunJobs();
        Tick(rt, frames, 1);
        Assert.Equal(Chrome.Overlay(set ? 0.30 : 0.11).Resolve(surface.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
        w.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Tick(rt, frames, 1);
        Assert.Equal(Chrome.Overlay(set ? 0.34 : 0.14).Resolve(surface.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
        w.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Tick(rt, frames, 1);
        Assert.Equal(Chrome.Accent.Opacity(0.26).Resolve(surface.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
        w.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Tick(rt, frames, 1);
        Assert.Equal(Chrome.Accent.Opacity(0.34).Resolve(surface.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
        w.Close();
    });

    private static ShortcutStore FreshStore() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), true)!;
    private static Avalonia.Controls.Window Show(Control c)
    {
        var w = new Avalonia.Controls.Window { Width = 500, Height = 300, Content = c };
        w.Show(); Dispatcher.UIThread.RunJobs(); return w;
    }
    private static void Tick(TestRuntime rt, ManualFrameSource frames, double seconds)
    { rt.Advance(TimeSpan.FromSeconds(seconds)); frames.Advance(TimeSpan.FromSeconds(seconds)); Dispatcher.UIThread.RunJobs(); }
    private static Color Mix(Color a, Color b, double p)
    {
        static byte C(byte a, byte b, double p) => (byte)Math.Clamp(Math.Round(a + (b - a) * p, MidpointRounding.AwayFromZero), 0, 255);
        return Color.FromArgb(C(a.A,b.A,p),C(a.R,b.R,p),C(a.G,b.G,p),C(a.B,b.B,p));
    }
}

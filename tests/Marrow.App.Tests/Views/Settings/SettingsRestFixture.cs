using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HostWindow = Avalonia.Controls.Window;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Settings;

// Independent SettingsView-rest harness. All input uses Avalonia's hit testing.
public abstract class SettingsRestFixture(HeadlessSession headless)
{
    protected static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    protected static T[] Nodes<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();
    protected static string[] Text(Control root) => Nodes<TextBlock>(root).Select(t => t is MiddleTruncatingText m ? m.FullText : t.Text ?? "").ToArray();
    protected static ChromeRow Row(Control root, string title) => Nodes<ChromeRow>(root).Single(r => Text(r).First() == title);
    protected static void Drain(HostWindow window) { for (var i = 0; i < 5; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
    protected static void Tick(AppModelHarness h, double ms) { h.Main.AdvanceMilliseconds(ms); ((ManualFrameSource)FrameClock.Source.Current).Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); }
    protected static Point At(Control control, HostWindow window, Point? local = null) => control.TranslatePoint(local ?? new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    /// <summary>OD-0134 (Ignis): with Ignis listed, the 580 pt window's sidebar scrolls to reach its last rows, as a person scrolls it. Scrolls the row into view and waits out the
    /// sidebar's 150 ms scroll freeze (SettingsScrollTracker), so the next pointer event meets the row.</summary>
    protected static void Reveal(HostWindow window, AppModelHarness h, Control control) { control.BringIntoView(); Drain(window); Tick(h, 160); Drain(window); }
    protected static void Hover(HostWindow window, Control control, Point? local = null) { window.MouseMove(At(control, window, local)); Drain(window); Assert.True(control.IsPointerOver); }
    protected static void Click(HostWindow window, Control control, Point? local = null)
    {
        var point = At(control, window, local);
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Drain(window);
    }
    protected static void Seed(AppModel model, Marrow.Core.Models.ProviderKind provider, ProviderStatus status) => typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers, model.Providers.Statuses.Mutating(map => map[provider] = status));
    protected void With(Func<Control> make, Action<Control, AppModel, AppModelHarness, HostWindow> check, Action<AppModel>? seed = null, Action<AppModelHarness>? before = null, double height = 1600, bool nativePopup = false)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var wallpaper = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!);
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            using var updates = UpdateChecker.OverrideShared(new UpdateChecker(AppInfo.Current, [], ""));
            using var popovers = Presentation.Presenter.Override(nativePopup ? new SettingsRestPopupPresenter() : new RecordingPresenter());
            before?.Invoke(h); var model = new AppModel(); seed?.Invoke(model);
            var host = new Border { Padding = new Thickness(40) }; AppEnvironment.SetAppModel(host, model);
            var view = make(); host.Child = view;
            var window = new HostWindow { Width = 900, Height = height, Content = host, Background = Brushes.Black };
            window.Template = new FuncControlTemplate<HostWindow>((owner, scope) => Layers(new VisualLayerManager
            {
                EnableOverlayLayer = true, EnableAdornerLayer = true,
                Child = new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] }.RegisterInNameScope(scope),
            }));
            try { window.Show(); Drain(window); check(view, model, h, window); }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); SettingsNavigation.Shared.RequestedPage = null; }
        });
    }
    private static VisualLayerManager Layers(VisualLayerManager manager)
    {
        // Avalonia 12.1.2 exposes its window popup layer enablement internally; match its native window template.
        typeof(VisualLayerManager).GetProperty("EnablePopupOverlayLayer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, true);
        return manager;
    }
    protected static void Ink(Control control, IBrush? brush, SwiftColor expected) => Assert.Equal(expected.Resolve(control.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
    protected static void Font(TextBlock text, double size, FontWeight weight = default)
    {
        Assert.Equal(size, text.FontSize); Assert.Equal(weight == default ? FontWeight.Normal : weight, text.FontWeight);
        Assert.Equal(SwiftFont.System(size).Resolve().FontFamily, text.FontFamily);
    }
    protected static void CheckRow(ChromeRow row, string title, string? detail)
    {
        // Chrome.swift:1062-1078: title/body 13, detail/subheadline 11, spacing 2, padding 16/11/10/11.
        var pad = Assert.IsType<Border>(row.Child); Assert.Equal(new Thickness(16, 11, 10, 11), pad.Padding);
        var grid = Assert.IsType<Grid>(pad.Child); var labels = Assert.IsType<StackPanel>(grid.Children[0]); Assert.Equal(2, labels.Spacing);
        var heading = Assert.IsType<TextBlock>(labels.Children[0]); Assert.Equal(title, heading.Text); Font(heading, 13); Ink(heading, heading.Foreground, Chrome.PrimaryText);
        Assert.Equal(detail is null ? 1 : 2, labels.Children.Count);
        if (detail is not null) { var subtitle = Assert.IsType<TextBlock>(labels.Children[1]); Assert.Equal(detail, subtitle.Text); Font(subtitle, 11); Ink(subtitle, subtitle.Foreground, Chrome.SecondaryText); Assert.Equal(TextWrapping.Wrap, subtitle.TextWrapping); }
        var accessory = grid.Children[1]; Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, accessory.HorizontalAlignment); Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, accessory.VerticalAlignment);
    }
    protected static Control Open(Popup popup)
    {
        ((EventHandler?)typeof(Popup).GetField("Opened", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup))?.Invoke(popup, EventArgs.Empty);
        return popup.Child!;
    }
}

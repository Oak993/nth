using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Palette;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppCommands.swift (B3 52bc792), :12-15,95-98.
// OD-0008 C3: menu geometry/type/colour are not rendered; real keys and palette pointer input reach production dispatch.
[Collection("Headless")]
public sealed class AppCommandsProofTests(HeadlessSession headless)
{
    private static ShortcutStore FreshStore() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!;
    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }
    private static void NoMenu(AvaloniaWindow window)
    {
        Assert.Null(NativeMenu.GetMenu(window)); // OD-0008 C3: no menu bar.
        Assert.Empty(window.GetVisualDescendants().OfType<Menu>());
    }
    private static void Press(AvaloniaWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers)
    {
        window.Activate();
        window.KeyPress(key, modifiers, physical, keySymbol: null);
        window.KeyRelease(key, modifiers, physical, keySymbol: null);
    }

    [Fact]
    [Trait("Req", "PR-WIN-090")] // App/AppCommands.swift:12-15; Window/WindowManager.swift:32-38,40-55
    [Trait("Req", "UI-LAUNCH-007")] // App/AppCommands.swift:12-15; Views/Palette/CommandPalette.swift:176-178
    [Trait("Req", "UI-WIN-006")] // App/AppCommands.swift:12-15; OD-0008 C3,B7
    public void Settings_keys_and_real_palette_click_preserve_the_page_center_and_focus_the_same_window() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h =>
        {
            var project = AppModelSamples.Project("P", h.NewFolder("p"));
            h.WriteLibrary([project], [AppModelSamples.Thread(project)]);
        });
        using var store = ShortcutStore.Shared.Override(FreshStore());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var wallpaper = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!);
        var appearance = Application.Current!.RequestedThemeVariant;
        var theme = ThemeManager.Current;
        var requested = SettingsNavigation.Shared.RequestedPage;
        AvaloniaWindow settings = null!;
        AvaloniaWindow main = null!;
        SettingsView view = null!;
        TextBox input = null!;
        CommandPalette palette = null!;
        try
        {
            launch.Harness.Main.OnMain(() =>
            {
                view = new SettingsView();
                launch.Manager.SettingsContent = () => view;
                launch.Manager.ShowMain();
                main = LaunchHarness.WindowOf(launch.MainContent)!;
                main.Position = new PixelPoint(100, 80); // source :48-49: centered by parent.Center minus half own frame.
                launch.Manager.ShowSettings(SettingsPage.Archive);
                settings = LaunchHarness.WindowOf(view)!;
                Settle(settings!);
                var search = Assert.Single(LogicalQuery.SelfAndDescendants<SidebarSearchField>(view));
                input = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(search));
                input.Text = "archive";
                Settle(settings!);
                settings.Close();
                Assert.False(settings!.IsVisible);
                main.Position = new PixelPoint(120, 100); // Move after initial construction: each chord must re-center.

                launch.Manager.Model.SelectedThreadID = launch.Manager.Model.SidebarThreads[0].Id;
                // Source :14 takes the live store; default Shortcuts.swift:234 is Command-comma, Ctrl+, (ED-050/099).
                Press(main, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);
                Settle(settings!);
                Assert.True(settings.IsVisible);
                Assert.True(settings!.IsActive); // WindowManager.swift:37,58-59 focuses the requested window.
                main.Activate();
                // Headless Activate does not deliver deactivation to the other top-level. Supply the documented
                // OS callback to establish Settings visible but inactive; production must activate it again.
                var deactivated = (Action)typeof(Avalonia.Platform.IWindowBaseImpl)
                    .GetProperty("Deactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(settings.PlatformImpl)!;
                deactivated();
                Assert.False(settings.IsActive);
                Press(main, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);
                Assert.True(settings.IsActive); // Also front an already-visible Settings window, :37,58-59.
                Assert.Same(settings, LaunchHarness.WindowOf(view));
                Assert.Equal("archive", input.Text); // :13 requests no page; cached view keeps page/search.
                Assert.Contains(view.GetVisualDescendants().OfType<Control>(), control => Avalonia.Automation.AutomationProperties.GetName(control) == "No archived threads"); // SettingsView.swift:201-204: actual Archive empty page, not its sidebar label.
                Assert.Null(SettingsNavigation.Shared.RequestedPage);
                Assert.Equal(new PixelPoint(390, 240), settings!.Position); // (120+(1320-780)/2,100+(860-580)/2), :48-49.
                NoMenu(main!); NoMenu(settings!);
                settings.Close();

                launch.Manager.Model.SelectedThreadID = null;
                var rebound = new KeyChord(0x15, ChordModifiers.Control | ChordModifiers.Shift); // US Y position, ED-099.
                ShortcutStore.Shared.Current.Set(rebound, AppShortcut.OpenSettings);
                Press(main, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);
                Assert.False(settings.IsVisible); // source :14 relinquishes the old chord.
                Press(main, Key.Y, PhysicalKey.Y, RawInputModifiers.Control | RawInputModifiers.Shift);
                Assert.True(settings.IsVisible);
                Assert.True(settings.IsActive);
                settings.Close();

                main.Position = new PixelPoint(140, 120); // Palette opens must re-center the same cached window too.
                palette = new CommandPalette();
                launch.MainContent.Child = palette;
                launch.Manager.Model.IsCommandPalettePresented = true;
                Settle(main);
                palette.QueryField.Text = "Settings";
            }); // let the manual main actor run the query observation before reading/clicking its row.
            launch.Harness.Main.OnMain(() =>
            {
                Settle(main!);
                var row = Assert.Single(palette.RowsPanel.Children);
                Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(row), text => text.Text == "Settings"); // :176
                Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(row), text => text.Text == ShortcutStore.Label(AppShortcut.OpenSettings)); // :177
                var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), main!)!.Value;
                main!.MouseMove(point);
                main!.MouseDown(point, MouseButton.Left);
                main!.MouseUp(point, MouseButton.Left); // real hit testing, palette :86-89,201-206.
                Settle(settings!);
                Assert.False(launch.Manager.Model.IsCommandPalettePresented);
                Assert.True(settings.IsVisible);
                Assert.True(settings.IsActive);
                Assert.Same(settings, LaunchHarness.WindowOf(view));
                Assert.Equal("archive", input.Text);
                Assert.Contains(view.GetVisualDescendants().OfType<Control>(), control => Avalonia.Automation.AutomationProperties.GetName(control) == "No archived threads"); // SettingsView.swift:201-204: actual Archive empty page, not its sidebar label.
                Assert.Equal(new PixelPoint(410, 260), settings.Position); // (140+(1320-780)/2,120+(860-580)/2), WindowManager.swift:48-49.
                NoMenu(main); NoMenu(settings);
            });
        }
        finally
        {
            launch.Harness.Main.OnMain(() =>
            {
                if (settings is not null) { settings.Content = null; settings.Hide(); }
                if (main is not null) { main.Content = null; main.Hide(); }
                SettingsNavigation.Shared.RequestedPage = requested;
                ThemeManager.Current = theme;
                Application.Current!.RequestedThemeVariant = appearance;
                Dispatcher.UIThread.RunJobs();
            });
        }
    });

    [Theory]
    [InlineData(1, Key.D1, PhysicalKey.Digit1, 0x02)]
    [InlineData(2, Key.D2, PhysicalKey.Digit2, 0x03)]
    [InlineData(3, Key.D3, PhysicalKey.Digit3, 0x04)]
    [InlineData(4, Key.D4, PhysicalKey.Digit4, 0x05)]
    [InlineData(5, Key.D5, PhysicalKey.Digit5, 0x06)]
    [InlineData(6, Key.D6, PhysicalKey.Digit6, 0x07)]
    [InlineData(7, Key.D7, PhysicalKey.Digit7, 0x08)]
    [InlineData(8, Key.D8, PhysicalKey.Digit8, 0x09)]
    [InlineData(9, Key.D9, PhysicalKey.Digit9, 0x0A)]
    [Trait("Req", "PR-WIN-110")] // App/AppCommands.swift:95-98; AppModel.swift:1119-1123; Shortcuts.swift:251-257; OD-0008 B8
    public void Every_digit_is_named_enabled_hard_coded_reserved_and_selects_its_sidebar_thread_from_both_production_windows(int number, Key key, PhysicalKey physical, long scanCode) => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h =>
        {
            var project = AppModelSamples.Project("P", h.NewFolder("p"));
            h.WriteLibrary([project], Enumerable.Range(1, 9).Select(_ => AppModelSamples.Thread(project)).ToArray());
        });
        using var store = ShortcutStore.Shared.Override(FreshStore());
        launch.Harness.Main.OnMain(() =>
        {
            var model = launch.Manager.Model;
            var order = model.SidebarThreads.Select(thread => thread.Id).ToArray();
            var commands = new AppCommands(model);
            var items = commands.Body.Skip(20).Take(9).ToArray(); // :89-98: immediately after Run top script.
            Assert.Equal(9, items.Length);
            for (var digit = 1; digit <= 9; digit++)
            {
                Assert.Equal("Thread " + digit, items[digit - 1].Title); // :96 "Thread \(number)".
                Assert.Equal(new KeyboardShortcut(new KeyEquivalent(digit.ToString(System.Globalization.CultureInfo.InvariantCulture)), ChordModifiers.Control), items[digit - 1].Shortcut);
                Assert.False(items[digit - 1].IsDisabled); // :95-98 has no disabled modifier.
            }
            var chord = new KeyChord(scanCode, ChordModifiers.Control);
            Assert.Equal(items[number - 1].Shortcut, chord.KeyboardShortcut);
            Assert.Equal("Windows or nth already uses this chord.", ShortcutStore.Shared.Current.Refusal(chord, AppShortcut.ShowMainWindow)); // :320-334 reserved test, OD-0008 B8.
            ShortcutStore.Shared.Current.Set(new KeyChord(0x15, ChordModifiers.Control | ChordModifiers.Shift), AppShortcut.NextThread);
            Assert.Equal(items[number - 1].Shortcut, new AppCommands(model).Body[20 + number - 1].Shortcut);
            launch.Manager.ShowMain();
            launch.Manager.ShowSettings();
            var main = LaunchHarness.WindowOf(launch.MainContent)!;
            var settings = LaunchHarness.WindowOf(launch.SettingsContent)!;
            try
            {
                model.SelectedThreadID = null;
                Press(settings, key, physical, RawInputModifiers.Control);
                Assert.Equal(order[number - 1], model.SelectedThreadID); // :1119-1123 uses sidebar order[number-1].
                model.SelectedThreadID = null;
                var numbers = new List<long>();
                var threadWindow = Assert.IsType<ThreadWindow>(main);
                var monitorAction = threadWindow.SelectThreadNumber;
                Assert.NotNull(monitorAction);
                threadWindow.SelectThreadNumber = digit => { numbers.Add(digit); monitorAction(digit); };
                Press(main, key, physical, RawInputModifiers.Control);
                Assert.Equal([number], numbers); // ThreadWindow.swift:5-19 local monitor runs before table dispatch.
                Assert.Equal(order[number - 1], model.SelectedThreadID); // ThreadWindow.swift:5-23 local digit monitor.
                NoMenu(main); NoMenu(settings);
            }
            finally { settings.Hide(); main.Hide(); Dispatcher.UIThread.RunJobs(); }
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-110")] // App/AppCommands.swift:95-98; AppModel.swift:1119-1123
    public void A_digit_past_the_end_preserves_selection_in_both_production_windows_even_when_the_list_is_empty() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h =>
        {
            var project = AppModelSamples.Project("P", h.NewFolder("p"));
            h.WriteLibrary([project], [AppModelSamples.Thread(project), AppModelSamples.Thread(project)]);
        });
        using var store = ShortcutStore.Shared.Override(FreshStore());
        launch.Harness.Main.OnMain(() =>
        {
            var model = launch.Manager.Model;
            var first = model.SidebarThreads[0].Id;
            launch.Manager.ShowMain(); launch.Manager.ShowSettings();
            var main = LaunchHarness.WindowOf(launch.MainContent)!;
            var settings = LaunchHarness.WindowOf(launch.SettingsContent)!;
            try
            {
                foreach (var window in new[] { main, settings })
                {
                    model.SelectedThreadID = first;
                    Press(window, Key.D9, PhysicalKey.Digit9, RawInputModifiers.Control);
                    Assert.Equal(first, model.SelectedThreadID); // guard order.indices.contains(number-1).
                }
                foreach (var thread in model.Threads.ToArray()) model.Archive(thread.Id);
                Assert.Empty(model.SidebarThreads);
                model.SelectedThreadID = null;
                foreach (var window in new[] { main, settings })
                {
                    Press(window, Key.D1, PhysicalKey.Digit1, RawInputModifiers.Control);
                    Assert.Null(model.SelectedThreadID);
                    NoMenu(window);
                }
            }
            finally { settings.Hide(); main.Hide(); Dispatcher.UIThread.RunJobs(); }
        });
    });
}

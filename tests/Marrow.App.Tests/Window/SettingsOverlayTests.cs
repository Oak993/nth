using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Common;
using Xunit;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// OD-0151 (ED-NEW-settingsinapp-1): Settings opens as a panel inside the main window, not as a window of its own. The owner, 2026-10-11:
// "clicking outside of settings window should close it. and settings window shouldn't open a second window but rather be part of the app
// itself?" and "Like claude's native app does?".
[Collection("Headless")]
[Trait("Decision", "OD-0151")]
[Trait("Decision", "ED-NEW-settingsinapp-1")]
public sealed class SettingsOverlayTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    // Opens Settings on a rig's clocks and runs its open to the end.
    private static (HostWindow Window, SettingsOverlay Overlay) OpenSettled(WindowManager manager, SettingsRiseRig rig, SettingsPage? page = null)
    {
        manager.ShowSettings(page);
        Dispatcher.UIThread.RunJobs();
        var window = Settings(manager);
        Drain(window);
        Settle(rig, window);
        return (window, Overlay(manager));
    }

    // Half a second of the rig's clocks, each frame laid out, then the window drawn: these tests read state, not pixels, and drawing the
    // whole window on every frame was most of their time.
    private static void Settle(SettingsRiseRig rig, HostWindow window)
    {
        for (var i = 0; i < 30; i++)
        {
            rig.Frame(1.0 / 60);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        Drain(window);
    }

    private static void PressAt(HostWindow window, Point point)
    {
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Drain(window);
    }

    [Fact]
    public void Opening_Settings_opens_no_window_and_shows_the_panel_in_the_main_window_at_the_requested_page() => WithManager((_, manager) =>
    {
        manager.ShowMain();
        var main = MainOf(manager);
        Drain(main);
        var opened = new List<HostWindow>();
        using var watch = HostWindow.WindowOpenedEvent.AddClassHandler<HostWindow>((window, _) => opened.Add(window));
        using var rig = new SettingsRiseRig();

        var (window, overlay) = OpenSettled(manager, rig, SettingsPage.Archive);

        Assert.Empty(opened); // no second window, so no second taskbar button
        Assert.Same(main, window);
        Assert.True(manager.IsSettingsOpen);
        Assert.True(overlay.IsVisible);
        Assert.Same(main, TopLevel.GetTopLevel(overlay));
        var view = View(manager);
        Assert.Same(main, TopLevel.GetTopLevel(view));
        Assert.Contains(view.GetVisualDescendants().OfType<Control>(), control => AutomationProperties.GetName(control) == "No archived threads");
        Assert.Null(SettingsNavigation.Shared.RequestedPage);
        // The panel lies over the window's content, under its caption buttons.
        var root = Assert.IsAssignableFrom<Panel>(main.Content);
        var at = root.Children.IndexOf(overlay);
        Assert.True(at > 0);
        Assert.Equal(root.Children.IndexOf(WindowChrome.CaptionLayer(main)), at + 1);
        // Asking for another page while it is open moves it there and keeps it open.
        manager.ShowSettings(SettingsPage.About); Drain(main);
        Assert.True(manager.IsSettingsOpen);
        Assert.Empty(opened);
        Assert.Contains(Nodes<AboutSettingsPage>(view), page => page.IsEffectivelyVisible);
    });

    [Fact]
    public void Settings_asked_for_before_the_main_window_shows_the_main_window_with_the_panel_in_it() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, _) = OpenSettled(manager, rig, SettingsPage.About);
        Assert.Same(manager.MainWindow, window);
        Assert.True(window.IsVisible);
        Assert.IsType<ThreadWindow>(window);
    });

    // ED-NEW-settingsinapp-2: the panel sits inset from the window as Claude's does (the owner's references, 100 % scaling): about 22 beside
    // and 50 above and below it in a 1296 x 1125 window, centred at about 1680 x 1262 in a maximized 2560 x 1393 one.
    [Theory]
    // ED-NEW-settingsinapp-4: in the 1296 x 1125 window the least room above and below binds: the caption buttons are drawn at the base scale
    // (1.3), so "just under the caption buttons" is MinimumVerticalMargin x 1.3 = 64.6 px, and the panel is 1125 - 2 x 64.6 = 995.8 tall.
    [InlineData(1296, 1125, 1246, 1258, 995, 997)]
    [InlineData(2560, 1393, 1660, 1700, 1258, 1268)]
    public void The_panel_is_inset_from_the_window_as_Claude_s_is_and_its_content_reflows_without_breaking_words(double width, double height, double leastWidth, double mostWidth, double leastHeight, double mostHeight) => WithManager((_, manager) =>
    {
        using var refused = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(refused);
        using var rig = new SettingsRiseRig();
        var (window, overlay) = OpenSettled(manager, rig, SettingsPage.General);
        window.Width = width; window.Height = height; Drain(window);
        var card = overlay.Card.Bounds;
        // ED-NEW-settingsinapp-4: the owner's measurements are the window's pixels, and the panel is laid out in points drawn at the base
        // scale (ED-NEW-textsize-2), so its frame is checked as drawn on the window.
        var drawn = ZoomDrawn.On(overlay.Card, window);
        Assert.InRange(drawn.Width, leastWidth, mostWidth);
        Assert.InRange(drawn.Height, leastHeight, mostHeight);
        Assert.InRange(drawn.Width - card.Width * AppZoom.BaseScale, -0.01, 0.01); // the panel's points at the base scale
        var origin = overlay.Card.TranslatePoint(default, window)!.Value;
        // Centred both ways, to the layout's whole point: the panel is laid out in points, each drawn at the base scale (ED-NEW-settingsinapp-4).
        Assert.InRange(origin.X - (width - drawn.Width) / 2, -AppZoom.BaseScale, AppZoom.BaseScale);
        Assert.InRange(origin.Y - (height - drawn.Height) / 2, -AppZoom.BaseScale, AppZoom.BaseScale);
        Assert.True(origin.X >= SettingsOverlay.SideMargin - 0.001);
        // Under the caption buttons, which are drawn at the same scale (WindowChrome.SetCaptionScale).
        Assert.True(origin.Y >= SettingsOverlay.MinimumVerticalMargin * AppZoom.BaseScale - 0.001, $"the panel starts {origin.Y} below the top, under the caption buttons");
        var view = View(manager);
        Assert.Equal(card.Size, view.Bounds.Size); // the content fills the panel: it reflows, nothing scales it
        Assert.Null(view.RenderTransform);
        foreach (var page in new[] { SettingsPage.General, SettingsPage.Appearance, SettingsPage.Providers, SettingsPage.About })
        {
            manager.ShowSettings(page); Drain(window); Settle(rig, window);
            AssertNoWordBroken(view, page);
        }
        // The page list: one line a label, cut with an ellipsis when it does not fit, never wrapped.
        var rows = Nodes<SidebarRow>(view).Where(row => row.IsEffectivelyVisible).ToArray();
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            var label = row.GetVisualDescendants().OfType<TextBlock>().First(text => !string.IsNullOrEmpty(text.Text));
            Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            Assert.Equal(TextWrapping.NoWrap, label.TextWrapping);
            Assert.Single(label.TextLayout.TextLines);
            var right = label.TranslatePoint(new Point(label.Bounds.Width, 0), row)!.Value.X;
            Assert.True(right <= row.Bounds.Width + 0.5, $"\"{label.Text}\" runs past its row");
        }
    });

    [Fact]
    public void A_page_title_too_long_for_the_page_list_ends_in_an_ellipsis() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, _) = OpenSettled(manager, rig, SettingsPage.General);
        var row = new SidebarRow("Claude in Chrome and every other browser there is", () => { }, () => new Border { Width = 20, Height = 20 }, _ => new Border()) { Width = 180 };
        var host = new HostWindow { Width = 400, Height = 200, Content = row };
        host.Show();
        try
        {
            Drain(host);
            var label = row.GetVisualDescendants().OfType<TextBlock>().First(text => !string.IsNullOrEmpty(text.Text));
            var line = Assert.Single(label.TextLayout.TextLines);
            Assert.True(line.HasCollapsed, "the label is cut with an ellipsis");
            Assert.True(label.Bounds.Width <= 180);
        }
        finally { host.Close(); }
        Assert.True(window.IsVisible);
    });

    [Fact]
    public void The_panel_grows_smoothly_with_the_window_so_a_live_resize_never_jumps() => WithManager((_, manager) =>
    {
        // No step anywhere from the main window's minimum to past a 4K screen: the size moves at most as far as the window does (the side
        // and vertical margins only ever grow) and never shrinks as the window grows.
        var previous = SettingsOverlay.SettingsStage.CardSize(new Size(860, 560));
        for (var width = 861.0; width <= 3840; width += 1)
        {
            var height = 560 + (width - 860) * (1600 - 560) / (3840 - 860);
            var size = SettingsOverlay.SettingsStage.CardSize(new Size(width, height));
            var dw = size.Width - previous.Width;
            var dh = size.Height - previous.Height;
            Assert.InRange(dw, 0, 1.0001);
            Assert.InRange(dh, 0, (1600 - 560) / (3840.0 - 860) + 0.0001);
            previous = size;
        }
        Assert.True(previous.Width < SettingsOverlay.WidthCap);
        // The smallest main window still has a usable panel with room round it.
        var least = SettingsOverlay.SettingsStage.CardSize(new Size(860, 560));
        Assert.Equal(860 - 2 * SettingsOverlay.SideMargin, least.Width, 3);
        Assert.Equal(560 - 2 * SettingsOverlay.MinimumVerticalMargin, least.Height, 3);
        // Laid out live: a window dragged a pixel at a time moves the panel by no more than that (each step laid out; the bounds are the
        // layout's, so the window is not drawn at each step).
        using var rig = new SettingsRiseRig();
        var (window, overlay) = OpenSettled(manager, rig, SettingsPage.General);
        window.Width = 1240; window.Height = 900; Drain(window);
        var last = overlay.Card.Bounds;
        for (var step = 0; step < 40; step++)
        {
            window.Width += 7; window.Height += 5;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var now = overlay.Card.Bounds;
            Assert.InRange(now.Width - last.Width, 0, 7.0001);
            Assert.InRange(now.Height - last.Height, 0, 5.0001);
            Assert.Equal(now.Size, View(manager).Bounds.Size);
            last = now;
        }
    });

    // A wrapped line that ends inside a word (a letter or digit on both sides of the break) is a word broken mid-way.
    private static void AssertNoWordBroken(Control view, SettingsPage page)
    {
        foreach (var text in view.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && text.TextWrapping != TextWrapping.NoWrap && !string.IsNullOrEmpty(text.Text) && text.Bounds.Width > 0))
        {
            var content = text.Text!;
            var lines = text.TextLayout.TextLines;
            for (var i = 0; i < lines.Count - 1; i++)
            {
                var end = lines[i].FirstTextSourceIndex + lines[i].Length;
                if (end <= 0 || end >= content.Length) continue;
                var broken = char.IsLetterOrDigit(content[end - 1]) && char.IsLetterOrDigit(content[end]);
                Assert.False(broken, $"{page}: \"{content}\" breaks mid-word at {end} ({text.Bounds.Width:0.#} wide)");
            }
        }
    }

    [Fact]
    public void A_press_outside_the_panel_closes_it_and_a_press_inside_does_not() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, overlay) = OpenSettled(manager, rig, SettingsPage.General);
        var card = overlay.Card.TranslatePoint(default, window)!.Value;
        // Inside: on the panel's own surface, beside its content.
        PressAt(window, new Point(card.X + overlay.Card.Bounds.Width - 30, card.Y + overlay.Card.Bounds.Height - 10));
        Assert.True(manager.IsSettingsOpen);
        // Inside: on a control.
        Click(window, Assert.Single(Nodes<SidebarSearchField>(View(manager))));
        Assert.True(manager.IsSettingsOpen);
        // Outside: the dim beside the panel.
        var outside = new Point(card.X / 2, window.Bounds.Height / 2);
        AssertHitWithin(window, overlay.Dim, outside);
        PressAt(window, outside);
        Assert.False(manager.IsSettingsOpen);
        Settle(rig, window);
        Assert.False(overlay.IsVisible);
        // Opening again keeps what it showed.
        manager.ShowSettings(); Settle(rig, window);
        Assert.True(manager.IsSettingsOpen);
        Assert.Same(overlay, Overlay(manager));
    });

    [Fact]
    public void A_press_outside_while_a_menu_is_open_over_the_panel_closes_only_the_menu() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, overlay) = OpenSettled(manager, rig, SettingsPage.General);
        var dismissed = 0;
        var menu = new object();
        WindowTransients.Show(menu, View(manager), () => { dismissed++; WindowTransients.Hide(menu); });
        try
        {
            var card = overlay.Card.TranslatePoint(default, window)!.Value;
            window.MouseDown(new Point(card.X / 2, 200), MouseButton.Left);
            WindowTransients.DismissNewest(window); // what the menu's own outside-press handler does
            window.MouseUp(new Point(card.X / 2, 200), MouseButton.Left);
            Drain(window);
            Assert.True(manager.IsSettingsOpen);
            Assert.Equal(1, dismissed);
        }
        finally { WindowTransients.Hide(menu); }
        PressAt(window, new Point(overlay.Card.TranslatePoint(default, window)!.Value.X / 2, 200));
        Assert.False(manager.IsSettingsOpen);
    });

    [Fact]
    public void Escape_closes_the_panel_even_from_the_search_field_it_focused() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, _) = OpenSettled(manager, rig, SettingsPage.General);
        var field = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(View(manager)))));
        Assert.True(field.IsFocused);
        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.False(manager.IsSettingsOpen);
    });

    [Fact]
    public void Escape_with_a_menu_open_over_the_panel_closes_only_the_menu() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, _) = OpenSettled(manager, rig, SettingsPage.General);
        var dismissed = 0;
        var menu = new object();
        WindowTransients.Show(menu, View(manager), () => dismissed++);
        try
        {
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Equal(1, dismissed);
            Assert.True(manager.IsSettingsOpen);
        }
        finally { WindowTransients.Hide(menu); }
        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.False(manager.IsSettingsOpen);
    });

    [Fact]
    public void The_close_button_at_the_top_right_closes_the_panel() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, overlay) = OpenSettled(manager, rig, SettingsPage.General);
        var button = Assert.Single(overlay.CloseButton.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Close Settings");
        var card = overlay.Card.TranslatePoint(default, window)!.Value;
        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        // The window's pixels: the panel's 60 and 50 points drawn at the base scale (ED-NEW-textsize-2, ED-NEW-settingsinapp-4).
        var drawn = ZoomDrawn.On(overlay.Card, window);
        Assert.InRange(at.X - card.X, drawn.Width - 60 * AppZoom.BaseScale, drawn.Width);
        Assert.InRange(at.Y - card.Y, 0, 50 * AppZoom.BaseScale);
        Click(window, button);
        Assert.False(manager.IsSettingsOpen);
    });

    [Fact]
    public void The_Settings_shortcut_toggles_the_panel_and_Ctrl_W_closes_it() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        manager.ShowMain();
        var main = MainOf(manager); Drain(main);
        Press(main, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);
        Assert.True(manager.IsSettingsOpen);
        Settle(rig, main);
        Press(main, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);
        Assert.False(manager.IsSettingsOpen);
        Settle(rig, main);
        Press(main, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);
        Assert.True(manager.IsSettingsOpen);
        Settle(rig, main);
        Press(main, Key.W, PhysicalKey.W, RawInputModifiers.Control);
        Assert.False(manager.IsSettingsOpen);
    });

    [Fact]
    public void Focus_moves_into_the_panel_Tab_stays_inside_it_and_focus_returns_on_close() => WithManager((_, manager) =>
    {
        var before = new TextBox { Width = 200 };
        manager.MainContent = () => new StackPanel { Background = Brushes.Transparent, Children = { before } };
        using var rig = new SettingsRiseRig();
        manager.ShowMain();
        var main = MainOf(manager); Drain(main);
        Assert.True(before.Focus());
        var (window, overlay) = OpenSettled(manager, rig, SettingsPage.General);
        var field = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(View(manager)))));
        Assert.True(field.IsFocused);
        for (var i = 0; i < 80; i++)
        {
            Press(window, Key.Tab, PhysicalKey.Tab);
            var focused = Assert.IsAssignableFrom<Visual>(window.FocusManager!.GetFocusedElement());
            Assert.True(overlay.Card.IsVisualAncestorOf(focused), $"Tab {i + 1} left the panel for {focused.GetType().Name}");
        }
        Press(window, Key.Escape, PhysicalKey.Escape);
        if (manager.IsSettingsOpen) Press(window, Key.Escape, PhysicalKey.Escape); // a control that used the first Escape
        Assert.False(manager.IsSettingsOpen);
        Assert.True(before.IsFocused);
    });

    [Fact]
    public void Keys_never_reach_the_content_under_the_panel() => WithManager((_, manager) =>
    {
        var under = new TextBox { Width = 200 };
        manager.MainContent = () => new StackPanel { Background = Brushes.Transparent, Children = { under } };
        using var rig = new SettingsRiseRig();
        var (window, overlay) = OpenSettled(manager, rig, SettingsPage.General);
        var seen = 0;
        under.KeyDown += (_, _) => seen++;
        under.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, Source = under });
        window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
        Assert.Equal(0, seen + (under.IsFocused ? 1 : 0));
        Assert.True(overlay.IsOpen);
    });

    [Fact]
    public void The_panel_opens_like_a_popover_and_closes_in_0_17_s() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        manager.ShowMain();
        var main = MainOf(manager); Drain(main);
        manager.ShowSettings(SettingsPage.General);
        var overlay = Overlay(manager);
        var host = overlay.Card.Parent as Control;
        Assert.NotNull(host);
        Assert.Equal(0, overlay.Dim.Opacity);
        Assert.Equal(0, host!.Opacity);
        rig.Frame(0.0); Drain(main);
        rig.Frame(0.1); Drain(main);
        var scale = Assert.IsType<ScaleTransform>(host.RenderTransform);
        Assert.InRange(host.Opacity, 0.3, 0.99);
        Assert.InRange(scale.ScaleX, SettingsOverlay.StartScale + 0.001, 0.9999);
        Assert.Equal(host.Opacity, overlay.Dim.Opacity, 6);
        rig.Frame(0.16); Drain(main);
        Assert.Equal(1, host.Opacity);
        Assert.Equal(1, scale.ScaleX);
        // Input reaches the panel while it opens: the open is a look, not a lock.
        manager.CloseSettings();
        rig.Frame(0.0); Drain(main);
        rig.Frame(0.1); Drain(main);
        Assert.True(overlay.IsVisible);
        Assert.InRange(host.Opacity, 0.01, 0.99);
        Assert.True(scale.ScaleX < 1);
        rig.Frame(0.08); Drain(main);
        Assert.False(overlay.IsVisible);
        Assert.Equal(0, overlay.Dim.Opacity);
    });

    [Fact]
    // ED-NEW-settingsinapp-1 (ED-254): the Settings window turned over as a whole; the panel's window does while it shows. Its content
    // shows dimmed around the panel and its caption buttons lie over it, outside the panel's tree: they turn over with it, and once it
    // has closed the window is as before (it changes appearance at once, as the main window always did).
    public void The_whole_window_turns_over_with_the_panel_while_it_shows() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        manager.ShowMain();
        var main = MainOf(manager); Drain(main);
        var root = Assert.IsAssignableFrom<Panel>(main.Content);
        var content = root.Children[0]; // the window's content (FrameHostingView), dimmed around the panel
        Control[] inContent = [content, .. content.GetVisualDescendants().OfType<Control>()];
        Assert.All(inContent, control => Assert.False(AppearanceTurn.Eases(control)));
        var plainText = root.GetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty);
        manager.ShowSettings(SettingsPage.General);
        var overlay = Overlay(manager);
        Assert.Same(root, overlay.Parent);
        Assert.False(ReferenceEquals(content, overlay) || overlay.IsVisualAncestorOf(content)); // outside the panel's tree
        rig.Frame(0.0); Drain(main);
        rig.Frame(0.3); Drain(main);
        var buttons = main.GetVisualDescendants().OfType<CaptionButton>().ToList(); // the window's minimize, maximize and close, made as Settings shows
        Assert.Equal(3, buttons.Count);
        Assert.All(buttons, button => Assert.False(overlay.IsVisualAncestorOf(button)));
        Control[] outside = [.. buttons, .. inContent];
        Assert.True(AppearanceTurn.GetTurnsOver(root));
        Assert.All(outside, control => Assert.True(AppearanceTurn.Eases(control)));
        Assert.NotSame(plainText, root.GetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty)); // held as the panel's is
        manager.CloseSettings();
        rig.Frame(0.0); Drain(main);
        rig.Frame(0.1); Drain(main);
        Assert.True(AppearanceTurn.GetTurnsOver(root)); // still on screen around the closing panel
        rig.Frame(0.1); Drain(main);
        Assert.False(overlay.IsVisible);
        Assert.False(AppearanceTurn.GetTurnsOver(root));
        Assert.All(outside, control => Assert.False(AppearanceTurn.Eases(control)));
        Assert.Same(plainText, root.GetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
    });

    [Fact]
    public void Reduce_motion_fades_the_panel_without_scaling_it() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        manager.ShowMain();
        var main = MainOf(manager); Drain(main);
        EnvironmentValues.SetAccessibilityReduceMotion(main, true);
        manager.ShowSettings(SettingsPage.General);
        var overlay = Overlay(manager);
        var host = (Control)overlay.Card.Parent!;
        rig.Frame(0.0); Drain(main);
        rig.Frame(0.1); Drain(main);
        Assert.InRange(host.Opacity, 0.3, 0.99);
        Assert.Equal(1, Assert.IsType<ScaleTransform>(host.RenderTransform).ScaleX);
    });

    [Fact]
    public void A_requested_provider_is_opened_and_brought_into_view_inside_the_panel() => WithManager((_, manager) =>
    {
        using var refused = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(refused);
        using var rig = new SettingsRiseRig();
        SettingsNavigation.Shared.RequestedProvider = ProviderKind.Claude;
        try
        {
            OpenSettled(manager, rig, SettingsPage.Providers);
            var page = Assert.Single(Nodes<ProvidersSettingsPage>(View(manager)));
            Assert.Equal(ProviderKind.Claude, page.Revealed);
            Assert.True(page.IsOpen(ProviderKind.Claude));
            Assert.Null(SettingsNavigation.Shared.RequestedProvider);
        }
        finally { SettingsNavigation.Shared.RequestedProvider = null; }
    });

    [Fact]
    public void A_sheet_Settings_opens_lies_above_the_panel_in_the_same_window_and_Escape_closes_only_the_sheet() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, overlay) = OpenSettled(manager, rig, SettingsPage.General);
        var opened = new List<HostWindow>();
        using var watch = HostWindow.WindowOpenedEvent.AddClassHandler<HostWindow>((w, _) => opened.Add(w));
        var sheet = new Border { Width = 240, Height = 120, Background = Brushes.Gray, Child = new Button { Content = "Inside the sheet" } };
        var anchor = Assert.Single(Nodes<SidebarSearchField>(View(manager)));
        ChatSheetPresentation.Show(anchor, _ => sheet, inWindow: true, still: true);
        Settle(rig, window);
        Assert.Empty(opened);
        Assert.Same(window, TopLevel.GetTopLevel(sheet));
        var root = Assert.IsAssignableFrom<Panel>(window.Content);
        var layer = Assert.Single(root.Children.OfType<WindowSheetLayer>());
        Assert.True(root.Children.IndexOf(layer) > root.Children.IndexOf(overlay));
        AssertHitWithin(window, sheet, sheet.TranslatePoint(new Point(120, 60), window)!.Value);
        Press(window, Key.Escape, PhysicalKey.Escape);
        Settle(rig, window);
        Assert.Empty(root.Children.OfType<WindowSheetLayer>());
        Assert.True(manager.IsSettingsOpen);
    });

    [Fact]
    public void Every_Settings_page_fades_its_content_under_its_title_row_once_scrolled_and_pages_too_short_to_scroll_have_no_fade() => WithManager((_, manager) =>
    {
        using var refused = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(refused);
        using var rig = new SettingsRiseRig();
        var (window, _) = OpenSettled(manager, rig, SettingsPage.General);
        window.Width = 1100; window.Height = 700; Drain(window);
        var view = View(manager);
        var scrolled = 0;
        foreach (var page in SwiftEnum<SettingsPage>.AllCases.Where(page => page.IsReady))
        {
            manager.ShowSettings(page); Drain(window); Settle(rig, window);
            var content = Assert.IsAssignableFrom<Control>(view.RisePage);
            if (content.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is not { } scroll)
            {
                // the one page with nothing to scroll: an empty list's symbol in place of the page (SettingsView.swift:204)
                Assert.Equal(SettingsPage.Archive, page);
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<ScrollViewer>(), viewer => viewer.OpacityMask is not null && viewer.IsEffectivelyVisible && content.GetVisualAncestors().Contains(viewer));
                continue;
            }
            Assert.True(ScrollEdgeFade.GetIsEnabled(scroll), $"{page}: the page's scroll fades");
            Assert.Equal(Chrome.ChromeTopPadding + Chrome.CapsuleHeight, ScrollEdgeFade.GetTopInset(scroll), 3);
            Assert.Equal(0, ScrollEdgeFade.TopStrength(scroll)); // at its top, nothing above to fade under
            if (scroll.Extent.Height <= scroll.Viewport.Height + 0.5)
            {
                Assert.Null(scroll.OpacityMask); // too short to scroll: no fade at all
                continue;
            }
            scrolled++;
            Assert.True(ScrollEdgeFade.BottomStrength(scroll) > 0, $"{page}: more below, so the foot fades");
            scroll.Offset = new Vector(0, Math.Min(200, scroll.Extent.Height - scroll.Viewport.Height)); Drain(window);
            Assert.Equal(1, ScrollEdgeFade.TopStrength(scroll), 3);
            var mask = Assert.IsType<LinearGradientBrush>(scroll.OpacityMask);
            // The title band is clear (the content under the title is not drawn) and the content is whole below the fade.
            Assert.Equal(0, mask.GradientStops[0].Color.A);
            Assert.Equal(0, mask.GradientStops[1].Color.A);
            Assert.Equal((Chrome.ChromeTopPadding + Chrome.CapsuleHeight) / scroll.Bounds.Height, mask.GradientStops[1].Offset, 4);
            Assert.Equal(255, mask.GradientStops[2].Color.A);
            Assert.Equal((Chrome.ChromeTopPadding + Chrome.CapsuleHeight + SettingsOverlay.TopFade) / scroll.Bounds.Height, mask.GradientStops[2].Offset, 4);
            // The title keeps its backing: the pane's top veil is drawn over the scrolled content.
            var veil = Assert.Single(Nodes<PaneTopVeil>((Control)scroll.Parent!));
            Assert.True(veil.GetVisualDescendants().OfType<Border>().Any(), $"{page}: the title's veil is drawn");
            scroll.Offset = new Vector(0, scroll.Extent.Height); Drain(window);
            Assert.Equal(0, ScrollEdgeFade.BottomStrength(scroll)); // at the end, nothing below
        }
        Assert.True(scrolled >= 5, $"only {scrolled} pages scrolled at this size");
        // The sidebar's list fades too (every scroll area in Settings).
        Assert.All(view.GetVisualDescendants().OfType<ScrollViewer>(), scroll => Assert.True(ScrollEdgeFade.GetIsEnabled(scroll)));
    });
}

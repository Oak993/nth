using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// NativeContextMenus (PORT-ONLY, proposal ED-229 (1)): a right-click on a control with a ContextMenu shows its rows as the
// system menu (a recording presenter here), rebuilt on every open, and the chosen row clicks its MenuItem.
[Collection("Headless")]
public sealed class NativeContextMenusTests(HeadlessSession headless)
{
    private static Window Host(Control content)
    {
        var layers = new VisualLayerManager { EnableOverlayLayer = true, Child = content };
        typeof(VisualLayerManager).GetProperty("EnablePopupOverlayLayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(layers, true);
        var window = new Window { Content = layers, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void RightClick(Window window, Point at)
    {
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Decision", "ED-107")] // (5) the shared registration point that installs the handler
    public void A_right_click_shows_the_rows_the_menu_builds_on_opening_and_never_the_empty_managed_popup() => headless.OnUiThread(() =>
    {
        var recorder = new ContextMenuRecorder();
        using var presenter = NativeContextMenus.Presenter.Override(recorder);
        var opens = 0;
        var target = new Border { Width = 200, Height = 100, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Background = Avalonia.Media.Brushes.White };
        var menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            opens++;
            menu.Items.Clear();
            menu.Items.Add(new MenuItem { Header = "Rename" });
            menu.Items.Add(new MenuItem { Header = new StackPanel { Children = { new Border(), new TextBlock { Text = "Pin" } } } });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "Archive", IsEnabled = false });
            menu.Items.Add(new MenuItem { Header = "Show reasoning", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true });
            menu.Items.Add(new MenuItem { Header = "Pair", Items = { new MenuItem { Header = "Opus" }, new MenuItem { Header = "Sonnet" } } });
            menu.Items.Add(new Separator());
        };
        target.ContextMenu = menu;
        var window = Host(target);
        try
        {
            RightClick(window, new Point(50, 40));
            var shown = Assert.Single(recorder.Shown);
            Assert.Same(target, shown.Target);
            Assert.Equal(1, opens);
            Assert.False(menu.IsOpen);
            Assert.Equal(["Rename", "Pin", "", "Archive", "Show reasoning", "Pair"], shown.Entries.Select(e => e.Title));
            Assert.True(shown.Entries[2].IsSeparator);
            Assert.False(shown.Entries[3].IsEnabled);
            Assert.True(shown.Entries[4].IsChecked);
            Assert.Equal(["Opus", "Sonnet"], shown.Entries[5].Children.Select(e => e.Title));
            // Each open reads afresh (the sites' per-open rebuild).
            RightClick(window, new Point(60, 40));
            Assert.Equal(2, recorder.Shown.Count);
            Assert.Equal(2, opens);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Choosing_a_row_clicks_its_menu_item() => headless.OnUiThread(() =>
    {
        var recorder = new ContextMenuRecorder();
        using var presenter = NativeContextMenus.Presenter.Override(recorder);
        var copied = 0;
        var copy = new MenuItem { Header = "Copy" };
        copy.Click += (_, _) => copied++;
        var target = new Border { Width = 200, Height = 100, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Background = Avalonia.Media.Brushes.White, ContextMenu = new ContextMenu { Items = { copy, new MenuItem { Header = "Select All" } } } };
        var window = Host(target);
        try
        {
            RightClick(window, new Point(20, 20));
            Assert.Equal(["Copy", "Select All"], recorder.Shown.Single().Entries.Select(e => e.Title));
            recorder.Shown.Single().Entries[0].Choose!();
            Assert.Equal(1, copied);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")] // LinkParagraphView.swift:453-461 a right-click on prose falls through to AppKit's text menu
    public void A_right_click_on_message_prose_offers_copy_and_select_all_and_select_all_selects_the_text() => headless.OnUiThread(() =>
    {
        var recorder = new ContextMenuRecorder();
        using var presenter = NativeContextMenus.Presenter.Override(recorder);
        var text = new ParagraphText();
        text.Append("The answer, in one paragraph.");
        var prose = new ParagraphTextPresenter { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        prose.SetText(text);
        var window = Host(prose);
        try
        {
            prose.Layout(300);
            window.UpdateLayout();
            RightClick(window, new Point(5, 5));
            var shown = Assert.Single(recorder.Shown);
            Assert.Equal(["Copy", "Select All"], shown.Entries.Select(e => e.Title));
            shown.Entries[1].Choose!();
            Assert.Equal("The answer, in one paragraph.", prose.SelectedText);
        }
        finally { window.Close(); }
    });

    private static Border Target(ContextMenu menu) => new()
    {
        Width = 200, Height = 100, Background = Avalonia.Media.Brushes.White, ContextMenu = menu,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
    };

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Choosing_a_row_runs_its_command_once() => headless.OnUiThread(() =>
    {
        var recorder = new ContextMenuRecorder();
        using var presenter = NativeContextMenus.Presenter.Override(recorder);
        var runs = 0;
        var item = new MenuItem { Header = "Archive", Command = new CountingCommand(() => runs++) };
        var window = Host(Target(new ContextMenu { Items = { item } }));
        try
        {
            RightClick(window, new Point(20, 20));
            recorder.Shown.Single().Entries[0].Choose!();
            Assert.Equal(1, runs);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_menu_whose_opening_is_cancelled_is_not_shown() => headless.OnUiThread(() =>
    {
        var recorder = new ContextMenuRecorder();
        using var presenter = NativeContextMenus.Presenter.Override(recorder);
        var menu = new ContextMenu { Items = { new MenuItem { Header = "Copy" } } };
        menu.Opening += (_, e) => e.Cancel = true;
        var window = Host(Target(menu));
        try
        {
            RightClick(window, new Point(20, 20));
            Assert.Empty(recorder.Shown);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Hidden_rows_are_left_out_and_a_divider_they_leave_alone_is_dropped() => headless.OnUiThread(() =>
    {
        var recorder = new ContextMenuRecorder();
        using var presenter = NativeContextMenus.Presenter.Override(recorder);
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = "Copy" },
                new Separator(),
                new MenuItem { Header = "Delete", IsVisible = false },
                new Separator { IsVisible = false },
                new MenuItem { Header = "Rename" },
            },
        };
        var window = Host(Target(menu));
        try
        {
            RightClick(window, new Point(20, 20));
            Assert.Equal(["Copy", "", "Rename"], recorder.Shown.Single().Entries.Select(e => e.Title));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Choosing_a_check_box_row_flips_it_as_a_menu_toggle_does() => headless.OnUiThread(() =>
    {
        var recorder = new ContextMenuRecorder();
        using var presenter = NativeContextMenus.Presenter.Override(recorder);
        var toggle = new MenuItem { Header = "Show reasoning", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true };
        var window = Host(Target(new ContextMenu { Items = { toggle } }));
        try
        {
            RightClick(window, new Point(20, 20));
            recorder.Shown.Single().Entries[0].Choose!();
            Assert.False(toggle.IsChecked);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void The_menu_raises_opened_before_it_shows_and_closed_after() => headless.OnUiThread(() =>
    {
        var log = new List<string>();
        var recorder = new ContextMenuLog(log);
        using var presenter = NativeContextMenus.Presenter.Override(recorder);
        var menu = new ContextMenu { Items = { new MenuItem { Header = "Copy" } } };
        menu.Opened += (_, _) => log.Add("opened");
        menu.Closed += (_, _) => log.Add("closed");
        var window = Host(Target(menu));
        try
        {
            RightClick(window, new Point(20, 20));
            Assert.Equal(["opened", "present", "closed"], log);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Shift_F10_on_a_focused_control_shows_its_menu() => headless.OnUiThread(() =>
    {
        var recorder = new ContextMenuRecorder();
        using var presenter = NativeContextMenus.Presenter.Override(recorder);
        var target = new Button { Content = "Row", Focusable = true, ContextMenu = new ContextMenu { Items = { new MenuItem { Header = "Copy" } } } };
        var window = Host(target);
        try
        {
            target.Focus();
            window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
            window.KeyRelease(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null); // Avalonia asks for the menu on key-up
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Copy"], Assert.Single(recorder.Shown).Entries.Select(e => e.Title));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_control_without_a_menu_shows_nothing() => headless.OnUiThread(() =>
    {
        var recorder = new ContextMenuRecorder();
        using var presenter = NativeContextMenus.Presenter.Override(recorder);
        var window = Host(new Border { Width = 200, Height = 100, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Background = Avalonia.Media.Brushes.White });
        try
        {
            RightClick(window, new Point(20, 20));
            Assert.Empty(recorder.Shown);
        }
        finally { window.Close(); }
    });
}

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

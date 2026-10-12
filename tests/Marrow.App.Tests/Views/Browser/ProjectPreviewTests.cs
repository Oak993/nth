using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Web;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.Testing;
using Avalonia.Threading;

namespace Marrow.App.Tests.Views.Browser;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w9-3")]
public sealed class ProjectPreviewTests(HeadlessSession headless)
{
    [Fact]
    public void Only_small_hot_update_frames_are_read_as_a_hot_reload_witness()
    {
        static string Frame(string payload) => System.Text.Json.JsonSerializer.Serialize(new { response = new { payloadData = payload } });
        Assert.True(ProjectPreviewRuntime.IsHotUpdate(Frame("{\"type\":\"update\",\"updates\":[]}")));
        Assert.True(ProjectPreviewRuntime.IsHotUpdate(Frame("{\"action\":\"built\"}")));
        Assert.False(ProjectPreviewRuntime.IsHotUpdate(Frame("{\"type\":\"ping\"}")));
        Assert.False(ProjectPreviewRuntime.IsHotUpdate(Frame("not json")));
        Assert.False(ProjectPreviewRuntime.IsHotUpdate("{\"response\":{}}"));
        Assert.False(ProjectPreviewRuntime.IsHotUpdate("[]"));
        // The page's own websocket traffic reaches this handler on the UI thread: a frame over 64 KiB is not parsed, even with the witness in it.
        Assert.True(ProjectPreviewRuntime.IsHotUpdate(Frame("{\"type\":\"update\",\"pad\":\"" + new string('x', 20_000) + "\"}")));
        Assert.False(ProjectPreviewRuntime.IsHotUpdate(Frame("{\"type\":\"update\",\"pad\":\"" + new string('x', 70_000) + "\"}")));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w9-11")]
    public void Drawer_exposes_named_filters_empty_state_selected_tabs_and_long_RTL_console_text() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig(); var tab = rig.Page("http://localhost:5173/", VisusTabKind.Preview);
        var preview = new VisusPaneBody(rig.Session).Preview!;
        var search = LogicalQuery.SelfAndDescendants<TextBox>(preview.Drawer).Single();
        var levels = LogicalQuery.SelfAndDescendants<ComboBox>(preview.Drawer).Single();
        var list = LogicalQuery.SelfAndDescendants<ListBox>(preview.Drawer).Single();
        Assert.Equal("Search preview logs", Avalonia.Automation.AutomationProperties.GetName(search));
        Assert.Equal("Console level", Avalonia.Automation.AutomationProperties.GetName(levels));
        Assert.Equal("Preview log entries", Avalonia.Automation.AutomationProperties.GetName(list));
        Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(preview.Drawer), text => text.Text == "No console messages" && text.IsVisible);
        var console = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Primitives.ToggleButton>(preview.Drawer).Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Console");
        var network = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Primitives.ToggleButton>(preview.Drawer).Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Network");
        Assert.True(console.IsChecked); Assert.False(network.IsChecked);
        network.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.False(console.IsChecked); Assert.True(network.IsChecked);
        console.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var longText = string.Concat(Enumerable.Repeat("رسالة طويلة ", 160));
        VisusRig.Live(tab).EmitCdp("Runtime.consoleAPICalled", System.Text.Json.JsonSerializer.Serialize(new { type = "error", args = new[] { new { value = longText } } }));
        search.Text = "رسالة"; Dispatcher.UIThread.RunJobs();
        var entry = Assert.IsType<Marrow.Services.Web.PreviewConsoleEntry>(Assert.Single(list.ItemsSource!.Cast<object>()));
        Assert.Equal(longText, entry.Text); Assert.Equal("error", entry.Level);
        var row = list.ItemTemplate!.Build(entry);
        Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(row!), text => text.Text == longText && text.TextWrapping == Avalonia.Media.TextWrapping.Wrap);
    });
    [Fact]
    [Trait("Decision", "ED-NEW-web-w9-9")]
    public void Drawer_tabs_keep_the_approved_24_DIP_height_7_DIP_radius_and_10_DIP_padding() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig(); rig.Page("http://localhost:5173/", VisusTabKind.Preview);
        var preview = new VisusPaneBody(rig.Session).Preview!;
        foreach (var name in new[] { "Console", "Network" })
        {
            var button = LogicalQuery.SelfAndDescendants<Button>(preview.Drawer).Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == name);
            Assert.Equal(24, button.Height); Assert.Equal(new Avalonia.CornerRadius(7), button.CornerRadius);
            Assert.Equal(new Avalonia.Thickness(10, 0), button.Padding);
        }
        var console = LogicalQuery.SelfAndDescendants<Button>(preview.Drawer).Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Console");
        var network = LogicalQuery.SelfAndDescendants<Button>(preview.Drawer).Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Network");
        Assert.Equal(Marrow.UI.Panes.PaneTokens.HoverFill.Resolve(console.ActualThemeVariant), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(console.Background).Color);
        Assert.Equal(Avalonia.Media.Colors.Transparent, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(network.Background).Color);
        network.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Marrow.UI.Panes.PaneTokens.HoverFill.Resolve(network.ActualThemeVariant), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(network.Background).Color);
        Assert.Equal(Avalonia.Media.Colors.Transparent, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(console.Background).Color);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w9-9")]
    public void Fill_has_no_quiet_device_rim_or_shadow_and_the_preset_restores_it() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig(); rig.Page("http://localhost:5173/", VisusTabKind.Preview);
        var preview = new VisusPaneBody(rig.Session).Preview!;
        var frame = LogicalQuery.SelfAndDescendants<Border>(preview).Single(b => b.Parent is Canvas && b.BoxShadow.Count > 0);
        Assert.True(frame.IsVisible);
        preview.SetDevice(DevicePreset.Fill); Assert.False(frame.IsVisible);
        preview.SetDevice(DevicePreset.Phone); Assert.True(frame.IsVisible);
    });

    [Fact]
    public void Closing_console_keeps_the_error_badge_separate_from_the_label() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("http://localhost:5173/", VisusTabKind.Preview);
        var page = VisusRig.Live(tab);
        page.EmitCdp("Runtime.consoleAPICalled", "{\"type\":\"error\",\"args\":[{\"value\":\"fixture\"}]}");
        page.EmitCdp("Runtime.exceptionThrown", "{\"exceptionDetails\":{\"text\":\"fixture\"}}");
        var body = new VisusPaneBody(rig.Session); var preview = body.Preview!;
        preview.ToggleConsole(); preview.Bar.Refresh();
        var row = (Panel)((ScrollViewer)preview.Bar.Child!).Content!;
        var button = row.Children.OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Console");
        var label = Assert.IsType<StackPanel>(button.Content);
        Assert.Equal("Console", Assert.Single(label.Children.OfType<TextBlock>()).Text);
        var badge = Assert.Single(label.Children.OfType<Border>());
        Assert.Equal(16, badge.Height); Assert.Equal("2", Assert.IsType<TextBlock>(badge.Child).Text);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w9-6")]
    public void Closing_console_keeps_the_drawer_visible_until_the_height_transition_can_run_and_can_reverse() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var clock = SwiftClock.Provider.Override(rig.Time);
        using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
        rig.Page("http://localhost:5173/", VisusTabKind.Preview);
        var body = new VisusPaneBody(rig.Session);
        // Avalonia.Headless owns this fake TopLevel; no native HWND or real window is created.
        var window = new Avalonia.Controls.Window { Width = 1000, Height = 800, Content = body };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var preview = body.Preview!; Assert.True(preview.Drawer.IsVisible);
            preview.ToggleConsole();
            Assert.False(preview.Drawer.State.Open);
            Assert.True(preview.Drawer.IsVisible); Assert.True(preview.Drawer.Height > 0);
            preview.ToggleConsole();
            Assert.True(preview.Drawer.State.Open); Assert.True(preview.Drawer.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Preview_tabs_have_the_bar_drawer_and_shared_splitters_browse_tabs_do_not() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("http://localhost:5173/", VisusTabKind.Preview);
        var body = new VisusPaneBody(rig.Session);
        Assert.NotNull(body.Preview);
        Assert.Equal(38, body.Preview!.Bar.Height);
        Assert.Equal(3, body.Preview.DeviceSplitters.Count);
        Assert.Equal("390 × 844", body.Preview.Bar.SizeText);
        body.Preview.Drawer.State.Begin(); body.Preview.Drawer.State.DragTo(33, 800); body.Preview.Drawer.State.End();
        body.Preview.Drawer.Refresh();
        Assert.True(body.Preview.Drawer.IsStrip); Assert.Equal(24, body.Preview.Drawer.ShownHeight);
        rig.Session.NewTab(VisusTabKind.Browse, "https://example.test/");
        // Sync on an unattached body is explicit; no real/native window is used.
        body.RefreshPreview(); Assert.Null(body.Preview);
        rig.Session.Select(tab); body.RefreshPreview(); Assert.NotNull(body.Preview);
    });

    [Fact]
    public void Preview_settings_survive_close_reopen_and_picker_chord_is_claimed() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("http://localhost:5173/", VisusTabKind.Preview);
        var body = new VisusPaneBody(rig.Session);
        body.Preview!.SetDevice(DevicePreset.Responsive);
        body.Preview.ResizeResponsive(412, 860); body.Preview.EndResponsive();
        Assert.Equal(412, tab.State.Preview!.Width); Assert.Equal(860, tab.State.Preview.Height);
        rig.Session.Close(tab); Assert.True(rig.Session.ReopenClosed());
        Assert.Equal(412, rig.Session.Selected!.State.Preview!.Width);
        Assert.Equal(VisusCommand.PickElement, VisusKeys.CommandFor(new Marrow.Core.Models.KeyChord(0x12, Marrow.Core.Port.ChordModifiers.Control | Marrow.Core.Port.ChordModifiers.Shift)));
    });
}

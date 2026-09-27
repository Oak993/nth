using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/FloatingGuidePanel.swift (B3 52bc7926), :1-71.
[Collection("Headless")]
public sealed class FloatingGuidePanelTests(HeadlessSession headless)
{
    private static MCPCatalogEntry KeyEntry(string id = "notion", MCPField[]? fields = null) =>
        new(id, "Notion", "Notion", "Notes", MCPCategory.Developer, "notion",
            0, new MCPTransport.Stdio("notion", ValueList<string>.Empty), "https://notion.example/docs")
        {
            Fields = new ValueList<MCPField>(fields ?? [new MCPField("api_key", "API key", "Paste the key", MCPField.Kind.Secret, null, IsRequired: false)]),
        };

    private static MCPCatalogEntry SetupEntry(string id = "gitlab") =>
        new(id, "GitLab", "GitLab", "Source control", MCPCategory.Developer, "",
            0, new MCPTransport.Stdio("node", ValueList<string>.Empty), "https://gitlab.com");

    private static MCPSetupGuide Guide() => new(
        "GitLab",
        "GitLab keeps its MCP server off until a group owner turns it on.",
        new ValueList<string>(["Open the group settings.", "Enable the MCP server."]),
        "Open my groups",
        "https://gitlab.com/groups",
        null);

    private static AvaloniaWindow? PanelOf(FloatingGuidePanel owner) =>
        (AvaloniaWindow?)typeof(FloatingGuidePanel)
            .GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(owner);

    // :33-56 makePanel(), shared by both subclasses through FloatingGuidePanel alone; MCPSetupPanel had none of
    // this before B3 (a pre-existing gap: it built its own window with no ShowActivated = false and no ED-063
    // hook), which is what B3's move into this shared base class fixes.
    [Fact]
    [Trait("Req", "PR-MCP-215")] // App/Views/Settings/FloatingGuidePanel.swift:33-56
    [Trait("Decision", "ED-063")]
    public void Both_panels_share_the_same_nonactivating_chromeless_floating_window_setup()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();

            harness.Main.OnMain(() => { MCPKeyPanel.Shared.Present(KeyEntry(), model); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); });
            var keyWindow = PanelOf(MCPKeyPanel.Shared);
            Assert.NotNull(keyWindow);
            Assert.False(keyWindow!.ShowActivated);
            Assert.True(keyWindow.Topmost);
            Assert.False(keyWindow.ShowInTaskbar);
            Assert.False(keyWindow.CanResize);
            Assert.Equal(WindowDecorations.None, keyWindow.WindowDecorations);
            Assert.Equal(100, keyWindow.MinHeight);
            Assert.Equal(380, keyWindow.Width);
            // :46-48 panel.isOpaque = false / backgroundColor = .clear, over a WindowBackdrop(0.5) with no
            // wallpaper (OD-0005/OD-0014): the window itself must supply the Acrylic material.
            Assert.Contains(WindowTransparencyLevel.AcrylicBlur, keyWindow.TransparencyLevelHint);
            MCPKeyPanel.Shared.Close();

            harness.Main.OnMain(() => { MCPSetupPanel.Shared.Present(SetupEntry(), Guide(), model); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); });
            var setupWindow = PanelOf(MCPSetupPanel.Shared);
            Assert.NotNull(setupWindow);
            Assert.False(setupWindow!.ShowActivated);
            Assert.True(setupWindow.Topmost);
            Assert.False(setupWindow.ShowInTaskbar);
            Assert.False(setupWindow.CanResize);
            Assert.Equal(WindowDecorations.None, setupWindow.WindowDecorations);
            Assert.Equal(100, setupWindow.MinHeight);
            Assert.Equal(400, setupWindow.Width);
            Assert.Contains(WindowTransparencyLevel.AcrylicBlur, setupWindow.TransparencyLevelHint);
            MCPSetupPanel.Shared.Close();
        });
    }

    // :16-26 present(_:): the height floor and the reused panel instance across presents.
    [Fact]
    [Trait("Req", "PR-MCP-215")] // App/Views/Settings/FloatingGuidePanel.swift:16-26
    public void Present_reuses_the_same_window_instance_and_floors_the_height_at_100()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();

            harness.Main.OnMain(() => { MCPKeyPanel.Shared.Present(KeyEntry(fields: []), model); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); });
            var first = PanelOf(MCPKeyPanel.Shared);
            Assert.NotNull(first);
            Assert.True(first!.Height >= 100);

            harness.Main.OnMain(() => { MCPKeyPanel.Shared.Present(KeyEntry(fields: []), model); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); });
            var second = PanelOf(MCPKeyPanel.Shared);
            Assert.Same(first, second);
            MCPKeyPanel.Shared.Close();
        });
    }

    // :58-64 startEscapeMonitor(): an application-wide monitor, not scoped to the panel window itself (needed
    // because a truly non-activating panel never receives OS keyboard focus).
    [Fact]
    [Trait("Req", "PR-MCP-216")] // App/Views/Settings/FloatingGuidePanel.swift:58-69
    public void Escape_anywhere_in_the_app_closes_the_visible_panel_and_does_nothing_once_it_is_closed()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();

            harness.Main.OnMain(() => { MCPKeyPanel.Shared.Present(KeyEntry(fields: []), model); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); });
            var window = PanelOf(MCPKeyPanel.Shared)!;
            Assert.True(window.IsVisible);

            var elsewhere = new AvaloniaWindow { Content = new Border() };
            elsewhere.Show();
            elsewhere.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(window.IsVisible);

            // Closed already: the monitor was removed by Close(), so a second Escape does nothing (and does
            // not throw).
            elsewhere.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(window.IsVisible);
            elsewhere.Close();
        });
    }

    // :58-64: a key other than Escape passes through untouched.
    [Fact]
    [Trait("Req", "PR-MCP-216")] // App/Views/Settings/FloatingGuidePanel.swift:60
    public void A_nonescape_key_does_not_close_the_visible_panel()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();

            harness.Main.OnMain(() => { MCPSetupPanel.Shared.Present(SetupEntry(), Guide(), model); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); });
            var window = PanelOf(MCPSetupPanel.Shared)!;

            var elsewhere = new AvaloniaWindow { Content = new Border() };
            elsewhere.Show();
            elsewhere.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A });
            Assert.True(window.IsVisible);

            elsewhere.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(window.IsVisible);
            elsewhere.Close();
        });
    }
}

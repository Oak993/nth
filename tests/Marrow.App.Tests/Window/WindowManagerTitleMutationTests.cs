using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Window;
using Marrow.Core.Support;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Window;

// Source: WindowManager.swift:63, :97; Text.swift:4. OD-0002 changes the shipped metadata, not its lookup.
[Collection("Headless")]
public sealed class WindowManagerTitleMutationTests(HeadlessSession headless)
{
    // STOP NAME-FALLBACK: WIN.json PR-WIN-009 says absent metadata becomes Marrow,
    // but Core/Support/Text.cs:31-32 and the unchanged Core oracle require Droppy Code.
    // Factory/peer coverage protects that oracle; it does not resolve the specification conflict.
    [Fact]
    [Trait("Req", "PR-WIN-009")] // WindowManager.swift:63,97; preserve the existing Core fallback through the real factory.
    public void Missing_display_name_metadata_reaches_the_main_window_and_managed_peer_as_Droppy_Code() => WindowManagerMutationScope.Run(headless, (_, manager) =>
    {
        using var info = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?>(), isDevelopment: true));
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        Assert.Equal("Droppy Code", window.Title);
        var peer = ControlAutomationPeer.CreatePeerForElement(window);
        Assert.NotNull(peer);
        Assert.Equal("Droppy Code", peer.GetName());
    });

    [Fact]
    [Trait("Req", "PR-WIN-009")] // WindowManager.swift:63,97; a non-string is not a usable display name.
    public void Wrong_type_display_name_metadata_reaches_the_main_window_and_managed_peer_as_Droppy_Code() => WindowManagerMutationScope.Run(headless, (_, manager) =>
    {
        using var info = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?>
        {
            ["CFBundleDisplayName"] = false,
        }, isDevelopment: true));
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        Assert.Equal("Droppy Code", window.Title);
        var peer = ControlAutomationPeer.CreatePeerForElement(window);
        Assert.NotNull(peer);
        Assert.Equal("Droppy Code", peer.GetName());
    });

    [Fact]
    [Trait("Req", "PR-WIN-009")]
    public void The_main_window_and_its_accessibility_peer_use_the_supplied_display_name_not_a_literal() => WindowManagerMutationScope.Run(headless, (_, manager) =>
    {
        const string displayName = "Window proof \U000003A9 007"; // U+03A9 GREEK CAPITAL LETTER OMEGA
        using var metadata = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?> { ["CFBundleDisplayName"] = displayName }, isDevelopment: true));
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        Assert.Equal(displayName, window.Title);
        var peer = ControlAutomationPeer.CreatePeerForElement(window);
        Assert.NotNull(peer);
        Assert.Equal(displayName, peer.GetName());
        manager.SettingsContent = () => new Border();
        manager.ShowSettings();
        Assert.Equal("Settings", WindowManagerMutationScope.Settings(manager).Title);
    });

    [Fact]
    [Trait("Req", "PR-WIN-009")]
    public void The_unmodified_main_factory_hosts_one_real_RootView_with_the_managers_model_and_window_environment() => WindowManagerMutationScope.Run(headless, (_, manager) =>
    {
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        WindowManagerMutationScope.Settle(window);
        var content = Assert.IsAssignableFrom<Control>(window.Content);
        var root = Assert.Single(LogicalQuery.SelfAndDescendants<RootView>(content));
        Assert.Same(window, TopLevel.GetTopLevel(root));
        Assert.True(root.IsEffectivelyVisible);
        Assert.True(root.IsEffectivelyEnabled);
        Assert.True(root.Bounds.Width > 0);
        Assert.True(root.Bounds.Height > 0);
        Assert.Same(manager.Model, AppEnvironment.GetAppModel(root));
        Assert.NotNull(AppEnvironment.GetWindowLiveResize(root));
        manager.ShowMain();
        Assert.Same(root, Assert.Single(LogicalQuery.SelfAndDescendants<RootView>(Assert.IsAssignableFrom<Control>(window.Content))));
    }, useDefaultMain: true);
}

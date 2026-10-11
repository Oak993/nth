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
    // RESOLVED (RECORD != SWIFT, Swift wins): WIN.json PR-WIN-009's windows_path says absent metadata becomes "Marrow", but
    // Text.swift:4 falls back to "Droppy Code" and Core/Support/Text.cs:31-32 kept it; OD-0135 makes the fallback "nth". The fallback is unreachable in a build,
    // because the shipped assembly always carries its display name (AssemblyInfo.cs:4-8: "Marrow", OD-0002); the test below reads
    // that metadata, and the two tests under it protect the fallback through the real factory and the managed peer.
    [Fact]
    [Trait("Req", "PR-WIN-009")] // WindowManager.swift:63,97; preserve the existing Core fallback through the real factory.
    public void Missing_display_name_metadata_reaches_the_main_window_and_managed_peer_as_Nth() => WindowManagerMutationScope.Run(headless, (_, manager) =>
    {
        using var info = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?>(), isDevelopment: true));
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        Assert.Equal("nth", window.Title);
        var peer = ControlAutomationPeer.CreatePeerForElement(window);
        Assert.NotNull(peer);
        Assert.Equal("nth", peer.GetName());
    });

    [Fact]
    [Trait("Req", "PR-WIN-009")] // WindowManager.swift:63,97; a non-string is not a usable display name.
    public void Wrong_type_display_name_metadata_reaches_the_main_window_and_managed_peer_as_Nth() => WindowManagerMutationScope.Run(headless, (_, manager) =>
    {
        using var info = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?>
        {
            ["CFBundleDisplayName"] = false,
        }, isDevelopment: true));
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        Assert.Equal("nth", window.Title);
        var peer = ControlAutomationPeer.CreatePeerForElement(window);
        Assert.NotNull(peer);
        Assert.Equal("nth", peer.GetName());
    });

    // OD-0002 (the deviation of PR-WIN-009): the window text of the build that ships is the product's name, read from the
    // metadata the app assembly carries (the testhost's entry assembly is not the app's, so AppInfo.Current is not it).
    [Fact]
    [Trait("Req", "PR-WIN-009")] // WindowManager.swift:63, :97; Text.swift:4 through the shipped AssemblyInfo.cs
    public void The_shipped_assembly_metadata_names_the_product_and_titles_the_main_window_and_its_peer_with_it() => WindowManagerMutationScope.Run(headless, (_, manager) =>
    {
        var shipped = AppInfo.FromAssembly(typeof(Marrow.App.AppDelegate).Assembly, isDevelopment: true);
#if DEBUG
        const string product = "nth Dev"; // AssemblyInfo.cs:4-5, the development channel's display name (OD-0135)
#else
        const string product = "nth"; // AssemblyInfo.cs:7, OD-0002, OD-0135
#endif
        Assert.Equal(product, shipped.Name);
        using var info = AppInfo.Running.Override(shipped);
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        Assert.Equal(product, window.Title);
        var peer = ControlAutomationPeer.CreatePeerForElement(window);
        Assert.NotNull(peer);
        Assert.Equal(product, peer.GetName());
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
        // OD-0140: Settings is a panel named "Settings" in the main window, which keeps its own title
        Assert.Same(window, WindowManagerMutationScope.Settings(manager));
        Assert.Equal("Settings", Avalonia.Automation.AutomationProperties.GetName(manager.SettingsOverlay!.Card));
        Assert.Equal(displayName, window.Title);
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

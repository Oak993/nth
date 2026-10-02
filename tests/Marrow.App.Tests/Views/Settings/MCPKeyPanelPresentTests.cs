using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPKeyPanel.swift:12-19 present(entry:model:), against MCPSetupPanel.swift:20-33
// (the guard this panel does not have). The page the panel opens in the default browser is recorded by
// MCPSetupLauncherScope and never opened (R-TEST-10): the panel finds the browser through the application's desktop
// lifetime, so the scope stands a recording launcher behind it.
[Collection("Headless")]
public sealed class MCPKeyPanelPresentTests(HeadlessSession headless)
{
    private const string KeysPage = "https://keys.example.invalid/new";

    private static MCPCatalogEntry KeyEntry(string id = "keyed", string keysUrl = KeysPage) =>
        new(id, "Keyed", "Vendor", "Needs a key", MCPCategory.Developer, "", 0,
            new MCPTransport.Stdio("offline-fixture", ValueList<string>.Empty), "")
        {
            KeysURL = keysUrl,
            Fields = new ValueList<MCPField>([new MCPField("token", "Access token", "Paste the token", MCPField.Kind.Secret)]),
        };

    private static AvaloniaWindow PanelWindow() =>
        (AvaloniaWindow)typeof(FloatingGuidePanel).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(MCPKeyPanel.Shared)!;

    private void RunShared(Action<AppModel, AppModelHarness, MCPSetupLauncherScope> body) =>
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var browser = new MCPSetupLauncherScope();
            var model = harness.MakeModel();
            try { body(model, harness, browser); }
            finally
            {
                harness.Main.OnMain(() => MCPKeyPanel.Shared.Close());
                Dispatcher.UIThread.RunJobs();
                // The shared panel outlives a test: hand the next one the state of a panel never presented.
                var field = typeof(FloatingGuidePanel).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!;
                (field.GetValue(MCPKeyPanel.Shared) as AvaloniaWindow)?.Close();
                field.SetValue(MCPKeyPanel.Shared, null);
            }
        });

    private static void Present(AppModelHarness harness, AppModel model, MCPCatalogEntry entry) =>
        harness.Main.OnMain(() =>
        {
            MCPKeyPanel.Shared.Present(entry, model);
            Dispatcher.UIThread.RunJobs();
        });

    [Fact]
    [Trait("Req", "PR-MCP-214")] // MCPKeyPanel.swift:12-19: no re-entry guard, so every present opens the keys page again and builds the content again
    public void Presenting_the_same_entry_again_reopens_the_key_page_and_rebuilds_the_content()
    {
        RunShared((model, harness, browser) =>
        {
            var entry = KeyEntry();
            Present(harness, model, entry);
            var window = PanelWindow();
            var first = window.Content;
            Assert.IsType<MCPKeyPanelView>(first);
            Assert.Equal([new Uri(KeysPage)], browser.Launcher.Requests);

            // The same entry again (the card's Connect pressed twice): the page opens a second time and the view is a new one.
            Present(harness, model, entry);
            Assert.Equal([new Uri(KeysPage), new Uri(KeysPage)], browser.Launcher.Requests);
            Assert.Same(window, PanelWindow()); // one floating panel, reused
            Assert.True(window.IsVisible);
            var second = window.Content;
            Assert.IsType<MCPKeyPanelView>(second);
            Assert.NotSame(first, second);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-214")] // MCPKeyPanel.swift:12-19, :170-175 draftBinding: the new view reads the draft the store kept
    public void The_rebuilt_view_shows_what_was_typed_because_the_store_keeps_the_draft()
    {
        RunShared((model, harness, browser) =>
        {
            var entry = KeyEntry();
            Present(harness, model, entry);
            var typed = Field((Control)PanelWindow().Content!, "Paste the token");
            harness.Main.OnMain(() =>
            {
                typed.Text = "typed-key";
                Dispatcher.UIThread.RunJobs();
            });
            Assert.Equal("typed-key", model.Mcp.Value(entry.Fields[0], entry)); // the draft sits in the store, not in the view

            Present(harness, model, entry);

            // A different view object over the same store draft: Swift's TextField binds get: model.mcp.value(for:of:).
            var rebuilt = Field((Control)PanelWindow().Content!, "Paste the token");
            Assert.NotSame(typed, rebuilt);
            Assert.Equal("typed-key", rebuilt.Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-214")] // MCPKeyPanel.swift:12-14 and close(): nothing is remembered, so a present after a close, or for another entry, opens its page too
    public void A_present_after_close_and_a_present_for_another_entry_each_open_their_own_page()
    {
        RunShared((model, harness, browser) =>
        {
            var entry = KeyEntry();
            Present(harness, model, entry);
            harness.Main.OnMain(() => MCPKeyPanel.Shared.Close());
            Assert.False(PanelWindow().IsVisible);

            Present(harness, model, entry);
            Assert.True(PanelWindow().IsVisible);
            Assert.Equal(2, browser.Launcher.Requests.Count);

            var other = KeyEntry("other", "https://other.example.invalid/keys");
            Present(harness, model, other);
            Assert.Equal(
                [new Uri(KeysPage), new Uri(KeysPage), new Uri("https://other.example.invalid/keys")],
                browser.Launcher.Requests);
            Assert.NotNull(Nodes<TextBox>((Control)PanelWindow().Content!).FirstOrDefault());
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-214")] // MCPKeyPanel.swift:13: only an entry with a keysURL opens a page
    public void An_entry_without_a_keys_address_opens_no_page_but_still_shows_the_panel()
    {
        RunShared((model, harness, browser) =>
        {
            var entry = KeyEntry() with { KeysURL = null };
            Present(harness, model, entry);
            Present(harness, model, entry);
            Assert.Empty(browser.Launcher.Requests);
            Assert.True(PanelWindow().IsVisible);
        });
    }
}

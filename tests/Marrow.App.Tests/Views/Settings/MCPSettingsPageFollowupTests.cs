using System.Reflection;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPSettingsPage.swift (B3 52bc792), :19-74,284-290.
// STOP App.Views.Settings.MCPSettingsPage #4
// Kind: shared UI dependency not ported at the collection site.
// Swift: App/Views/Settings/MCPSettingsPage.swift:55-57.
// Records: UI-MCP-002; PR-MCP-203 (grid transition/spring only).
// Missing: ForEach.Apply (UI/Port/ViewLifetime.cs) inserts/removes Panel.Children directly; it does not
// consume SwiftTransition.Insert/Remove or animate the surviving grid's positions. Publishing the 0.35s
// transaction is insufficient to render the source scale(0.9)+opacity exit and grid layout spring.
// Needed: the shared UI owner supplies transition-aware keyed reconciliation/grid layout support, then
// this site attaches the source transition and receives frame-driven motion assertions. No shared UI
// file was changed. The 900ms celebration, chime, exclusions, live refresh and setup effects are tested.
[Collection("Headless")]
public sealed class MCPSettingsPageFollowupTests(HeadlessSession headless)
{
    private static void Publish(AppModel model, string id, MCPConnectionState state)
    {
        var property = typeof(MCPStore).GetProperty(nameof(MCPStore.States))!;
        Assert.NotNull(property.GetSetMethod(nonPublic: true));
        property.SetValue(model.Mcp, new Dictionary<string, MCPConnectionState>(model.Mcp.States, StringComparer.Ordinal) { [id] = state }); // completed offline probe through the observable setter
    }

    [Fact]
    [Trait("Req", "PR-MCP-203")] // App/Views/Settings/MCPSettingsPage.swift:64-74
    [Trait("Req", "PR-THEME-088")] // App/Views/Settings/MCPSettingsPage.swift:65-71
    [Trait("Req", "PR-MCP-200")] // App/Views/Settings/MCPSettingsPage.swift:19-23
    [Trait("Req", "PR-MCP-201")] // App/Views/Settings/MCPSettingsPage.swift:25-33
    public void A_new_connection_celebrates_for_900ms_then_moves_to_connected_and_chimes_with_settle_sound_off()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var h = new AppModelHarness(withRuntime: false);
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
            var sound = new MCPSettingsSilentSound(); using var audio = SoundOutput.Current.Override(sound);
            AppModel model = null!; MCPSettingsPage page = null!; IDisposable window = null!;
            runtime.OnMain(() => { model = new AppModel(); model.Settings.SettleSound = false; page = new MCPSettingsPage(""); window = Attach(page, model); });
            try
            {
                runtime.OnMain(() => Publish(model, "github", new MCPConnectionState.Connected(4)));
                runtime.OnMain(() => { Assert.Contains("Connected \u00B7 4 tools", Texts(page)); Assert.DoesNotContain(Nodes<MCPConnectedRow>(page), r => Texts(r).Contains("GitHub")); Assert.Equal(1, sound.Plays); }); // U+00B7 MIDDLE DOT
                runtime.AdvanceMilliseconds(899);
                runtime.OnMain(() => Assert.Contains("Connected \u00B7 4 tools", Texts(page))); // U+00B7 MIDDLE DOT
                runtime.AdvanceMilliseconds(1);
                runtime.OnMain(() => { Assert.Contains(Nodes<MCPConnectedRow>(page), r => Texts(r).Contains("GitHub")); Assert.DoesNotContain(Nodes<MCPServerCard>(page), c => Read<MCPCatalogEntry>(c, "entry").Id == "github"); Assert.Equal(1, sound.Plays); });
            }
            finally { runtime.OnMain(window.Dispose); }
        });
    }
    [Fact]
    [Trait("Req", "PR-MCP-203")] // App/Views/Settings/MCPSettingsPage.swift:64-74
    [Trait("Req", "PR-THEME-088")] // App/Views/Settings/MCPSettingsPage.swift:65-71
    public void Reduce_motion_suppresses_celebration_and_chime_and_initial_connections_do_not_replay()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var h = new AppModelHarness(withRuntime: false); using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var sound = new MCPSettingsSilentSound(); using var audio = SoundOutput.Current.Override(sound);
            AppModel model = null!; MCPSettingsPage page = null!; IDisposable window = null!;
            runtime.OnMain(() => { model = new AppModel(); State(model, "github", new MCPConnectionState.Connected(2)); page = new MCPSettingsPage(""); EnvironmentValues.SetAccessibilityReduceMotion(page, true); window = Attach(page, model); });
            try
            {
                Assert.Equal(0, sound.Plays);
                runtime.OnMain(() => Publish(model, "deepwiki", new MCPConnectionState.Connected(3)));
                runtime.OnMain(() => { Assert.Equal(0, sound.Plays); Assert.DoesNotContain(Texts(page), t => t.StartsWith("Connected \u00B7", StringComparison.Ordinal)); Assert.Contains(Nodes<MCPConnectedRow>(page), r => Texts(r).Contains("DeepWiki")); }); // U+00B7 MIDDLE DOT
            }
            finally { runtime.OnMain(window.Dispose); }
        });
    }
    [Fact]
    [Trait("Req", "PR-MCP-203")] // App/Views/Settings/MCPSettingsPage.swift:66-71
    [Trait("Req", "PR-THEME-088")] // App/Views/Settings/MCPSettingsPage.swift:66-68
    public void An_already_connected_state_change_does_not_chime_again_and_custom_ids_still_chime()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var h = new AppModelHarness(withRuntime: false); using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var sound = new MCPSettingsSilentSound(); using var audio = SoundOutput.Current.Override(sound); AppModel model = null!; IDisposable window = null!;
            runtime.OnMain(() => { model = new AppModel(); window = Attach(new MCPSettingsPage(""), model); });
            try { runtime.OnMain(() => Publish(model, "github", new MCPConnectionState.Connected(2))); runtime.OnMain(() => Publish(model, "github", new MCPConnectionState.Connected(3))); Assert.Equal(1, sound.Plays); runtime.OnMain(() => Publish(model, "custom-example", new MCPConnectionState.Connected(1))); Assert.Equal(2, sound.Plays); }
            finally { runtime.OnMain(window.Dispose); }
        });
    }
    [Fact]
    [Trait("Req", "PR-MCP-205")] // App/Views/Settings/MCPSettingsPage.swift:284-290
    public void A_live_needs_setup_transition_opens_the_panel_and_preserves_the_catalog_card()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var h = new AppModelHarness(withRuntime: false); using var frames = FrameClock.Source.Override(new ManualFrameSource());
            AppModel model = null!; MCPSettingsPage page = null!; MCPServerCard card = null!; IDisposable window = null!;
            var guide = new MCPSetupGuide("Enable it", "Setup is required.", new[] { "Enable the server." }, "Open settings", "", "Then check again.");
            runtime.OnMain(() => { model = new AppModel(); page = new MCPSettingsPage(""); window = Attach(page, model); card = Nodes<MCPServerCard>(page).Single(c => Read<MCPCatalogEntry>(c, "entry").Id == "github"); });
            try
            {
                runtime.OnMain(() => Publish(model, "github", new MCPConnectionState.NeedsSetup(guide)));
                runtime.OnMain(() => { Assert.Same(card, Nodes<MCPServerCard>(page).Single(c => Read<MCPCatalogEntry>(c, "entry").Id == "github")); Assert.Contains("Enable it", Texts(card)); Assert.True(MCPSetupPanel.Shared.IsPresenting(MCPCatalog.Entry("github")!)); });
            }
            finally { runtime.OnMain(() => { MCPSetupPanel.Shared.Close(); window.Dispose(); }); }
        });
    }
    [Fact]
    [Trait("Req", "PR-MCP-204")] // App/Views/Settings/MCPSettingsPage.swift:145-168
    [Trait("Req", "UI-MCP-009")] // App/Views/Settings/MCPSettingsPage.swift:159-167
    public void Connected_row_refreshes_version_when_connections_change_without_a_state_change()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var h = new AppModelHarness(withRuntime: false); AppModel model = null!; MCPConnectedRow row = null!; IDisposable window = null!;
            runtime.OnMain(() => { model = new AppModel(); State(model, "github", new MCPConnectionState.Connected(2)); row = new MCPConnectedRow(MCPCatalog.Entry("github")!); window = Attach(row, model); });
            try
            {
                runtime.OnMain(() => { var property = typeof(MCPStore).GetProperty(nameof(MCPStore.Connections))!; Assert.NotNull(property.GetSetMethod(true)); property.SetValue(model.Mcp, new Dictionary<string, MCPConnection>(model.Mcp.Connections) { ["github"] = new MCPConnection("github") { ServerVersion = "fixture-2", Tools = new[] { new MCPToolSummary("read", null), new MCPToolSummary("write", null) } } }); });
                runtime.OnMain(() => Assert.Contains("2 tools \u00B7 fixture-2", Texts(row))); // U+00B7 MIDDLE DOT
            }
            finally { runtime.OnMain(window.Dispose); }
        });
    }
    [Fact]
    [Trait("Req", "PR-MCP-073")] // App/Views/Settings/MCPSettingsPage.swift:564-566; Services/MCP/MCPStore.swift:186-193
    [Trait("Req", "PR-MCP-228")] // Services/MCP/MCPStore.swift:115-128; OD-0012 E6
    public void Confirmed_custom_delete_reports_a_refused_deletion_after_the_row_disappears()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var h = new AppModelHarness(withRuntime: false); using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
            var backend = new MCPSettingsRefusingCredentialBackend { Refuse = false, RefuseDeletes = true }; using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
            AppModel model = null!; MCPSettingsPage page = null!; IDisposable window = null!;
            runtime.OnMain(() => { model = new AppModel(); model.Settings.ConfirmBeforeDeleting = true; Assert.Null(model.Mcp.SaveCustom(Custom(1))); page = new MCPSettingsPage(""); window = Attach(page, model); });
            try
            {
                runtime.OnMain(() => { var row = Assert.Single(Nodes<MCPCustomRow>(page)); Call(row, "Delete"); var confirm = Read<Control>(row, "DeleteConfirmation"); using var confirmation = Attach(confirm, model); Click(Button(confirm, "Delete server")); });
                runtime.OnMain(() => { Assert.Empty(model.Mcp.Customs); Assert.Empty(Nodes<MCPCustomRow>(page)); Assert.Contains("Disconnected, but Credential Manager did not remove this server\u2019s stored values.", Texts(page)); }); // U+2019 RIGHT SINGLE QUOTATION MARK
            }
            finally { runtime.OnMain(window.Dispose); }
        });
    }
}

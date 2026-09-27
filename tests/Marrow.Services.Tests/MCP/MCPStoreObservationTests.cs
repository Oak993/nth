using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.MCP;

// Source: DroppyCode/Services/MCP/MCPStore.swift (B3 52bc792), :8-351.
// Follow-up rulings: all observed runtime writes notify; constructors remain silent; no real credentials/probes.
[Collection(MCPProxyCollection.Name)]
public sealed class MCPStoreObservationTests
{
    private const string Failure = "Disconnected, but Credential Manager did not remove this server\u2019s stored values."; // U+2019 RIGHT SINGLE QUOTATION MARK
    private static MCPCustomServer Custom() => new("custom-example", "Example", MCPCustomServer.Kind.Stdio)
    { Command = "offline-fixture", Env = new[] { new MCPCustomServer.Variable("MODE") } };
    private static string? Report(MCPStore store) => (string?)typeof(MCPStore).GetProperty("CredentialDeletionFailure")?.GetValue(store);

    [Fact]
    [Trait("Req", "PR-MCP-060")] // Services/MCP/MCPStore.swift:14,179
    public void States_tracker_and_state_computation_are_notified_without_mutating_the_previous_snapshot()
    {
        using var h = new MCPStoreHarness();
        h.Runtime.OnMain(() =>
        {
            var store = new MCPStore(); var custom = Custom(); Assert.Null(store.SaveCustom(custom));
            Assert.True(store.SaveCustomValues(new[] { ("MODE", "sample-mode", false) }, custom.Id, null));
            var backing = (Dictionary<string, MCPConnectionState>)typeof(MCPStore).GetField("statesField", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
            backing[custom.Id] = new MCPConnectionState.Connected(2); // cached-state arrangement, before tracking
            var snapshot = store.States; var notices = 0;
            var tracking = ObservationTracking.Track(() => (store.States, store.State(custom.Id)), () => notices++);
            try { Assert.Null(store.SaveCustom(custom, custom.Id)); Assert.Equal(1, notices); Assert.IsType<MCPConnectionState.NotConnected>(store.State(custom.Id)); Assert.IsType<MCPConnectionState.Connected>(snapshot[custom.Id]); }
            finally { tracking.Tracker.Cancel(); }
        });
    }
    [Fact]
    [Trait("Req", "PR-MCP-059")] // Services/MCP/MCPStore.swift:12,209-234
    public void Connections_tracker_and_connection_lookup_are_notified_when_plain_values_are_saved()
    {
        using var h = new MCPStoreHarness();
        h.Runtime.OnMain(() =>
        {
            var store = new MCPStore(); var snapshot = store.Connections; var notices = 0;
            var tracking = ObservationTracking.Track(() => (store.Connections, store.Connection("custom-example")), () => notices++);
            try { Assert.True(store.SaveCustomValues(new[] { ("MODE", "sample-mode", false) }, "custom-example", null)); Assert.Equal(1, notices); Assert.Equal("sample-mode", store.Connection("custom-example")!.Values["MODE"]); Assert.Empty(snapshot); }
            finally { tracking.Tracker.Cancel(); }
        });
    }
    [Fact]
    [Trait("Req", "PR-MCP-053")] // Services/MCP/MCPStore.swift:16,67-69
    public void Drafts_tracker_and_field_value_are_notified_when_a_nested_draft_changes()
    {
        using var h = new MCPStoreHarness();
        h.Runtime.OnMain(() =>
        {
            var store = new MCPStore(); var entry = Custom().CatalogEntry(); var field = entry.Fields[0]; store.SetDraft("first", field, entry);
            var snapshot = store.Drafts; var notices = 0;
            var tracking = ObservationTracking.Track(() => (store.Drafts, store.Value(field, entry)), () => notices++);
            try { store.SetDraft("second", field, entry); Assert.Equal(1, notices); Assert.Equal("second", store.Value(field, entry)); Assert.Equal("first", snapshot[entry.Id][field.Key]); }
            finally { tracking.Tracker.Cancel(); }
        });
    }
    [Fact]
    [Trait("Req", "PR-MCP-051")] // Services/MCP/MCPStore.swift:18,51-54,150-169
    public void Customs_tracker_and_entry_lookup_are_notified_when_a_definition_is_added()
    {
        using var h = new MCPStoreHarness();
        h.Runtime.OnMain(() =>
        {
            var store = new MCPStore(); var custom = Custom(); var snapshot = store.Customs; var notices = 0;
            var tracking = ObservationTracking.Track(() => (store.Customs, store.Entry(custom.Id)), () => notices++);
            try { Assert.Null(store.SaveCustom(custom)); Assert.Equal(1, notices); Assert.Equal(custom.Name, store.Entry(custom.Id)!.Name); Assert.Empty(snapshot); }
            finally { tracking.Tracker.Cancel(); }
        });
    }
    [Fact]
    [Trait("Req", "PR-MCP-053")] // Services/MCP/MCPStore.swift:8,16,67-69
    public void An_off_main_draft_assignment_traps_instead_of_bypassing_isolation()
    {
        using var h = new MCPStoreHarness(); var store = h.Runtime.OnMain(() => new MCPStore()); var entry = Custom().CatalogEntry();
        // R-OBS-6 / ED-065: MainActor.AssumeIsolated and ObservationTests.A_main_actor_model_mutated_off_main_traps use SwiftTrapException.
        Assert.Throws<SwiftTrapException>(() => store.SetDraft("sample-mode", entry.Fields[0], entry));
        Assert.Empty(store.Drafts);
    }
    [Fact]
    [Trait("Req", "PR-MCP-228")] // Services/MCP/MCPKeychain.swift:52-67; Services/MCP/MCPStore.swift:115-128
    [Trait("Req", "PR-MCP-073")] // Services/MCP/MCPStore.swift:186-193
    public void Refused_credential_deletion_is_reported_but_custom_definition_connection_and_draft_are_removed()
    {
        using var h = new MCPStoreHarness();
        h.Runtime.OnMain(() =>
        {
            var store = new MCPStore(); var custom = Custom() with { Env = new[] { new MCPCustomServer.Variable("TOKEN", true) } }; Assert.Null(store.SaveCustom(custom));
            var entry = store.Entry(custom.Id)!; MCPKeychain.Set("made-up", entry.Id, "TOKEN");
            Assert.True(store.SaveCustomValues(new[] { ("MODE", "sample-mode", false) }, entry.Id, null)); store.SetDraft("draft", entry.Fields[0], entry);
            h.Backend.RefusedDeleteTargets.Add(SecretStore.TargetName(AppInfo.Current.Name, "MCP " + entry.Id + " TOKEN"));
            store.DeleteCustom(custom);
            Assert.Equal(Failure, Report(store)); Assert.Null(store.Connection(entry.Id)); Assert.IsType<MCPConnectionState.NotConnected>(store.State(entry.Id)); Assert.Empty(store.Customs); Assert.Empty(store.Drafts); Assert.Equal("made-up", MCPKeychain.Value(entry.Id, "TOKEN"));
        });
    }
    [Theory]
    [InlineData("oauth")]
    [InlineData("oauth-client")]
    [Trait("Req", "PR-MCP-228")] // Services/MCP/MCPStore.swift:118-127; Services/MCP/MCPOAuth.swift:189-191
    public void OAuth_signout_deletion_failures_are_included_and_every_item_is_attempted(string refusedField)
    {
        using var h = new MCPStoreHarness();
        h.Runtime.OnMain(() =>
        {
            var store = new MCPStore(); var entry = MCPCatalog.Entry("gitlab")!;
            MCPKeychain.Set("made-up", entry.Id, "oauth"); MCPKeychain.Set("made-up", entry.Id, "oauth-client");
            h.Backend.RefusedDeleteTargets.Add(SecretStore.TargetName(AppInfo.Current.Name, "MCP " + entry.Id + " " + refusedField));
            store.Disconnect(entry); Assert.Equal(Failure, Report(store)); Assert.Equal("made-up", MCPKeychain.Value(entry.Id, refusedField)); Assert.Null(MCPKeychain.Value(entry.Id, refusedField == "oauth" ? "oauth-client" : "oauth")); Assert.Null(store.Connection(entry.Id));
        });
    }
    [Fact]
    [Trait("Req", "PR-MCP-228")] // Services/MCP/MCPKeychain.swift:52-67; Services/MCP/MCPStore.swift:115-128
    public void Missing_credentials_and_an_entry_with_no_fields_are_successful_disconnects()
    {
        using var h = new MCPStoreHarness(); h.Runtime.OnMain(() => { var store = new MCPStore(); store.Disconnect(Custom().CatalogEntry()); Assert.Null(Report(store)); var empty = Custom() with { Env = ValueList<MCPCustomServer.Variable>.Empty }; store.Disconnect(empty.CatalogEntry()); Assert.Null(Report(store)); });
    }
}

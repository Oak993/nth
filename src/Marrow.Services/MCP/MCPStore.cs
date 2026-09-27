using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Models;
using Marrow.Services.Port;

// Source: DroppyCode/Services/MCP/MCPStore.swift (B3 52bc792), :1-351. Records PR-MCP-049 to PR-MCP-072 (this
// part) and PR-MCP-073 and up (load/save/deleteCustom/storedValue/saveCustomValues, part 2 of this unit); this
// file implements the whole class so it compiles and the part-1 records behave correctly end to end.
//
// B3 delta (lane A17, R3-47): allEntries (B2 lines 51-56) is deleted, having had no caller (PR-MCP-050
// retired); every citation at or after B2 line 57 shifts -6.
namespace Marrow.Services.MCP;

// :8-10 @MainActor @Observable final class MCPStore (R-OBS-6)
public sealed class MCPStore : MainActorModel
{
    // :12 private(set) var connections: [String: MCPConnection] = [:]
    private Dictionary<string, MCPConnection> connectionsField = new(StringComparer.Ordinal);

    // :14 private(set) var states: [String: MCPConnectionState] = [:]
    private Dictionary<string, MCPConnectionState> statesField = new(StringComparer.Ordinal);

    // :16 var drafts: [String: [String: String]] = [:]. Keep the public dictionary API; runtime writes copy
    // both dictionary levels on reads and assignments (Swift value semantics), then use the tracked setter (R-OBS-1/3/6).
    private Dictionary<string, Dictionary<string, string>> draftsField = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, string>> Drafts { get => DraftSnapshot(Read(draftsField)); set => Set(ref draftsField, DraftSnapshot(value)); }
    private static Dictionary<string, Dictionary<string, string>> DraftSnapshot(Dictionary<string, Dictionary<string, string>> value) =>
        value.ToDictionary(pair => pair.Key, pair => new Dictionary<string, string>(pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);

    // :18 private(set) var customs: [MCPCustomServer] = []
    private List<MCPCustomServer> customsField = [];

    // :20 @ObservationIgnored private var probes: [String: Task<Void, Never>] = [:] (R-TASK-1)
    private readonly Dictionary<string, TaskHandle> probes = new(StringComparer.Ordinal);

    // :22-28
    public MCPStore()
    {
        Load(); // :23
        // :24-25 Up before anything resolves a route to it: the probe, the hub and every launch reach
        // signed-in servers through the proxy.
        MCPProxy.Shared.Current.Start(); // :26
        ExportAll(); // :27
    }

    // MARK: Reading (:30)

    // :11-18. Every runtime mutation replaces a snapshot through Set; only Load (constructor) writes fields.
    public IReadOnlyDictionary<string, MCPConnection> Connections { get => Read(connectionsField); private set => Set(ref connectionsField, new Dictionary<string, MCPConnection>(value, StringComparer.Ordinal)); }
    public IReadOnlyDictionary<string, MCPConnectionState> States { get => Read(statesField); private set => Set(ref statesField, new Dictionary<string, MCPConnectionState>(value, StringComparer.Ordinal)); }
    public IReadOnlyList<MCPCustomServer> Customs { get => Read(customsField); private set => Set(ref customsField, new List<MCPCustomServer>(value)); }

    // PORT-ONLY: OD-0008 A12 / OD-0012 E6, PR-MCP-228. The report outlives the disconnected/deleted row.
    // ED-221: own the page-level report above its footer; the next disconnect/deletion replaces or clears it.
    private string? credentialDeletionFailure;
    public string? CredentialDeletionFailure { get => Read(credentialDeletionFailure); private set => Set(ref credentialDeletionFailure, value); }
    private const string CredentialDeletionFailureText = "Disconnected, but Credential Manager did not remove this server\u2019s stored values."; // U+2019 RIGHT SINGLE QUOTATION MARK

    // :32-34
    public MCPConnectionState State(string id) =>
        States.TryGetValue(id, out var state) ? state : new MCPConnectionState.NotConnected();

    // :36-38
    public MCPConnection? Connection(string id) => Connections.TryGetValue(id, out var connection) ? connection : null;

    /// :40-49 Connected and enabled servers, resolved with their field values: what providers get.
    public IReadOnlyList<MCPResolvedServer> EnabledServers => Computed(() => Connections.Values
        .Where(connection => connection.IsEnabled && connection.ConnectedAt is not null) // :43
        .Select(connection => // :44-47
        {
            var entry = Entry(connection.CatalogID);
            return entry is null ? null : MCPResolver.Resolve(entry, StoredValues(entry), MCPProxy.Url);
        })
        .Where(resolved => resolved is not null)
        .Select(resolved => resolved!)
        .OrderBy(resolved => resolved.Id, SwiftStringComparer.Instance) // :48, R-COLL-1
        .ToList());

    /// :51-54 The entry an id names: a custom first, then the catalog.
    public MCPCatalogEntry? Entry(string id) => // :52-54
        Customs.FirstOrDefault(custom => SwiftString.Equal(custom.Id, id))?.CatalogEntry() ?? MCPCatalog.Entry(id);

    /// :56-57 The value the card shows for a field: the draft, else the stored value (secrets from the store).
    public string Value(MCPField field, MCPCatalogEntry entry) // :57-65
    {
        if (Drafts.TryGetValue(entry.Id, out var entryDrafts) && entryDrafts.TryGetValue(field.Key, out var draft))
            return draft; // :58
        if (field.FieldKind == MCPField.Kind.Secret) // :59
        {
            if (MCPKeychain.Value(entry.Id, field.Key) is { } secret) return secret; // :60
        }
        else if (Connection(entry.Id)?.Values is { } values && values.TryGetValue(field.Key, out var stored)) // :61
        {
            return stored; // :62
        }
        return field.DefaultValue ?? ""; // :64
    }

    // :67-69
    public void SetDraft(string value, MCPField field, MCPCatalogEntry entry)
    {
        MainActor.AssumeIsolated(); // :8 @MainActor, before any mutation
        var next = new Dictionary<string, Dictionary<string, string>>(Drafts, StringComparer.Ordinal);
        var entryDrafts = Drafts.TryGetValue(entry.Id, out var old) ? new Dictionary<string, string>(old, StringComparer.Ordinal) : new Dictionary<string, string>(StringComparer.Ordinal);
        entryDrafts[field.Key] = value; next[entry.Id] = entryDrafts; Drafts = next; // :68, default: [:]
    }

    /// :71-74 Whether every required field of the entry has a value (draft or stored).
    public bool CanConnect(MCPCatalogEntry entry) => // :72-74
        entry.Fields.Where(field => field.IsRequired).All(field => Value(field, entry).Length > 0);

    // MARK: Connecting (:76)

    /// :78-112 Stores the drafts (secrets to protected storage), probes the server, and on success marks it
    /// connected with its tools; on failure keeps the values and records the message.
    public void Connect(MCPCatalogEntry entry) // :80
    {
        MainActor.AssumeIsolated(); // :8, before credential/process side effects
        if (probes.Remove(entry.Id, out var existing)) existing.Cancel(); // :81

        var connection = Connection(entry.Id) ?? new MCPConnection(entry.Id); // :82
        connection = connection with { IsEnabled = true }; // :83

        // :84-90 A sign-in belongs to one address: pointing the server elsewhere (another GitLab instance)
        // drops the old token so the browser flow runs against the new one.
        if (entry.IsOAuth && Drafts.TryGetValue(entry.Id, out var addressDraft)) // :86
        {
            var before = entry.OauthURL(connection.Values)!; // :87
            var merged = connection.Values.Mutating(map => { foreach (var pair in addressDraft) map[pair.Key] = pair.Value; }); // :87 merging(draft) { $1 }
            var after = entry.OauthURL(merged)!; // :87
            if (!SwiftString.Equal(before, after)) // :87
            {
                MCPOAuth.SignOut(entry.Id); // :88
                MCPProxy.Shared.Current.Unregister(entry.Id); // :89
            }
        }

        // :91-96 A Try again after a rejection must not reuse the rejected token: a failed OAuth entry
        // starts signed out so the browser flow runs afresh.
        if (entry.IsOAuth && State(entry.Id) is MCPConnectionState.Failed) // :93
        {
            MCPOAuth.SignOut(entry.Id); // :94
            MCPProxy.Shared.Current.Unregister(entry.Id); // :95
        }

        if (Drafts.TryGetValue(entry.Id, out var draft)) // :97
        {
            foreach (var field in entry.Fields) // :98
            {
                if (!draft.TryGetValue(field.Key, out var text)) continue; // :99
                if (field.FieldKind == MCPField.Kind.Secret) // :100
                    MCPKeychain.Set(text, entry.Id, field.Key); // :101
                else
                    connection = connection with { Values = connection.Values.Mutating(map => map[field.Key] = text) }; // :103
            }
        }

        Connections = Setting(Connections, entry.Id, connection); // :107
        Drafts = Removing(Drafts, entry.Id); // :108
        Save(); // :109
        States = Setting(States, entry.Id, new MCPConnectionState.Connecting()); // :110
        StartProbe(entry); // :111
    }

    /// :114-128 Forgets the connection, its values and its secrets, and removes it from every provider config.
    public void Disconnect(MCPCatalogEntry entry) // :115
    {
        MainActor.AssumeIsolated(); // :8, before credential deletions
        if (probes.Remove(entry.Id, out var existing)) existing.Cancel(); // :116-117
        var removed = MCPKeychain.DeleteAll(entry); // :118, DEVIATION OD-0008 A12 / PR-MCP-228
        if (entry.IsOAuth) // :119
        {
            removed = MCPOAuth.SignOut(entry.Id) && removed; // :120, PR-MCP-228 includes both OAuth items; do not short-circuit sign-out
            MCPProxy.Shared.Current.Unregister(entry.Id); // :121
        }
        Connections = Removing(Connections, entry.Id); // :123
        States = Removing(States, entry.Id); // :124
        Drafts = Removing(Drafts, entry.Id); // :125
        Save(); // :126
        ExportAll(); // :127
        CredentialDeletionFailure = removed ? null : CredentialDeletionFailureText; // OD-0012 E6; disconnect still completes
    }

    /// :130-135 Re-probes a connected server without changing its values.
    public void Refresh(MCPCatalogEntry entry) // :131
    {
        MainActor.AssumeIsolated(); // :8
        if (probes.Remove(entry.Id, out var existing)) existing.Cancel(); // :132
        States = Setting(States, entry.Id, new MCPConnectionState.Connecting()); // :133
        StartProbe(entry); // :134
    }

    // :137-143
    public void SetEnabled(bool enabled, MCPCatalogEntry entry)
    {
        MainActor.AssumeIsolated(); // :8
        if (Connection(entry.Id) is not { } connection) return; // :138
        connection = connection with { IsEnabled = enabled }; // :139
        Connections = Setting(Connections, entry.Id, connection); // :140
        Save(); // :141
        ExportAll(); // :142
    }

    // MARK: Custom servers (:145)

    /// :147-184 Adds or replaces a custom server. Validates first; the error, when there is one, is for the
    /// editor to show and nothing is written.
    public MCPCustomError? SaveCustom(MCPCustomServer server, string? replacing = null) // :150
    {
        MainActor.AssumeIsolated(); // :8
        var old = replacing is { } oldID ? Customs.FirstOrDefault(custom => SwiftString.Equal(custom.Id, oldID)) : null; // :151
        var taken = new HashSet<string>(MCPCatalog.Entries.Select(entry => entry.Id), SwiftStringComparer.Instance); // :152
        foreach (var custom in Customs) taken.Add(custom.Id); // :153
        if (old is not null) taken.Remove(old.Id); // :154
        server = server with { Name = server.Name.Trimmed() }; // :155-156
        // :157-159 The id is made once, at creation, from the first name; a rename never moves it, so the
        // connection and the store's secrets stay where they are.
        server = server with { Id = old?.Id ?? MCPCustomServer.MakeID(server.Name) }; // :159
        if (MCPCustomError.Validate(server, taken) is { } error) return error; // :160

        if (probes.Remove(server.Id, out var existing)) existing.Cancel(); // :161-162
        Drafts = Removing(Drafts, server.Id); // :163
        var nextCustoms = Customs.ToList(); var index = nextCustoms.FindIndex(custom => SwiftString.Equal(custom.Id, server.Id)); // :164
        if (index >= 0) nextCustoms[index] = server; // :164-165
        else nextCustoms.Add(server); // :167
        Customs = nextCustoms;
        SaveCustoms(); // :169

        // :170-173 The definition changed under any connection it had: the tools and the provider configs
        // still describe the old command, address or variables, so both go now. The server leaves the
        // providers' configs (it is no longer connected) until the editor's re-probe succeeds, and the probe
        // runs after the values are saved.
        if (Connection(server.Id) is { } connection) // :174
        {
            connection = connection with { ConnectedAt = null, ServerVersion = null, Tools = ValueList<MCPToolSummary>.Empty }; // :175-177
            Connections = Setting(Connections, server.Id, connection); // :178
            States = Setting(States, server.Id, new MCPConnectionState.NotConnected()); // :179
            Save(); // :180
            ExportAll(); // :181
        }
        return null; // :183
    }

    /// :186-193 Removes a custom server: its connection and secrets go with it.
    public void DeleteCustom(MCPCustomServer server)
    {
        MainActor.AssumeIsolated(); // :8
        if (Customs.FirstOrDefault(custom => SwiftString.Equal(custom.Id, server.Id)) is { } entry) // :188
            Disconnect(entry.CatalogEntry()); // :189
        Customs = Customs.Where(custom => !SwiftString.Equal(custom.Id, server.Id)).ToList(); // :191
        SaveCustoms(); // :192
    }

    /// :195-200 A variable's stored value, for the editor to prefill: the store's when the variable is
    /// secret, the connection's otherwise.
    public string? StoredValue(string id, string field, bool secret) =>
        secret ? MCPKeychain.Value(id, field) : Connection(id)?.Values.GetValueOrDefault(field); // :198-199

    /// :202-235 Saves a custom's variable values where connect would put them: plain ones in the connection
    /// (mcp.json), secret ones in protected storage. A variable that moved between the two keeps its saved
    /// value and the stale copy in the other store is removed. Empty values clear the field; the editor
    /// supplies the stored value for fields the user has not changed.
    public bool SaveCustomValues(IReadOnlyList<(string Key, string Value, bool IsSecret)> values, string id, MCPCatalogEntry? replacingFieldsOf) // :209
    {
        MainActor.AssumeIsolated(); // :8, before credential writes
        foreach (var item in values.Where(item => item.IsSecret)) // :210
            if (!MCPKeychain.Set(item.Value, id, item.Key)) return false; // :211

        var kept = new HashSet<string>(values.Select(item => item.Key), SwiftStringComparer.Instance); // :213
        var connection = Connection(id) ?? new MCPConnection(id); // :214

        // :216-218 Fields the edit no longer declares: their plain values go, their secrets too.
        foreach (var field in (replacingFieldsOf?.Fields ?? ValueList<MCPField>.Empty).Where(field => !kept.Contains(field.Key)))
            MCPKeychain.Delete(id, field.Key); // :217

        connection = connection with { Values = new ValueMap<string, string>(connection.Values.Where(pair => kept.Contains(pair.Key)), SwiftStringComparer.Instance) }; // :219

        foreach (var item in values) // :220
        {
            var resolved = item.Value; // :221
            if (item.IsSecret) // :222
            {
                connection = connection with { Values = connection.Values.Mutating(map => map.Remove(item.Key)) }; // :223
            }
            else
            {
                connection = connection with
                {
                    Values = resolved.Length == 0
                        ? connection.Values.Mutating(map => map.Remove(item.Key)) // :225 nil clears it
                        : connection.Values.Mutating(map => map[item.Key] = resolved),
                };
                MCPKeychain.Delete(id, item.Key); // :226
            }
        }

        // :229-230 A server whose values are all empty needs no connection yet: it gets one on connect.
        if (Connection(id) is not null || connection.Values.Count > 0) // :230
        {
            Connections = Setting(Connections, id, connection); // :231
            Save(); // :232
        }
        return true; // :234
    }

    // MARK: Persistence (:237)

    // PORT-ONLY copy/modify helpers for Swift dictionary value mutations. They never write a model field.
    private static Dictionary<string, T> Setting<T>(IReadOnlyDictionary<string, T> source, string key, T value) => new(source, StringComparer.Ordinal) { [key] = value };
    private static Dictionary<string, T> Removing<T>(IReadOnlyDictionary<string, T> source, string key)
    {
        var next = new Dictionary<string, T>(source, StringComparer.Ordinal); next.Remove(key); return next;
    }

    private void Load() // :239-254
    {
        if (SwiftTry.Optional(() => File.ReadAllBytes(MCPPaths.CustomsURL)) is { } customData && // :240
            SwiftTry.Optional(() => JsonSerializer.Deserialize<List<MCPCustomServer>>(customData, CustomOptions)) is { } list) // :241
        {
            customsField = list; // :242
        }
        var data = SwiftTry.Optional(() => File.ReadAllBytes(MCPPaths.ConnectionsURL)); // :244
        if (data is null) return;
        var connections = SwiftTry.Optional(() => JsonSerializer.Deserialize<List<MCPConnection>>(data, ConnectionOptions)); // :247
        if (connections is null) return;
        foreach (var connection in connections) // :248
        {
            connectionsField[connection.CatalogID] = connection; // :249
            if (connection.ConnectedAt is not null) // :250
                statesField[connection.CatalogID] = new MCPConnectionState.Connected(connection.Tools.Count); // :251
        }
    }

    private void Save() // :256-263
    {
        var list = connectionsField.Values.OrderBy(connection => connection.CatalogID, SwiftStringComparer.Instance).ToList(); // :260
        var data = SwiftTry.Optional(() => SwiftJson.Encode(list, ConnectionOptions, SwiftJsonStyle.PrettySorted)); // :257-261
        if (data is null) return;
        SwiftTry.Ignore(() => FileOps.WriteAtomic(MCPPaths.ConnectionsURL, data)); // :262
    }

    private void SaveCustoms() // :265-270
    {
        var data = SwiftTry.Optional(() => SwiftJson.Encode(customsField, CustomOptions, SwiftJsonStyle.PrettySorted)); // :267-268
        if (data is null) return;
        SwiftTry.Ignore(() => FileOps.WriteAtomic(MCPPaths.CustomsURL, data)); // :269
    }

    private static readonly JsonSerializerOptions ConnectionOptions = new() { Converters = { new MCPConnectionConverter() } };
    private static readonly JsonSerializerOptions CustomOptions = new() { Converters = { new MCPCustomServerConverter() } };

    /// :272-284 Rewrites every provider's config from enabledServers and runs the one-time external-CLI cleanup.
    private void ExportAll() // :273
    {
        var servers = EnabledServers; // :274
        // :275-276 The proxy's routes follow the enabled list: one per signed-in server.
        MCPProxy.Shared.Current.ReplaceAll(servers // :276-279
            .Where(server => server.OauthUpstream is not null)
            .Select(server => (ServerID: server.Id, Upstream: server.OauthUpstream!, Headers: (IReadOnlyDictionary<string, string>)ValueMap<string, string>.Empty, UsesOAuth: true))
            .ToList());
        MCPProviderConfig.WriteAll(servers); // :280
        MCPExternalSync.Sync(servers); // :281
        // :282-283 The in-app clients (API providers) pick the new list up on their next use.
        MainActor.Spawn(async () => await MCPHub.Shared.Reload());
    }

    /// :286-295 Non-secret values from the connection, overlaid with the store's secrets.
    private ValueMap<string, string> StoredValues(MCPCatalogEntry entry) // :287
    {
        var values = Connection(entry.Id)?.Values ?? ValueMap<string, string>.Empty; // :288
        foreach (var field in entry.Fields.Where(field => field.FieldKind == MCPField.Kind.Secret)) // :289
            if (MCPKeychain.Value(entry.Id, field.Key) is { } secret) // :290
                values = values.Mutating(map => map[field.Key] = secret); // :291
        return values; // :294
    }

    /// :297-308 A server that signs in: the browser flow first (Marrow's own, with its branded page), then
    /// its route on the proxy, and only then the probe, which goes through that route like every provider will.
    private static async Task<MCPProbeResult> ProbeOnce(MCPCatalogEntry entry, MCPResolvedServer resolved) // :297
    {
        if (resolved.OauthUpstream is { } remote) // :301
        {
            if (!MCPOAuth.IsSignedIn(entry.Id)) // :302
                await MCPOAuth.SignIn(entry.Id, entry.Name, remote); // :303
            MCPProxy.Shared.Current.Register(entry.Id, remote, ValueMap<string, string>.Empty, usesOAuth: true); // :305
        }
        return await MCPProbe.Probe(resolved); // :307
    }

    private void StartProbe(MCPCatalogEntry entry) // :310-349
    {
        probes[entry.Id] = MainActor.Spawn(async () =>
        {
            try
            {
                var resolved = MCPResolver.Resolve(entry, StoredValues(entry), MCPProxy.Url); // :313
                MCPProbeResult result; // :314
                try
                {
                    result = await ProbeOnce(entry, resolved); // :316
                }
                catch (MCPProbeError e) when (e.ErrorCase is MCPProbeError.Case.UnauthorizedOAuth && resolved.OauthUpstream is not null) // :317
                {
                    SwiftTask.CheckCancellation(); // :318
                    MCPOAuth.SignOut(entry.Id); // :319
                    MCPProxy.Shared.Current.Unregister(entry.Id); // :320
                    result = await ProbeOnce(entry, resolved); // :321
                }
                catch (MCPProbeError e) when (e.ErrorCase is MCPProbeError.Case.Http(401 or 403, _) && resolved.OauthUpstream is not null) // :322
                {
                    SwiftTask.CheckCancellation(); // :323
                    MCPOAuth.SignOut(entry.Id); // :324
                    MCPProxy.Shared.Current.Unregister(entry.Id); // :325
                    result = await ProbeOnce(entry, resolved); // :326
                }
                SwiftTask.CheckCancellation(); // :328
                if (Connection(entry.Id) is { } connection) // :329
                {
                    connection = connection with
                    {
                        ConnectedAt = SwiftDate.Now, // :330
                        ServerVersion = result.ServerVersion, // :331
                        Tools = result.Tools, // :332
                    };
                    Connections = Setting(Connections, entry.Id, connection); // :333
                }
                States = Setting(States, entry.Id, new MCPConnectionState.Connected(result.Tools.Count)); // :335
                Save(); // :336
                ExportAll(); // :337
            }
            catch (CancellationError) // :338
            {
            }
            catch (MCPProbeError e) when (e.ErrorCase is MCPProbeError.Case.NeedsSetup(var guide)) // :339
            {
                if (SwiftTask.IsCancelled) return; // :340
                // :341-342 The token is good; the server needs a step on its side. Keep the sign-in so Check
                // again re-probes without another browser round trip.
                States = Setting(States, entry.Id, new MCPConnectionState.NeedsSetup(guide)); // :343
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error)) // :344
            {
                if (SwiftTask.IsCancelled) return; // :345
                States = Setting(States, entry.Id, new MCPConnectionState.Failed(SwiftErrors.LocalizedDescription(error))); // :346
            }
        });
    }
}

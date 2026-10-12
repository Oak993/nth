using System.Linq;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Models;

// Source: DroppyCode/Services/MCP/MCPProviderConfig.swift (B3 52bc792), :1-245. B3 delta (lane A17, R3-46):
// connectedIDs (B2 lines 230-236) is deleted, having had no caller (PR-MCP-172 reclassified: there is no
// fourth accessor); every citation at or after B2 line 238 shifts -8.
//
// ED-177 RESUME: Services.Store.Storage merged, so Marrow.Services.Models.MCPPaths (src/Marrow.Services/Models/MCP.cs)
// now builds its paths from Storage.Root instead of throwing. This part completes WriteAll, AcpServers,
// CodexOverrides and the four accessors (PR-MCP-169 to PR-MCP-172, and the rest of PR-MCP-171) on top of it.

namespace Marrow.Services.MCP;

public static class MCPProviderConfig
{
    // :4-28. PR-MCP-165. Remote entries carry "type": "http" plus "headers" only when non-empty; local entries
    // carry no "type" key at all, plus "env" only when non-empty. A missing url/command becomes "" rather than
    // being skipped (upstream defect 23, reproduced).
    public static JSONValue Claude(IReadOnlyList<MCPResolvedServer> servers) // :4
    {
        var entries = new Dictionary<string, JSONValue>();
        foreach (var server in servers) // :6
        {
            if (server.IsRemote) // :7
            {
                var entry = new Dictionary<string, JSONValue>
                {
                    ["type"] = "http", // :9
                    ["url"] = server.Url ?? "", // :10
                };
                if (server.Headers.Count > 0) entry["headers"] = MapOf(server.Headers); // :12-14
                entries[server.Id] = ObjectOf(entry); // :15
            }
            else
            {
                var entry = new Dictionary<string, JSONValue>
                {
                    ["command"] = server.Command ?? "", // :18
                    ["args"] = ArrayOf(server.Args), // :19
                };
                if (server.Env.Count > 0) entry["env"] = MapOf(server.Env); // :21-23
                entries[server.Id] = ObjectOf(entry); // :24
            }
        }
        return ObjectOf(new Dictionary<string, JSONValue> { ["mcpServers"] = ObjectOf(entries) }); // :27
    }

    // :30-51. PR-MCP-166. Snake-case root key "mcp_servers"; remote entries carry "http_headers" (never "type")
    // only when headers are non-empty; local entries are identical in shape to Claude's.
    public static JSONValue Codex(IReadOnlyList<MCPResolvedServer> servers) // :30
    {
        var entries = new Dictionary<string, JSONValue>();
        foreach (var server in servers) // :32
        {
            if (server.IsRemote) // :33
            {
                var entry = new Dictionary<string, JSONValue> { ["url"] = server.Url ?? "" }; // :34
                if (server.Headers.Count > 0) entry["http_headers"] = MapOf(server.Headers); // :35-37
                entries[server.Id] = ObjectOf(entry); // :38
            }
            else
            {
                var entry = new Dictionary<string, JSONValue>
                {
                    ["command"] = server.Command ?? "", // :41
                    ["args"] = ArrayOf(server.Args), // :42
                };
                if (server.Env.Count > 0) entry["env"] = MapOf(server.Env); // :44-46
                entries[server.Id] = ObjectOf(entry); // :47
            }
        }
        return ObjectOf(new Dictionary<string, JSONValue> { ["mcp_servers"] = ObjectOf(entries) }); // :50
    }

    // :53-75. PR-MCP-167. Both branches always carry "headers"/"env" (even when empty) and a one-element
    // "tools": ["*"] array; the local branch carries "type": "local", the remote branch "type": "http".
    public static JSONValue Copilot(IReadOnlyList<MCPResolvedServer> servers) // :53
    {
        var entries = new Dictionary<string, JSONValue>();
        foreach (var server in servers) // :55
        {
            entries[server.Id] = server.IsRemote // :56
                ? ObjectOf(new Dictionary<string, JSONValue> // :57-62
                {
                    ["type"] = "http", // :58
                    ["url"] = server.Url ?? "", // :59
                    ["headers"] = MapOf(server.Headers), // :60
                    ["tools"] = ArrayOf(new[] { "*" }), // :61
                })
                : ObjectOf(new Dictionary<string, JSONValue> // :64-70
                {
                    ["type"] = "local", // :65
                    ["command"] = server.Command ?? "", // :66
                    ["args"] = ArrayOf(server.Args), // :67
                    ["env"] = MapOf(server.Env), // :68
                    ["tools"] = ArrayOf(new[] { "*" }), // :69
                });
        }
        return ObjectOf(new Dictionary<string, JSONValue> { ["mcpServers"] = ObjectOf(entries) }); // :74
    }

    // :126-146. PR-MCP-168. The remote key is "httpUrl", not "url", and there is no "type" key; "headers" is
    // always present, even empty. Local entries match Claude's/Codex's shape.
    public static JSONValue Gemini(IReadOnlyList<MCPResolvedServer> servers) // :126
    {
        var entries = new Dictionary<string, JSONValue>();
        foreach (var server in servers) // :128
        {
            if (server.IsRemote) // :129
            {
                entries[server.Id] = ObjectOf(new Dictionary<string, JSONValue> // :130-133
                {
                    ["httpUrl"] = server.Url ?? "", // :131
                    ["headers"] = MapOf(server.Headers), // :132
                });
            }
            else
            {
                var entry = new Dictionary<string, JSONValue>
                {
                    ["command"] = server.Command ?? "", // :136
                    ["args"] = ArrayOf(server.Args), // :137
                };
                if (server.Env.Count > 0) entry["env"] = MapOf(server.Env); // :139-141
                entries[server.Id] = ObjectOf(entry); // :142
            }
        }
        return ObjectOf(new Dictionary<string, JSONValue> { ["mcpServers"] = ObjectOf(entries) }); // :145
    }

    // :163-197. Half of PR-MCP-171 (the half not blocked by MCPPaths): parses ~/.codex/config.toml line by line
    // for bare "[mcp_servers.<name>]" table headers and returns their names sorted. The public overload uses
    // LoginEnvironment.HomeDirectory; the internal overload is the tests' seam (Shell.cs's Which(name,
    // environment, home) convention).
    public static ValueList<string> CodexUserServerNames() => CodexUserServerNames(LoginEnvironment.HomeDirectory); // :164

    internal static ValueList<string> CodexUserServerNames(string home) // :163-164
    {
        var path = SwiftPath.AppendingPathComponent(home, ".codex/config.toml"); // :164
        string? text = SwiftTry.Optional(() => SwiftText.DecodeUtf8OrNil(File.ReadAllBytes(path))); // :165, R-STR-8
        if (text is null) return ValueList<string>.Empty; // :165

        var names = new HashSet<string>(SwiftStringComparer.Instance);
        foreach (var rawLine in SwiftString.ComponentsSeparatedByNewlines(text)) // :167
        {
            var line = FoundationText.Trim(rawLine); // :168
            if (!SwiftString.HasPrefix(line, "[mcp_servers.")) continue; // :169
            var rest = SwiftString.DropFirst(line, "[mcp_servers.".Length); // :170
            var end = rest.IndexOf(']', StringComparison.Ordinal); // :171
            if (end < 0) continue; // :171
            var tail = FoundationText.Trim(rest[(end + 1)..]); // :172
            if (!(tail.Length == 0 || SwiftString.HasPrefix(tail, "#"))) continue; // :173
            rest = rest[..end]; // :174

            char? quote = SwiftString.HasPrefix(rest, "'") ? '\'' : SwiftString.HasPrefix(rest, "\"") ? '"' : null; // :178
            string name;
            if (quote is { } q)
            {
                var remainder = SwiftString.DropFirst(rest); // :181
                var close = remainder.IndexOf(q, StringComparison.Ordinal); // :182
                if (close < 0) continue; // :182
                name = remainder[..close]; // :183
            }
            else
            {
                var dot = rest.IndexOf('.', StringComparison.Ordinal); // :184
                name = dot >= 0 ? rest[..dot] : rest; // :185-187
            }
            name = FoundationText.Trim(name); // :189

            if (name.Length == 0 || name.Any(c => // :193
                    SwiftCharacter.IsWhitespace(c.ToString()) || c is '[' or ']' or '\'' or '"' or ',' or '.'))
            {
                continue;
            }
            names.Add(name); // :194
        }
        return new ValueList<string>(names.OrderBy(n => n, SwiftStringComparer.Instance)); // :196, R-COLL-1/R-STR-2
    }

    // :148-159. PR-MCP-169. An empty list removes all four provider files (in claude, codex, copilot, gemini
    // order) and returns; a non-empty list writes all four in that same order. Individual write/delete failures
    // are swallowed (try?).
    public static void WriteAll(IReadOnlyList<MCPResolvedServer> servers) // :148
    {
        if (servers.Count == 0) // :149
        {
            foreach (var url in new[] { MCPPaths.ClaudeConfigURL, MCPPaths.CodexConfigURL, MCPPaths.CopilotConfigURL, GeminiSettingsURL }) // :150
                SwiftTry.Ignore(() => FileOps.RemoveItem(url)); // :151
            return; // :153
        }
        Write(Claude(servers), MCPPaths.ClaudeConfigURL); // :155
        Write(Codex(servers), MCPPaths.CodexConfigURL); // :156
        Write(Copilot(servers), MCPPaths.CopilotConfigURL); // :157
        Write(Gemini(servers), GeminiSettingsURL); // :158
    }

    // :77-124. PR-MCP-170. Re-reads claude.json and rebuilds an ACP server array in sorted id order; a remote
    // entry (type "http" or a non-nil url) becomes an http server when allowsHTTP, else an npx mcp-remote stdio
    // launch; a local entry keeps its raw args array and a sorted env list.
    public static JSONValue AcpServers(bool allowsHTTP) // :77
    {
        var data = SwiftTry.Optional(() => File.ReadAllBytes(MCPPaths.ClaudeConfigURL)); // :78
        var parsed = data is null ? null : JSONValue.Parse(data); // :79
        var mcpServers = parsed?["mcpServers"]?.JSONValueObject; // :80
        if (mcpServers is null || mcpServers.Count == 0) return new JSONValue.Array(ValueList<JSONValue>.Empty); // :80-81

        var servers = new List<JSONValue>(); // :82
        foreach (var id in mcpServers.Keys.OrderBy(k => k, SwiftStringComparer.Instance)) // :83, R-COLL-1/R-STR-2
        {
            var entry = mcpServers[id]; // :84
            if (entry["type"]?.JSONValueString == "http" || entry["url"]?.JSONValueString is not null) // :85
            {
                var url = entry["url"]?.JSONValueString ?? ""; // :86
                var headers = entry["headers"]?.JSONValueObject ?? new Dictionary<string, JSONValue>(); // :87
                var headerList = headers.Keys.OrderBy(k => k, SwiftStringComparer.Instance) // :88
                    .Select(key => (JSONValue)ObjectOf(new Dictionary<string, JSONValue>
                    {
                        ["name"] = key, // :89
                        ["value"] = headers[key].JSONValueString ?? "", // :89
                    })).ToList();
                if (allowsHTTP) // :91
                {
                    servers.Add(ObjectOf(new Dictionary<string, JSONValue> // :92-97
                    {
                        ["type"] = "http", // :93
                        ["name"] = id, // :94
                        ["url"] = url, // :95
                        ["headers"] = new JSONValue.Array(new ValueList<JSONValue>(headerList)), // :96
                    }));
                }
                else
                {
                    var args = new List<JSONValue> { "-y", "mcp-remote", url }; // :99
                    foreach (var key in headers.Keys.OrderBy(k => k, SwiftStringComparer.Instance)) // :100
                        args.AddRange(new JSONValue[] { "--header", $"{key}: {headers[key].JSONValueString ?? ""}" }); // :101
                    servers.Add(ObjectOf(new Dictionary<string, JSONValue> // :103-108
                    {
                        ["name"] = id, // :104
                        ["command"] = "npx", // :105
                        ["args"] = new JSONValue.Array(new ValueList<JSONValue>(args)), // :106
                        ["env"] = new JSONValue.Array(ValueList<JSONValue>.Empty), // :107
                    }));
                }
            }
            else
            {
                var env = entry["env"]?.JSONValueObject ?? new Dictionary<string, JSONValue>(); // :111
                var envList = env.Keys.OrderBy(k => k, SwiftStringComparer.Instance) // :112
                    .Select(key => (JSONValue)ObjectOf(new Dictionary<string, JSONValue>
                    {
                        ["name"] = key, // :113
                        ["value"] = env[key].JSONValueString ?? "", // :113
                    })).ToList();
                servers.Add(ObjectOf(new Dictionary<string, JSONValue> // :115-120
                {
                    ["name"] = id, // :116
                    ["command"] = entry["command"]?.JSONValueString ?? "", // :117
                    ["args"] = new JSONValue.Array(new ValueList<JSONValue>(entry["args"]?.JSONValueArray ?? Array.Empty<JSONValue>())), // :118
                    ["env"] = new JSONValue.Array(new ValueList<JSONValue>(envList)), // :119
                }));
            }
        }
        return new JSONValue.Array(new ValueList<JSONValue>(servers)); // :123
    }

    // :199-210. PR-MCP-171 (MCPPaths-reading half). Returns nil unless codex.json parses to a non-empty
    // mcp_servers object; otherwise adds {"enabled": false} for every user server name not already a key. The
    // public overload reads the real ~/.codex/config.toml via CodexUserServerNames(); the internal overload is
    // the tests' seam.
    public static JSONValue? CodexOverrides() => CodexOverrides(CodexUserServerNames()); // :199, :206

    internal static JSONValue? CodexOverrides(IReadOnlyList<string> userServerNames) // :199-203
    {
        var data = ReadOptionalConfig(MCPPaths.CodexConfigURL); // :200
        var parsed = data is null ? null : JSONValue.Parse(data); // :201
        var mcpServers = parsed?["mcp_servers"]?.JSONValueObject; // :202
        if (mcpServers is null || mcpServers.Count == 0) return null; // :202-203

        var @object = new Dictionary<string, JSONValue>(mcpServers);
        foreach (var name in userServerNames) // :206
        {
            if (!@object.ContainsKey(name)) // :206
                @object[name] = ObjectOf(new Dictionary<string, JSONValue> { ["enabled"] = false }); // :207
        }
        return ObjectOf(@object); // :209
    }

    // :212-215. PR-MCP-172.
    public static string? ClaudeConfigPath() // :212
    {
        if (!ServersPresent(MCPPaths.ClaudeConfigURL, "mcpServers")) return null; // :213
        return MCPPaths.ClaudeConfigURL; // :214
    }

    // :217-223. PR-MCP-172. Returns the whole copilot.json re-serialized as a compact JSON string, not a path.
    public static string? CopilotConfigJSON() // :217
    {
        var data = ReadOptionalConfig(MCPPaths.CopilotConfigURL); // :218
        var parsed = data is null ? null : JSONValue.Parse(data); // :219
        var mcpServers = parsed?["mcpServers"]?.JSONValueObject; // :220
        if (mcpServers is null || mcpServers.Count == 0) return null; // :220-221
        return parsed!.CompactString; // :222
    }

    // :225-228. PR-MCP-172.
    public static string? GeminiSettingsPath() // :225
    {
        if (!ServersPresent(GeminiSettingsURL, "mcpServers")) return null; // :226
        return GeminiSettingsURL; // :227
    }

    // PORT-ONLY (Ignis Advisor, ED-NEW-ignisadvisor-2): no Swift counterpart. A session's ask_advisor server, null unless
    // its chat has an advisor (a Hydra head, whose lead advises); the per-provider shapes below add it beside the
    // connected servers only then, so a chat with no advisor launches exactly as before.
    public static MCPResolvedServer? AdvisorServer(Providers.SessionConfiguration configuration) =>
        Port.Advisor.AdvisorMcpBridge.Server(configuration);

    // PORT-ONLY (web surfaces W12, ED-NEW-web-w12-1): no Swift counterpart. A chat session's nth-ui server (Tabula's
    // artifact tools), null for a session of no chat or while artifacts are off; the shapes below add it beside the
    // connected servers and the advisor's, so a session without it launches exactly as before.
    public static MCPResolvedServer? UiServer(Providers.SessionConfiguration configuration) =>
        Web.UiMcpBridge.Server(configuration);

    private static IReadOnlyList<MCPResolvedServer> BuiltIns(MCPResolvedServer? advisor, MCPResolvedServer? ui) =>
        [.. new[] { advisor, ui }.OfType<MCPResolvedServer>()];

    /// <summary>Codex: the connected servers (CodexOverrides) with the advisor's and nth-ui's entries added; null when there are none.</summary>
    public static JSONValue? CodexServers(JSONValue? overrides, MCPResolvedServer? advisor, MCPResolvedServer? ui = null)
    {
        var builtIns = BuiltIns(advisor, ui);
        if (builtIns.Count == 0) return overrides;
        var merged = new Dictionary<string, JSONValue>(overrides?.JSONValueObject ?? new Dictionary<string, JSONValue>());
        foreach (var (key, value) in Codex(builtIns)["mcp_servers"]!.JSONValueObject!)
            merged[key] = key == Web.UiMcpServer.ServerName ? WithToolTimeout(value, Web.AskUserTool.ToolTimeoutSeconds) : value;
        return ObjectOf(merged);
    }

    // PORT-ONLY (web surfaces W25, ED-NEW-web-w25-1): Codex ends an MCP tool call after 60 s unless the server's entry says otherwise
    // (mcp_servers.<id>.tool_timeout_sec, the config reference's own key); an ask_user question waits for a person, so nth-ui's entry asks for a day.
    private static JSONValue WithToolTimeout(JSONValue entry, long seconds)
    {
        var pairs = new Dictionary<string, JSONValue>(entry.JSONValueObject ?? new Dictionary<string, JSONValue>()) { ["tool_timeout_sec"] = seconds };
        return ObjectOf(pairs);
    }

    /// <summary>Copilot: the advisor's and nth-ui's entries in the "mcpServers" shape a session's create parameters take; null without either.</summary>
    public static JSONValue? CopilotSessionServers(MCPResolvedServer? advisor, MCPResolvedServer? ui = null) =>
        BuiltIns(advisor, ui) is { Count: > 0 } builtIns ? Copilot(builtIns)["mcpServers"] : null;

    /// <summary>ACP: the connected servers with the advisor's and nth-ui's entries appended (the skills relay's form).</summary>
    public static JSONValue AcpServers(JSONValue servers, MCPResolvedServer? advisor, MCPResolvedServer? ui = null) =>
        BuiltIns(advisor, ui) is { Count: > 0 } builtIns
            ? new JSONValue.Array(new ValueList<JSONValue>((servers.JSONValueArray ?? []).Concat(builtIns.Select(Port.Advisor.AdvisorMcpBridge.AcpEntry))))
            : servers;

    /// <summary>
    /// Claude: the session's one --mcp-config file and whether --strict-mcp-config follows it. Without an advisor it is
    /// exactly ClaudeConfigPath() with strict (or null), as before. With one, the connected servers of claude.json and
    /// the advisor's entry are merged into a file of the chat's own, strict only when there were connected servers (as
    /// before: with none, the user's own servers stay on). A file that cannot be written leaves the launch as before.
    /// </summary>
    public static (string Path, bool Strict)? ClaudeSessionConfig(MCPResolvedServer? advisor, Guid? thread, MCPResolvedServer? ui = null)
    {
        var connected = ClaudeConfigPath();
        (string, bool)? plain = connected is null ? null : (connected, true);
        var builtIns = BuiltIns(advisor, ui);
        if (builtIns.Count == 0) return plain;
        var merged = SwiftTry.Optional(() =>
        {
            var servers = new Dictionary<string, JSONValue>();
            if (connected is not null && ReadOptionalConfig(connected) is { } data && JSONValue.Parse(data)?["mcpServers"]?.JSONValueObject is { } existing)
                foreach (var (key, value) in existing) servers[key] = value;
            foreach (var builtIn in builtIns) servers[builtIn.Id] = Claude([builtIn])["mcpServers"]![builtIn.Id]!;
            var path = SwiftPath.AppendingPathComponent(MCPPaths.Directory, (advisor is null ? "nth-" : "advisor-") + (thread ?? Guid.NewGuid()).ToString("N") + ".json");
            FileOps.WriteAtomic(path, ObjectOf(new Dictionary<string, JSONValue> { ["mcpServers"] = ObjectOf(servers) }).Data(pretty: true));
            return path;
        });
        return merged is null ? plain : (merged, connected is not null);
    }

    // :230. private static let geminiSettingsURL. ENGINEERING DECISION: like MCPPaths.Directory (MCP.cs, ED-177),
    // Storage.Root is an Ambient scoped per async flow, so a process-wide cached field here would freeze on
    // whichever test's root won the race to evaluate it first. Recomputed on every access instead.
    private static string GeminiSettingsURL => SwiftPath.AppendingPathComponent(MCPPaths.Directory, "gemini-settings.json"); // :230

    // :232-235. Creates the parent directory then writes pretty-printed JSON atomically; both are try? in Swift.
    private static void Write(JSONValue value, string url) // :232
    {
        SwiftTry.Ignore(() => Directory.CreateDirectory(Path.GetDirectoryName(url) ?? url)); // :233
        SwiftTry.Ignore(() => FileOps.WriteAtomic(url, value.Data(pretty: true))); // :234
    }

    // :237-243. True only when the file reads, parses, and the named key holds a non-empty object.
    private static bool ServersPresent(string url, string key) // :237
    {
        var data = ReadOptionalConfig(url); // :238
        var parsed = data is null ? null : JSONValue.Parse(data); // :239
        var @object = parsed?[key]?.JSONValueObject; // :240
        return @object is not null && @object.Count > 0; // :240-241
    }

    // T44: nil for an absent/unreadable optional file, as try? Data(contentsOf:) above; never cache absence.
    private static byte[]? ReadOptionalConfig(string path) =>
        File.Exists(path) ? SwiftTry.Optional(() => File.ReadAllBytes(path)) : null;

    private static JSONValue ObjectOf(IEnumerable<KeyValuePair<string, JSONValue>> pairs) =>
        new JSONValue.Object(new ValueMap<string, JSONValue>(pairs, SwiftStringComparer.Instance));

    private static JSONValue MapOf(IReadOnlyDictionary<string, string> map) =>
        new JSONValue.Object(new ValueMap<string, JSONValue>(
            map.Select(kv => new KeyValuePair<string, JSONValue>(kv.Key, new JSONValue.String(kv.Value))),
            SwiftStringComparer.Instance));

    private static JSONValue ArrayOf(IReadOnlyList<string> items) =>
        new JSONValue.Array(new ValueList<JSONValue>(items.Select(i => (JSONValue)new JSONValue.String(i))));
}

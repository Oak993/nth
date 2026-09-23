using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.MCP;

// Tests for src/Marrow.Services/MCP/MCPProviderConfig.cs, the port of DroppyCode/Services/MCP/MCPProviderConfig.swift
// (B2 1fbf81e), :1-253. Every expectation is transcribed from that file and records PR-MCP-165 to PR-MCP-172, never
// from the C# implementation.
//
// Swift's [String: JSONValue] iteration order is unspecified (R-COLL-2), so a multi-key JSON.Object's own keys are
// asserted by membership (AssertKeys / the JSONValue indexer), never by comparing a serialized string whose byte
// order would depend on that unspecified order. Only an EMPTY object ("{}") or a genuinely single-key object is
// compared as an exact byte string.
//
// NOT TESTED HERE, and not implemented in this part: WriteAll (PR-MCP-169), AcpServers (PR-MCP-170), CodexOverrides
// and the four accessors ClaudeConfigPath/CopilotConfigJSON/GeminiSettingsPath/ConnectedIDs plus the private
// ServersPresent (PR-MCP-172, and the MCPPaths-reading half of PR-MCP-171). All of them read or write through
// Marrow.Services.Models.MCPPaths, whose members are still MARROW_STUB (throw NotImplementedException) because
// they build on Storage.root (Services/Store/Storage.swift:18), which has no C# twin in the tree yet. This is a
// missing dependency (IMPLEMENTING.md section 6); see the unit's STOP report.
public sealed class MCPProviderConfigTests
{
    private static MCPResolvedServer Remote(string id, string? url = "https://example.test/mcp",
        IEnumerable<(string, string)>? headers = null) =>
        new MCPResolvedServer(Id: id, Name: id) { Url = url, Headers = MapOf(headers) };

    private static MCPResolvedServer Local(string id, string? command = "npx",
        IEnumerable<string>? args = null, IEnumerable<(string, string)>? env = null) =>
        new MCPResolvedServer(Id: id, Name: id) { Command = command, Args = ListOf(args), Env = MapOf(env) };

    private static ValueMap<string, string> MapOf(IEnumerable<(string, string)>? pairs) =>
        pairs is null ? ValueMap<string, string>.Empty : new ValueMap<string, string>(
            pairs.Select(p => new KeyValuePair<string, string>(p.Item1, p.Item2)), SwiftStringComparer.Instance);

    private static ValueList<string> ListOf(IEnumerable<string>? items) =>
        items is null ? ValueList<string>.Empty : new ValueList<string>(items);

    private static void AssertKeys(JSONValue obj, params string[] expected) =>
        Assert.Equal(new HashSet<string>(expected), new HashSet<string>(obj.JSONValueObject!.Keys));

    // ===== PR-MCP-165: claude(_:) =====

    [Fact]
    [Trait("Req", "PR-MCP-165")] // DroppyCode/Services/MCP/MCPProviderConfig.swift:4-28
    public void Claude_with_no_servers_writes_empty_mcpServers_object()
    {
        var result = MCPProviderConfig.Claude(Array.Empty<MCPResolvedServer>());
        Assert.Equal("{\"mcpServers\":{}}", result.CompactString); // :27, no key-order ambiguity: only key present
    }

    [Fact]
    [Trait("Req", "PR-MCP-165")] // :4-15
    public void Claude_remote_server_has_type_http_url_and_no_headers_key_when_headers_empty()
    {
        var result = MCPProviderConfig.Claude(new[] { Remote("s1", url: "https://x.test") });
        var entry = result["mcpServers"]!["s1"]!;
        AssertKeys(entry, "type", "url"); // :9-11, headers omitted (:12-14) because Headers is empty
        Assert.Equal("http", entry["type"]!.JSONValueString);
        Assert.Equal("https://x.test", entry["url"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-165")] // :10, :118 isRemote is `url != nil`, so an empty (not nil) url is the only way
    // a remote entry can exercise the "?? \"\"" fallback: it is written back out unchanged, not refused or skipped.
    public void Claude_remote_server_with_empty_url_writes_empty_string_not_skipped()
    {
        var result = MCPProviderConfig.Claude(new[] { Remote("s1", url: "") });
        Assert.Equal("", result["mcpServers"]!["s1"]!["url"]!.JSONValueString); // upstream defect 23, reproduced
    }

    [Fact]
    [Trait("Req", "PR-MCP-165")] // :12-14
    public void Claude_remote_server_with_headers_includes_headers_object()
    {
        var result = MCPProviderConfig.Claude(new[] { Remote("s1", headers: new[] { ("X-Key", "abc") }) });
        var entry = result["mcpServers"]!["s1"]!;
        AssertKeys(entry, "type", "url", "headers");
        Assert.Equal("abc", entry["headers"]!["X-Key"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-165")] // :16-24
    public void Claude_local_server_has_command_args_and_no_type_key()
    {
        var result = MCPProviderConfig.Claude(new[] { Local("s2", command: "npx", args: new[] { "-y", "pkg" }) });
        var entry = result["mcpServers"]!["s2"]!;
        AssertKeys(entry, "command", "args"); // :17-20, no "type" key on the local branch, env omitted
        Assert.Equal("npx", entry["command"]!.JSONValueString);
        Assert.Equal("-y", entry["args"]!.JSONValueArray![0].JSONValueString);
        Assert.Equal("pkg", entry["args"]!.JSONValueArray![1].JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-165")] // :18
    public void Claude_local_server_missing_command_becomes_empty_string_not_skipped()
    {
        var result = MCPProviderConfig.Claude(new[] { Local("s2", command: null) });
        Assert.Equal("", result["mcpServers"]!["s2"]!["command"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-165")] // :21-23
    public void Claude_local_server_with_env_includes_env_object()
    {
        var result = MCPProviderConfig.Claude(new[] { Local("s2", env: new[] { ("TOKEN", "t") }) });
        var entry = result["mcpServers"]!["s2"]!;
        AssertKeys(entry, "command", "args", "env");
        Assert.Equal("t", entry["env"]!["TOKEN"]!.JSONValueString);
    }

    // ===== PR-MCP-166: codex(_:) =====

    [Fact]
    [Trait("Req", "PR-MCP-166")] // :30-51, snake-case root key
    public void Codex_root_key_is_snake_case_mcp_servers()
    {
        var result = MCPProviderConfig.Codex(Array.Empty<MCPResolvedServer>());
        Assert.Equal("{\"mcp_servers\":{}}", result.CompactString); // :50
    }

    [Fact]
    [Trait("Req", "PR-MCP-166")] // :33-38
    public void Codex_remote_server_has_url_and_no_type_key()
    {
        var result = MCPProviderConfig.Codex(new[] { Remote("s1", url: "https://x.test") });
        var entry = result["mcp_servers"]!["s1"]!;
        AssertKeys(entry, "url"); // :34, no "type" key at all, headers omitted because empty
        Assert.Equal("https://x.test", entry["url"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-166")] // :35-37, http_headers not headers
    public void Codex_remote_server_with_headers_uses_http_headers_key()
    {
        var result = MCPProviderConfig.Codex(new[] { Remote("s1", headers: new[] { ("X-Key", "abc") }) });
        var entry = result["mcp_servers"]!["s1"]!;
        AssertKeys(entry, "url", "http_headers");
        Assert.Null(entry["headers"]); // the key is http_headers, not headers
        Assert.Equal("abc", entry["http_headers"]!["X-Key"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-166")] // :39-47, identical local shape to Claude's
    public void Codex_local_server_shape_matches_claude()
    {
        var result = MCPProviderConfig.Codex(new[] { Local("s2", command: "npx", args: new[] { "-y" }, env: new[] { ("K", "v") }) });
        var entry = result["mcp_servers"]!["s2"]!;
        AssertKeys(entry, "command", "args", "env");
    }

    // ===== PR-MCP-167: copilot(_:) =====

    [Fact]
    [Trait("Req", "PR-MCP-167")] // :56-62
    public void Copilot_remote_server_always_carries_headers_and_tools_wildcard_even_when_empty()
    {
        var result = MCPProviderConfig.Copilot(new[] { Remote("s1", headers: null) });
        var entry = result["mcpServers"]!["s1"]!;
        AssertKeys(entry, "type", "url", "headers", "tools"); // :57-62, headers present though empty
        Assert.Equal("http", entry["type"]!.JSONValueString);
        Assert.Equal("{}", entry["headers"]!.CompactString);
        Assert.Equal("[\"*\"]", entry["tools"]!.CompactString); // :61, one-element array holding "*"
    }

    [Fact]
    [Trait("Req", "PR-MCP-167")] // :64-70
    public void Copilot_local_server_always_carries_env_and_tools_wildcard_even_when_empty()
    {
        var result = MCPProviderConfig.Copilot(new[] { Local("s2", env: null) });
        var entry = result["mcpServers"]!["s2"]!;
        AssertKeys(entry, "type", "command", "args", "env", "tools"); // :65-69, env present though empty
        Assert.Equal("local", entry["type"]!.JSONValueString); // :65
        Assert.Equal("{}", entry["env"]!.CompactString);
        Assert.Equal("[\"*\"]", entry["tools"]!.CompactString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-167")] // :59 empty url written unchanged (isRemote requires url non-nil, :118); :66
    // missing command (nil, the local branch's only optional) becomes ""
    public void Copilot_empty_url_and_missing_command_become_empty_strings()
    {
        var remote = MCPProviderConfig.Copilot(new[] { Remote("s1", url: "") });
        Assert.Equal("", remote["mcpServers"]!["s1"]!["url"]!.JSONValueString);
        var local = MCPProviderConfig.Copilot(new[] { Local("s2", command: null) });
        Assert.Equal("", local["mcpServers"]!["s2"]!["command"]!.JSONValueString);
    }

    // ===== PR-MCP-168: gemini(_:) =====

    [Fact]
    [Trait("Req", "PR-MCP-168")] // :130-133
    public void Gemini_remote_server_uses_httpUrl_key_not_url_and_always_carries_headers()
    {
        var result = MCPProviderConfig.Gemini(new[] { Remote("s1", url: "https://x.test", headers: null) });
        var entry = result["mcpServers"]!["s1"]!;
        AssertKeys(entry, "httpUrl", "headers"); // :131-132, no "type" key, headers present though empty
        Assert.Equal("https://x.test", entry["httpUrl"]!.JSONValueString);
        Assert.Null(entry["url"]);
        Assert.Equal("{}", entry["headers"]!.CompactString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-168")] // :131 empty url written unchanged (isRemote requires url non-nil, :118)
    public void Gemini_remote_server_with_empty_url_writes_empty_string()
    {
        var result = MCPProviderConfig.Gemini(new[] { Remote("s1", url: "") });
        Assert.Equal("", result["mcpServers"]!["s1"]!["httpUrl"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-168")] // :135-142, matches Claude's/Codex's local shape
    public void Gemini_local_server_omits_env_when_empty_and_includes_it_when_present()
    {
        var withoutEnv = MCPProviderConfig.Gemini(new[] { Local("s2", env: null) });
        AssertKeys(withoutEnv["mcpServers"]!["s2"]!, "command", "args");
        var withEnv = MCPProviderConfig.Gemini(new[] { Local("s2", env: new[] { ("K", "v") }) });
        AssertKeys(withEnv["mcpServers"]!["s2"]!, "command", "args", "env");
    }

    [Fact]
    [Trait("Req", "PR-MCP-168")] // :126-146
    public void Gemini_with_no_servers_writes_empty_mcpServers_object()
    {
        var result = MCPProviderConfig.Gemini(Array.Empty<MCPResolvedServer>());
        Assert.Equal("{\"mcpServers\":{}}", result.CompactString); // :145
    }

    // ===== PR-MCP-171 (codexUserServerNames half): codexUserServerNames() =====

    [Fact]
    [Trait("Req", "PR-MCP-171")] // DroppyCode/Services/MCP/MCPProviderConfig.swift:163-197
    public void CodexUserServerNames_reads_a_bare_table_header_name()
    {
        using var home = new MCPProviderConfigScratchHome();
        home.WriteCodexConfig("[mcp_servers.foo]\nenabled = true\n");
        var names = MCPProviderConfig.CodexUserServerNames(home.FullPath);
        Assert.Equal(new[] { "foo" }, names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :165, missing file yields an empty list
    public void CodexUserServerNames_returns_empty_when_config_file_is_missing()
    {
        using var home = new MCPProviderConfigScratchHome();
        var names = MCPProviderConfig.CodexUserServerNames(home.FullPath);
        Assert.Empty(names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :196, names.sorted()
    public void CodexUserServerNames_returns_names_sorted()
    {
        using var home = new MCPProviderConfigScratchHome();
        home.WriteCodexConfig("[mcp_servers.zebra]\n[mcp_servers.alpha]\n");
        var names = MCPProviderConfig.CodexUserServerNames(home.FullPath);
        Assert.Equal(new[] { "alpha", "zebra" }, names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :166, Set<String> dedupes
    public void CodexUserServerNames_dedupes_repeated_names()
    {
        using var home = new MCPProviderConfigScratchHome();
        home.WriteCodexConfig("[mcp_servers.foo]\n[mcp_servers.foo]\n");
        var names = MCPProviderConfig.CodexUserServerNames(home.FullPath);
        Assert.Equal(new[] { "foo" }, names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :178-183, a double-quoted first segment is the name literally
    public void CodexUserServerNames_reads_a_double_quoted_name_literally()
    {
        using var home = new MCPProviderConfigScratchHome();
        home.WriteCodexConfig("[mcp_servers.\"simple\"]\n");
        var names = MCPProviderConfig.CodexUserServerNames(home.FullPath);
        Assert.Equal(new[] { "simple" }, names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :178-183, a single-quoted name containing whitespace is dropped by the final guard
    public void CodexUserServerNames_drops_a_quoted_name_containing_whitespace()
    {
        using var home = new MCPProviderConfigScratchHome();
        home.WriteCodexConfig("[mcp_servers.'foo bar']\n");
        var names = MCPProviderConfig.CodexUserServerNames(home.FullPath);
        Assert.Empty(names); // :193 rejects any name with whitespace
    }

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :184-185, an unquoted nested-table path keeps only the part before the first dot
    public void CodexUserServerNames_reads_only_the_prefix_of_a_nested_table_path()
    {
        using var home = new MCPProviderConfigScratchHome();
        home.WriteCodexConfig("[mcp_servers.imap-email.env]\n");
        var names = MCPProviderConfig.CodexUserServerNames(home.FullPath);
        Assert.Equal(new[] { "imap-email" }, names); // matches the PR-MCP-171 windows_path example verbatim
    }

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :172-173, text after the closing bracket must be empty or a comment
    public void CodexUserServerNames_accepts_a_comment_after_the_closing_bracket()
    {
        using var home = new MCPProviderConfigScratchHome();
        home.WriteCodexConfig("[mcp_servers.foo] # a comment\n");
        var names = MCPProviderConfig.CodexUserServerNames(home.FullPath);
        Assert.Equal(new[] { "foo" }, names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :172-173, non-comment text after the closing bracket rejects the whole line
    public void CodexUserServerNames_rejects_a_line_with_trailing_non_comment_text()
    {
        using var home = new MCPProviderConfigScratchHome();
        home.WriteCodexConfig("[mcp_servers.foo] extra\n");
        var names = MCPProviderConfig.CodexUserServerNames(home.FullPath);
        Assert.Empty(names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :171, no closing bracket at all
    public void CodexUserServerNames_rejects_a_line_missing_the_closing_bracket()
    {
        using var home = new MCPProviderConfigScratchHome();
        home.WriteCodexConfig("[mcp_servers.foo\n");
        var names = MCPProviderConfig.CodexUserServerNames(home.FullPath);
        Assert.Empty(names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :193, empty name after trimming is rejected
    public void CodexUserServerNames_rejects_an_empty_name()
    {
        using var home = new MCPProviderConfigScratchHome();
        home.WriteCodexConfig("[mcp_servers.]\n");
        var names = MCPProviderConfig.CodexUserServerNames(home.FullPath);
        Assert.Empty(names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :169, a line without the "[mcp_servers." prefix is not a table header at all
    public void CodexUserServerNames_ignores_lines_without_the_mcp_servers_prefix()
    {
        using var home = new MCPProviderConfigScratchHome();
        home.WriteCodexConfig("[other_table.foo]\nname = \"bar\"\n");
        var names = MCPProviderConfig.CodexUserServerNames(home.FullPath);
        Assert.Empty(names);
    }
}

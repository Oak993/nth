using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Services.Store;
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
// ED-177 RESUME: Services.Store.Storage merged (Storage.cs) and Marrow.Services.Models.MCPPaths now builds its
// locations from Storage.Root, so the file-touching members (WriteAll, AcpServers, CodexOverrides,
// ClaudeConfigPath/CopilotConfigJSON/GeminiSettingsPath/ConnectedIDs, private ServersPresent) are tested below
// using StorageScratchFolder (R-TEST-9), never the real %LOCALAPPDATA%\Marrow Code.
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

    // ===== PR-MCP-169: writeAll(_:) =====

    [Fact]
    [Trait("Req", "PR-MCP-169")] // :149-153, empty list deletes all four and writes nothing
    public void WriteAll_with_no_servers_deletes_all_four_files_and_writes_nothing()
    {
        using var storage = new StorageScratchFolder();
        var directory = MCPPaths.Directory; // creates <root>/mcp
        File.WriteAllText(Path.Combine(directory, "claude.json"), "stale");
        File.WriteAllText(Path.Combine(directory, "codex.json"), "stale");
        File.WriteAllText(Path.Combine(directory, "copilot.json"), "stale");
        File.WriteAllText(Path.Combine(directory, "gemini-settings.json"), "stale");

        MCPProviderConfig.WriteAll(Array.Empty<MCPResolvedServer>());

        Assert.False(File.Exists(MCPPaths.ClaudeConfigURL));
        Assert.False(File.Exists(MCPPaths.CodexConfigURL));
        Assert.False(File.Exists(MCPPaths.CopilotConfigURL));
        Assert.False(File.Exists(Path.Combine(directory, "gemini-settings.json")));
    }

    [Fact]
    [Trait("Req", "PR-MCP-169")] // :149-153, no files present is not an error (try? removeItem)
    public void WriteAll_with_no_servers_and_no_existing_files_does_not_throw()
    {
        using var storage = new StorageScratchFolder();
        var exception = Record.Exception(() => MCPProviderConfig.WriteAll(Array.Empty<MCPResolvedServer>()));
        Assert.Null(exception);
    }

    [Fact]
    [Trait("Req", "PR-MCP-169")] // :155-158, a non-empty list writes all four provider files
    public void WriteAll_with_servers_writes_all_four_files()
    {
        using var storage = new StorageScratchFolder();
        MCPProviderConfig.WriteAll(new[] { Remote("s1") });

        Assert.True(File.Exists(MCPPaths.ClaudeConfigURL));
        Assert.True(File.Exists(MCPPaths.CodexConfigURL));
        Assert.True(File.Exists(MCPPaths.CopilotConfigURL));
        Assert.True(File.Exists(Path.Combine(MCPPaths.Directory, "gemini-settings.json")));

        var claude = JSONValue.Parse(File.ReadAllBytes(MCPPaths.ClaudeConfigURL))!;
        Assert.Equal("http", claude["mcpServers"]!["s1"]!["type"]!.JSONValueString);
        var codex = JSONValue.Parse(File.ReadAllBytes(MCPPaths.CodexConfigURL))!;
        AssertKeys(codex["mcp_servers"]!["s1"]!, "url");
    }

    [Fact]
    [Trait("Req", "PR-MCP-169")] // :156, codex writes to codex.json under the mcp directory
    public void WriteAll_writes_codex_file_at_codex_json()
    {
        using var storage = new StorageScratchFolder();
        MCPProviderConfig.WriteAll(new[] { Remote("s1") });
        Assert.Equal(Path.Combine(MCPPaths.Directory, "codex.json"), MCPPaths.CodexConfigURL);
        Assert.True(File.Exists(MCPPaths.CodexConfigURL));
    }

    // ===== PR-MCP-170: acpServers(allowsHTTP:) =====

    [Fact]
    [Trait("Req", "PR-MCP-170")] // :78-81, a missing claude.json yields an empty array
    public void AcpServers_returns_empty_array_when_claude_config_file_is_missing()
    {
        using var storage = new StorageScratchFolder();
        var result = MCPProviderConfig.AcpServers(allowsHTTP: true);
        Assert.Equal("[]", result.CompactString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-170")] // :91-97, a remote entry with allowsHTTP true becomes an http server object
    public void AcpServers_remote_entry_with_allowsHTTP_true_becomes_http_type_with_sorted_headers()
    {
        using var storage = new StorageScratchFolder();
        MCPProviderConfig.WriteAll(new[] { Remote("s1", url: "https://x.test", headers: new[] { ("Z", "z"), ("A", "a") }) });

        var result = MCPProviderConfig.AcpServers(allowsHTTP: true);
        var entry = result.JSONValueArray![0];
        Assert.Equal("http", entry["type"]!.JSONValueString);
        Assert.Equal("s1", entry["name"]!.JSONValueString);
        Assert.Equal("https://x.test", entry["url"]!.JSONValueString);
        var headerList = entry["headers"]!.JSONValueArray!;
        Assert.Equal(2, headerList.Count);
        Assert.Equal("A", headerList[0]["name"]!.JSONValueString); // :88 headers.keys.sorted()
        Assert.Equal("Z", headerList[1]["name"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-170")] // :98-108, allowsHTTP false rewrites a remote entry as an npx mcp-remote stdio launch
    public void AcpServers_remote_entry_with_allowsHTTP_false_becomes_npx_mcp_remote_launch()
    {
        using var storage = new StorageScratchFolder();
        MCPProviderConfig.WriteAll(new[] { Remote("s1", url: "https://x.test", headers: new[] { ("X-Key", "abc") }) });

        var result = MCPProviderConfig.AcpServers(allowsHTTP: false);
        var entry = result.JSONValueArray![0];
        Assert.Null(entry["type"]); // :103-108, no type key
        Assert.Equal("s1", entry["name"]!.JSONValueString);
        Assert.Equal("npx", entry["command"]!.JSONValueString);
        var args = entry["args"]!.JSONValueArray!;
        Assert.Equal("-y", args[0].JSONValueString);
        Assert.Equal("mcp-remote", args[1].JSONValueString);
        Assert.Equal("https://x.test", args[2].JSONValueString);
        Assert.Equal("--header", args[3].JSONValueString);
        Assert.Equal("X-Key: abc", args[4].JSONValueString); // :101, "<key>: <value>" with one space
        Assert.Equal("[]", entry["env"]!.CompactString); // :107, env is an empty ARRAY not an object
    }

    [Fact]
    [Trait("Req", "PR-MCP-170")] // :110-121, a local entry keeps its command, raw args array and sorted env list
    public void AcpServers_local_entry_keeps_command_and_raw_args_with_sorted_env_list()
    {
        using var storage = new StorageScratchFolder();
        MCPProviderConfig.WriteAll(new[]
        {
            Local("s1", command: "npx", args: new[] { "-y", "pkg" }, env: new[] { ("Z", "z"), ("A", "a") }),
        });

        var result = MCPProviderConfig.AcpServers(allowsHTTP: true);
        var entry = result.JSONValueArray![0];
        Assert.Null(entry["type"]);
        Assert.Equal("npx", entry["command"]!.JSONValueString);
        var args = entry["args"]!.JSONValueArray!;
        Assert.Equal("-y", args[0].JSONValueString);
        Assert.Equal("pkg", args[1].JSONValueString);
        var env = entry["env"]!.JSONValueArray!;
        Assert.Equal(2, env.Count);
        Assert.Equal("A", env[0]["name"]!.JSONValueString); // :112 env.keys.sorted()
        Assert.Equal("Z", env[1]["name"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-170")] // :83, ids are iterated in sorted order
    public void AcpServers_returns_entries_in_sorted_id_order()
    {
        using var storage = new StorageScratchFolder();
        MCPProviderConfig.WriteAll(new[] { Remote("zebra"), Remote("alpha") });

        var result = MCPProviderConfig.AcpServers(allowsHTTP: true).JSONValueArray!;
        Assert.Equal("alpha", result[0]["name"]!.JSONValueString);
        Assert.Equal("zebra", result[1]["name"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-170")] // :85, an entry counts as remote when type is "http" OR it has a url string
    public void AcpServers_treats_entry_with_url_but_no_type_as_remote()
    {
        using var storage = new StorageScratchFolder();
        Directory.CreateDirectory(MCPPaths.Directory);
        File.WriteAllText(MCPPaths.ClaudeConfigURL, "{\"mcpServers\":{\"s1\":{\"url\":\"https://x.test\"}}}");

        var result = MCPProviderConfig.AcpServers(allowsHTTP: true).JSONValueArray!;
        Assert.Equal("http", result[0]["type"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-170")] // :80, an empty mcpServers object yields an empty array
    public void AcpServers_returns_empty_array_when_claude_config_has_empty_mcpServers()
    {
        using var storage = new StorageScratchFolder();
        Directory.CreateDirectory(MCPPaths.Directory);
        File.WriteAllText(MCPPaths.ClaudeConfigURL, "{\"mcpServers\":{}}");
        var result = MCPProviderConfig.AcpServers(allowsHTTP: true);
        Assert.Equal("[]", result.CompactString);
    }

    // ===== PR-MCP-171 (MCPPaths-reading half): codexOverrides() =====

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :200-203, codex.json missing yields nil
    public void CodexOverrides_returns_null_when_codex_config_file_is_missing()
    {
        using var storage = new StorageScratchFolder();
        Assert.Null(MCPProviderConfig.CodexOverrides(ValueList<string>.Empty));
    }

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :202, an empty mcp_servers object also yields nil
    public void CodexOverrides_returns_null_when_mcp_servers_object_is_empty()
    {
        using var storage = new StorageScratchFolder();
        Directory.CreateDirectory(MCPPaths.Directory);
        File.WriteAllText(MCPPaths.CodexConfigURL, "{\"mcp_servers\":{}}");
        Assert.Null(MCPProviderConfig.CodexOverrides(ValueList<string>.Empty));
    }

    [Fact]
    [Trait("Req", "PR-MCP-171")] // :206-208, a user server name not already a key is switched off by name
    public void CodexOverrides_adds_enabled_false_for_user_server_names_not_already_present()
    {
        using var storage = new StorageScratchFolder();
        MCPProviderConfig.WriteAll(new[] { Local("connected") });

        var overrides = MCPProviderConfig.CodexOverrides(new ValueList<string>(new[] { "connected", "other" }))!;
        Assert.Equal("false", overrides["other"]!["enabled"]!.CompactString);
        Assert.Null(overrides["connected"]!["enabled"]); // :206 `where object[name] == nil`, "connected" already a key
    }

    // ===== PR-MCP-172: the four accessors and serversPresent =====

    [Fact]
    [Trait("Req", "PR-MCP-172")] // :212-215, claudeConfigPath returns nil when the file is missing
    public void ClaudeConfigPath_returns_null_when_file_is_missing()
    {
        using var storage = new StorageScratchFolder();
        Assert.Null(MCPProviderConfig.ClaudeConfigPath());
    }

    [Fact]
    [Trait("Req", "PR-MCP-172")] // :212-215, claudeConfigPath returns the path when servers are present
    public void ClaudeConfigPath_returns_path_when_servers_present()
    {
        using var storage = new StorageScratchFolder();
        MCPProviderConfig.WriteAll(new[] { Remote("s1") });
        Assert.Equal(MCPPaths.ClaudeConfigURL, MCPProviderConfig.ClaudeConfigPath());
    }

    [Fact]
    [Trait("Req", "PR-MCP-172")] // :225-228, geminiSettingsPath returns nil when empty and the path when present
    public void GeminiSettingsPath_returns_null_when_empty_and_path_when_present()
    {
        using var storage = new StorageScratchFolder();
        Assert.Null(MCPProviderConfig.GeminiSettingsPath());
        MCPProviderConfig.WriteAll(new[] { Remote("s1") });
        Assert.Equal(Path.Combine(MCPPaths.Directory, "gemini-settings.json"), MCPProviderConfig.GeminiSettingsPath());
    }

    [Fact]
    [Trait("Req", "PR-MCP-172")] // :217-223, copilotConfigJSON returns nil when the mcpServers object is empty
    public void CopilotConfigJSON_returns_null_when_mcpServers_object_is_empty()
    {
        using var storage = new StorageScratchFolder();
        Assert.Null(MCPProviderConfig.CopilotConfigJSON());
    }

    [Fact]
    [Trait("Req", "PR-MCP-172")] // :217-223, copilotConfigJSON returns the whole file re-serialized compact
    public void CopilotConfigJSON_returns_whole_file_as_compact_json_string()
    {
        using var storage = new StorageScratchFolder();
        MCPProviderConfig.WriteAll(new[] { Remote("s1", url: "https://x.test") });
        var json = MCPProviderConfig.CopilotConfigJSON()!;
        var parsed = JSONValue.Parse(json)!;
        Assert.Equal("https://x.test", parsed["mcpServers"]!["s1"]!["url"]!.JSONValueString);
        Assert.DoesNotContain("\n", json, StringComparison.Ordinal); // :222 compactString, not pretty-printed
    }

    [Fact]
    [Trait("Req", "PR-MCP-172")] // :230-236, connectedIDs returns empty when the file is missing
    public void ConnectedIDs_returns_empty_when_claude_config_missing()
    {
        using var storage = new StorageScratchFolder();
        Assert.Empty(MCPProviderConfig.ConnectedIDs());
    }

    [Fact]
    [Trait("Req", "PR-MCP-172")] // :235, connectedIDs returns sorted keys of mcpServers
    public void ConnectedIDs_returns_sorted_keys_of_mcpServers()
    {
        using var storage = new StorageScratchFolder();
        MCPProviderConfig.WriteAll(new[] { Remote("zebra"), Remote("alpha") });
        Assert.Equal(new[] { "alpha", "zebra" }, MCPProviderConfig.ConnectedIDs());
    }
}

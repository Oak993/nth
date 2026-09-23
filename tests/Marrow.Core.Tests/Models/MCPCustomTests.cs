using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models;

public sealed class MCPCustomTests
{
    // ---- PR-MCP-085: makeID ----

    [Theory]
    [InlineData("My Server", "custom-my-server")]
    [InlineData("!!!", "custom-server")]
    [InlineData("  a  b  ", "custom-a-b")]
    [Trait("Req", "PR-MCP-085")] // DroppyCode/Core/Models/MCPCustom.swift:62-76
    public void MakeID_slugs_the_name_and_prefixes_custom(string name, string expected) =>
        Assert.Equal(expected, MCPCustomServer.MakeID(name));

    [Fact]
    [Trait("Req", "PR-MCP-085")] // DroppyCode/Core/Models/MCPCustom.swift:62-76
    public void MakeID_keeps_unicode_letters_and_numbers()
    {
        // "café 2" lowercases to "café 2"; é is a letter, digit is a number, the space becomes one dash.
        Assert.Equal("custom-café-2", MCPCustomServer.MakeID("Café 2"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-085")] // DroppyCode/Core/Models/MCPCustom.swift:62-76
    public void MakeID_never_produces_a_trailing_dash()
    {
        // Every trailing separator collapses; no dash survives at the end even with many trailing separators.
        Assert.Equal("custom-a", MCPCustomServer.MakeID("a---"));
    }

    // ---- PR-MCP-086: summary ----

    [Fact]
    [Trait("Req", "PR-MCP-086")] // DroppyCode/Core/Models/MCPCustom.swift:79-87
    public void Summary_for_stdio_joins_command_and_args_with_Runs_on_this_PC_prefix()
    {
        var server = new MCPCustomServer("custom-x", "X", MCPCustomServer.Kind.Stdio)
        {
            Command = "npx",
            Args = new ValueList<string>(["-y", "x"]),
        };
        Assert.Equal("Runs on this PC: npx -y x.", server.Summary);
    }

    [Fact]
    [Trait("Req", "PR-MCP-086")] // DroppyCode/Core/Models/MCPCustom.swift:79-87
    public void Summary_for_stdio_with_empty_command_and_no_args_is_just_the_prefix_and_a_period()
    {
        var server = new MCPCustomServer("custom-x", "X", MCPCustomServer.Kind.Stdio);
        Assert.Equal("Runs on this PC: .", server.Summary);
    }

    [Fact]
    [Trait("Req", "PR-MCP-086")] // DroppyCode/Core/Models/MCPCustom.swift:79-87
    public void Summary_for_http_names_the_host_when_the_URL_parses()
    {
        var server = new MCPCustomServer("custom-x", "X", MCPCustomServer.Kind.Http)
        {
            Url = "https://example.com/path",
        };
        Assert.Equal("Remote server at example.com.", server.Summary);
    }

    [Fact]
    [Trait("Req", "PR-MCP-086")] // DroppyCode/Core/Models/MCPCustom.swift:79-87
    public void Summary_for_http_falls_back_to_the_raw_string_when_the_URL_does_not_parse()
    {
        // An empty string is not a valid URL (SwiftURL.Create returns null), so the raw string is used.
        var server = new MCPCustomServer("custom-x", "X", MCPCustomServer.Kind.Http) { Url = "" };
        Assert.Equal("Remote server at .", server.Summary);
    }

    // ---- PR-MCP-087: catalogEntry() ----

    [Fact]
    [Trait("Req", "PR-MCP-087")] // DroppyCode/Core/Models/MCPCustom.swift:93-118
    public void CatalogEntry_maps_http_variables_to_fields_and_placeholder_headers()
    {
        var server = new MCPCustomServer("custom-x", "X Server", MCPCustomServer.Kind.Http)
        {
            Url = "https://example.com/mcp",
            Headers = new ValueList<MCPCustomServer.Variable>(
                [new MCPCustomServer.Variable("Authorization", IsSecret: true), new MCPCustomServer.Variable("X-Plain")]),
        };
        var entry = server.CatalogEntry();

        Assert.Equal("custom-x", entry.Id);
        Assert.Equal("X Server", entry.Name);
        Assert.Equal("Custom", entry.Vendor);
        Assert.Equal(server.Summary, entry.Summary);
        Assert.Equal(MCPCategory.Developer, entry.Category);
        Assert.Equal("", entry.Asset);
        Assert.Equal(0u, entry.ColorValue);
        Assert.True(entry.IsMonochrome);
        Assert.Equal("", entry.DocsURL);
        Assert.Empty(entry.SampleTools);
        Assert.Null(entry.KeysURL);
        Assert.True(entry.NeedsInput);

        Assert.Equal(2, entry.Fields.Count);
        Assert.Equal(new MCPField("Authorization", "Authorization", "", MCPField.Kind.Secret), entry.Fields[0]);
        Assert.Equal(new MCPField("X-Plain", "X-Plain", "", MCPField.Kind.Text), entry.Fields[1]);

        var http = Assert.IsType<MCPTransport.Http>(entry.Transport);
        Assert.Equal("https://example.com/mcp", http.Url);
        Assert.Equal("{Authorization}", http.Headers["Authorization"]);
        Assert.Equal("{X-Plain}", http.Headers["X-Plain"]);
        Assert.Empty(entry.Env);
    }

    [Fact]
    [Trait("Req", "PR-MCP-087")] // DroppyCode/Core/Models/MCPCustom.swift:93-118
    public void CatalogEntry_puts_stdio_placeholders_into_entry_Env_and_transport_env_not_headers()
    {
        var server = new MCPCustomServer("custom-y", "Y", MCPCustomServer.Kind.Stdio)
        {
            Command = "node",
            Args = new ValueList<string>(["server.js"]),
            Env = new ValueList<MCPCustomServer.Variable>([new MCPCustomServer.Variable("TOKEN", IsSecret: true)]),
        };
        var entry = server.CatalogEntry();

        var stdio = Assert.IsType<MCPTransport.Stdio>(entry.Transport);
        Assert.Equal("node", stdio.Command);
        Assert.Equal(["server.js"], stdio.Args);
        Assert.Equal("{TOKEN}", entry.Env["TOKEN"]);
        Assert.Single(entry.Fields);
        Assert.Equal(MCPField.Kind.Secret, entry.Fields[0].FieldKind);
    }

    [Fact]
    [Trait("Req", "PR-MCP-087")] // DroppyCode/Core/Models/MCPCustom.swift:93-118
    public void CatalogEntry_placeholders_keep_the_first_of_duplicate_variable_keys()
    {
        // A draft reaches the editor's header before duplicates are validated (:97-98).
        var server = new MCPCustomServer("custom-z", "Z", MCPCustomServer.Kind.Http)
        {
            Url = "https://example.com",
            Headers = new ValueList<MCPCustomServer.Variable>(
                [new MCPCustomServer.Variable("K", IsSecret: false), new MCPCustomServer.Variable("K", IsSecret: true)]),
        };
        var entry = server.CatalogEntry();
        var http = Assert.IsType<MCPTransport.Http>(entry.Transport);
        Assert.Equal("{K}", Assert.Single(http.Headers).Value);
        // The fields list itself is NOT deduplicated: both variables still map to fields (:94-96).
        Assert.Equal(2, entry.Fields.Count);
    }

    [Fact]
    [Trait("Req", "PR-MCP-087")] // DroppyCode/Core/Models/MCPCustom.swift:93-118
    public void CatalogEntry_with_no_variables_needs_no_input()
    {
        var server = new MCPCustomServer("custom-none", "None", MCPCustomServer.Kind.Stdio) { Command = "true" };
        Assert.False(server.CatalogEntry().NeedsInput);
    }

    // ---- PR-MCP-088: tolerant top-level decode ----

    [Fact]
    [Trait("Req", "PR-MCP-088")] // DroppyCode/Core/Models/MCPCustom.swift:33-49
    public void Decode_fills_command_args_env_url_headers_when_absent()
    {
        var json = """{"id":"custom-x","name":"X","kind":"stdio"}""";
        var server = JsonSerializer.Deserialize<MCPCustomServer>(json)!;
        Assert.Equal("custom-x", server.Id);
        Assert.Equal("X", server.Name);
        Assert.Equal(MCPCustomServer.Kind.Stdio, server.ServerKind);
        Assert.Equal("", server.Command);
        Assert.Empty(server.Args);
        Assert.Empty(server.Env);
        Assert.Equal("", server.Url);
        Assert.Empty(server.Headers);
    }

    [Fact]
    [Trait("Req", "PR-MCP-088")] // DroppyCode/Core/Models/MCPCustom.swift:33-49
    public void Decode_reads_every_present_field_and_only_the_eight_named_keys()
    {
        var json = """
        {"id":"custom-x","name":"X","kind":"http","command":"c","args":["a"],
         "env":[{"key":"E","isSecret":true}],"url":"https://h","headers":[{"key":"H","isSecret":false}]}
        """;
        var server = JsonSerializer.Deserialize<MCPCustomServer>(json)!;
        Assert.Equal("c", server.Command);
        Assert.Equal(["a"], server.Args);
        Assert.Equal("E", Assert.Single(server.Env).Key);
        Assert.True(server.Env[0].IsSecret);
        Assert.Equal("https://h", server.Url);
        Assert.Equal("H", Assert.Single(server.Headers).Key);
        Assert.False(server.Headers[0].IsSecret);
    }

    [Theory]
    [InlineData("""{"name":"X","kind":"stdio"}""")]
    [InlineData("""{"id":"custom-x","kind":"stdio"}""")]
    [InlineData("""{"id":"custom-x","name":"X"}""")]
    [Trait("Req", "PR-MCP-088")] // DroppyCode/Core/Models/MCPCustom.swift:41-43
    public void Decode_throws_when_id_name_or_kind_is_missing(string json) =>
        Assert.Throws<DecodingError>(() => JsonSerializer.Deserialize<MCPCustomServer>(json));

    // ---- PR-MCP-089: the Core-level mechanism a failed load rests on ----

    [Fact]
    [Trait("Req", "PR-MCP-089")] // DroppyCode/Core/Models/MCPCustom.swift:15-19; the Services-level report-and-
    // never-overwrite behaviour (MCPStore) is out of this Core unit's scope and is not implemented here.
    public void Variable_decode_throws_when_isSecret_is_missing_despite_its_property_default()
    {
        var json = """{"key":"K"}""";
        Assert.Throws<DecodingError>(() => JsonSerializer.Deserialize<MCPCustomServer.Variable>(json));
    }

    [Fact]
    [Trait("Req", "PR-MCP-089")] // DroppyCode/Core/Models/MCPCustom.swift:46
    public void Decode_of_the_whole_server_throws_when_an_env_entry_is_missing_isSecret()
    {
        var json = """{"id":"custom-x","name":"X","kind":"stdio","env":[{"key":"K"}]}""";
        Assert.Throws<DecodingError>(() => JsonSerializer.Deserialize<MCPCustomServer>(json));
    }

    // ---- PR-MCP-090: variables selector ----

    [Fact]
    [Trait("Req", "PR-MCP-090")] // DroppyCode/Core/Models/MCPCustom.swift:58
    public void Variables_selects_env_for_stdio_and_headers_for_http_independently_of_the_other_array()
    {
        var server = new MCPCustomServer("custom-x", "X", MCPCustomServer.Kind.Stdio)
        {
            Env = new ValueList<MCPCustomServer.Variable>([new MCPCustomServer.Variable("E")]),
            Headers = new ValueList<MCPCustomServer.Variable>([new MCPCustomServer.Variable("H")]),
        };
        Assert.Equal(["E"], server.Variables.Select(v => v.Key));

        var http = server with { ServerKind = MCPCustomServer.Kind.Http };
        Assert.Equal(["H"], http.Variables.Select(v => v.Key));
        // Switching kind leaves the old env list on disk untouched.
        Assert.Equal(["E"], http.Env.Select(v => v.Key));
    }

    // ---- PR-MCP-091: validate order ----

    [Fact]
    [Trait("Req", "PR-MCP-091")] // DroppyCode/Core/Models/MCPCustom.swift:151-152
    public void Validate_rejects_a_blank_name_before_any_other_check()
    {
        var server = new MCPCustomServer("", "   ", MCPCustomServer.Kind.Stdio);
        Assert.Equal(new MCPCustomError.EmptyName(), MCPCustomError.Validate(server, new HashSet<string>()));
    }

    [Fact]
    [Trait("Req", "PR-MCP-091")] // DroppyCode/Core/Models/MCPCustom.swift:153-154
    public void Validate_rejects_a_name_whose_id_is_already_taken()
    {
        var server = new MCPCustomServer("", "My Server", MCPCustomServer.Kind.Stdio) { Command = "x" };
        var result = MCPCustomError.Validate(server, new HashSet<string> { "custom-my-server" });
        Assert.Equal(new MCPCustomError.TakenName("My Server"), result);
    }

    [Fact]
    [Trait("Req", "PR-MCP-091")] // DroppyCode/Core/Models/MCPCustom.swift:156-157
    public void Validate_rejects_a_blank_stdio_command()
    {
        var server = new MCPCustomServer("", "X", MCPCustomServer.Kind.Stdio) { Command = "   " };
        Assert.Equal(new MCPCustomError.EmptyCommand(), MCPCustomError.Validate(server, new HashSet<string>()));
    }

    [Fact]
    [Trait("Req", "PR-MCP-091")] // DroppyCode/Core/Models/MCPCustom.swift:158-159
    public void Validate_rejects_a_bad_http_url()
    {
        var server = new MCPCustomServer("", "X", MCPCustomServer.Kind.Http) { Url = "ws://example.com" };
        Assert.Equal(new MCPCustomError.BadURL(), MCPCustomError.Validate(server, new HashSet<string>()));
    }

    [Fact]
    [Trait("Req", "PR-MCP-091")] // DroppyCode/Core/Models/MCPCustom.swift:163
    public void Validate_rejects_an_invalid_variable_key()
    {
        var server = new MCPCustomServer("", "X", MCPCustomServer.Kind.Stdio)
        {
            Command = "x",
            Env = new ValueList<MCPCustomServer.Variable>([new MCPCustomServer.Variable("1bad")]),
        };
        Assert.Equal(new MCPCustomError.BadVariableKey("1bad"), MCPCustomError.Validate(server, new HashSet<string>()));
    }

    [Fact]
    [Trait("Req", "PR-MCP-091")] // DroppyCode/Core/Models/MCPCustom.swift:164-165
    public void Validate_treats_http_header_keys_as_case_insensitive_duplicates()
    {
        var server = new MCPCustomServer("", "X", MCPCustomServer.Kind.Http)
        {
            Url = "https://example.com",
            Headers = new ValueList<MCPCustomServer.Variable>(
                [new MCPCustomServer.Variable("Authorization"), new MCPCustomServer.Variable("authorization")]),
        };
        var result = MCPCustomError.Validate(server, new HashSet<string>());
        Assert.Equal(new MCPCustomError.DuplicateVariableKey("authorization"), result);
    }

    [Fact]
    [Trait("Req", "PR-MCP-091")] // DroppyCode/Core/Models/MCPCustom.swift:164-165
    public void Validate_treats_stdio_env_keys_as_case_sensitive_so_PATH_and_path_are_both_accepted()
    {
        var server = new MCPCustomServer("", "X", MCPCustomServer.Kind.Stdio)
        {
            Command = "x",
            Env = new ValueList<MCPCustomServer.Variable>(
                [new MCPCustomServer.Variable("PATH"), new MCPCustomServer.Variable("path")]),
        };
        Assert.Null(MCPCustomError.Validate(server, new HashSet<string>()));
    }

    [Fact]
    [Trait("Req", "PR-MCP-091")] // DroppyCode/Core/Models/MCPCustom.swift:150-168
    public void Validate_returns_null_for_a_fully_valid_server()
    {
        var server = new MCPCustomServer("", "OK", MCPCustomServer.Kind.Http)
        {
            Url = "https://example.com",
            Headers = new ValueList<MCPCustomServer.Variable>([new MCPCustomServer.Variable("Authorization")]),
        };
        Assert.Null(MCPCustomError.Validate(server, new HashSet<string>()));
    }

    // ---- PR-MCP-092: validVariableKey ----

    [Theory]
    [InlineData("", false)]
    [InlineData("1abc", false)]
    [InlineData("_a.b-c", true)]
    [InlineData("A", true)]
    [InlineData("_", true)]
    [Trait("Req", "PR-MCP-092")] // DroppyCode/Core/Models/MCPCustom.swift:143-146
    public void ValidVariableKey_requires_a_letter_or_underscore_first(string key, bool expected) =>
        Assert.Equal(expected, MCPCustomError.ValidVariableKey(key));

    [Fact]
    [Trait("Req", "PR-MCP-092")] // DroppyCode/Core/Models/MCPCustom.swift:143-146
    public void ValidVariableKey_accepts_unicode_letters()
    {
        Assert.True(MCPCustomError.ValidVariableKey("über")); // U+00FC LATIN SMALL LETTER U WITH DIAERESIS
    }

    // ---- PR-MCP-093: fixed error messages ----

    [Fact]
    [Trait("Req", "PR-MCP-093")] // DroppyCode/Core/Models/MCPCustom.swift:130-139
    public void Error_messages_match_the_pinned_source_byte_for_byte()
    {
        Assert.Equal("The server needs a name.", new MCPCustomError.EmptyName().Message);
        Assert.Equal(
            "A server named “mine” already exists.", // U+201C, U+201D curly quotes
            new MCPCustomError.TakenName("mine").Message);
        Assert.Equal("A stdio server needs a command.", new MCPCustomError.EmptyCommand().Message);
        Assert.Equal(
            "The URL must start with https:// (or http:// for a local server).",
            new MCPCustomError.BadURL().Message);
        Assert.Equal(
            "“k” is not a valid variable name: letters, digits, `_`, `-` and `.`, starting with a letter or `_`.",
            new MCPCustomError.BadVariableKey("k").Message);
        Assert.Equal("“k” is listed twice.", new MCPCustomError.DuplicateVariableKey("k").Message);
    }

    // ---- PR-MCP-094: validRemoteURL ----

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("https://localhost", true)]
    [InlineData("http://localhost", true)]
    [InlineData("http://127.0.0.1", true)]
    [InlineData("http://[::1]", true)]
    [InlineData("http://example.com", false)]
    [InlineData("ws://example.com", false)]
    [InlineData("", false)]
    [InlineData("not a url", false)]
    [Trait("Req", "PR-MCP-094")] // DroppyCode/Core/Models/MCPCustom.swift:172-182
    public void ValidRemoteURL_accepts_https_anywhere_and_http_only_on_loopback(string url, bool expected) =>
        Assert.Equal(expected, MCPCustomError.ValidRemoteURL(url));

    [Fact]
    [Trait("Req", "PR-MCP-094")] // DroppyCode/Core/Models/MCPCustom.swift:173
    public void ValidRemoteURL_trims_surrounding_whitespace()
    {
        Assert.True(MCPCustomError.ValidRemoteURL("  https://example.com  \n"));
    }
}

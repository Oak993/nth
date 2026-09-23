using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// Source: DroppyCode/Core/Models/MCP.swift (B2 1fbf81e), :1-263. Every expected value below is transcribed from
// that file, the records PR-MCP-038 to PR-MCP-048, PR-MCP-119, and the shipped catalog (MCPCatalog.swift), never
// from the C# implementation. resolve(values:) and MCPPaths reach Services (MCPProxy, Storage.root); they move up a
// layer (HANDOFF section 9, IMPLEMENTING 2.1) and are tested in tests/Marrow.Services.Tests/Models/MCPTests.cs.
public sealed class MCPTests
{
    // Swift [String: String]: keys compare by canonical equivalence (R-STR-2).
    private static ValueMap<string, string> M(params (string Key, string Value)[] pairs) =>
        new(pairs.Select(p => KeyValuePair.Create(p.Key, p.Value)), SwiftStringComparer.Instance);

    private static ValueList<T> L<T>(params T[] items) => new(items);

    private static MCPField Field(string key, string? defaultValue = null, bool isRequired = true) =>
        new(Key: key, Label: key, Placeholder: "", DefaultValue: defaultValue, IsRequired: isRequired);

    // A trimmed MCPCatalogEntry (MCP.swift:65-91), shaped like the shipped entries of MCPCatalog.swift.
    private static MCPCatalogEntry Entry(MCPTransport transport, ValueList<MCPField> fields) =>
        new MCPCatalogEntry(Id: "server", Name: "Server", Vendor: "Vendor", Summary: "Summary",
            Category: MCPCategory.Developer, Asset: "mcp-server", ColorValue: 0x123456, Transport: transport,
            DocsURL: "docs") { Fields = fields };

    private static MCPCatalogEntry Entry(params MCPField[] fields) => Entry(new MCPTransport.Oauth("https://{instance}/api"), L(fields));

    // ---- PR-MCP-038 -------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-038")] // MCP.swift:39-48 enum MCPCategory: String, CaseIterable; id = rawValue
    public void Category_raw_values_are_the_lowercase_case_names_in_declaration_order_and_the_id_is_the_raw_value()
    {
        var expected = new[] { "developer", "browser", "search", "work", "data", "cloud", "knowledge" };
        Assert.Equal(
            new[] { MCPCategory.Developer, MCPCategory.Browser, MCPCategory.Search, MCPCategory.Work, MCPCategory.Data, MCPCategory.Cloud, MCPCategory.Knowledge },
            SwiftEnum<MCPCategory>.AllCases);
        Assert.Equal(expected, SwiftEnum<MCPCategory>.AllCases.Select(SwiftEnum<MCPCategory>.RawValue));
        Assert.Equal(expected, SwiftEnum<MCPCategory>.AllCases.Select(c => c.Id()));
        Assert.Equal(MCPCategory.Cloud, SwiftEnum<MCPCategory>.FromRawValue("cloud"));
        Assert.Null(SwiftEnum<MCPCategory>.FromRawValue("Cloud")); // a raw value is exact and case-sensitive
    }

    [Fact]
    [Trait("Req", "PR-MCP-038")] // MCP.swift:50-60 var title
    public void Category_titles_are_the_seven_fixed_strings_byte_for_byte()
    {
        string[] expected = ["Developer", "Browser", "Search", "Work", "Data", "Cloud", "Knowledge"];
        var titles = SwiftEnum<MCPCategory>.AllCases.Select(c => c.Title()).ToArray();
        Assert.Equal(expected.Length, titles.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(Encoding.UTF8.GetBytes(expected[i]), Encoding.UTF8.GetBytes(titles[i]));
    }

    // ---- PR-MCP-039 -------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-039")] // MCP.swift:135-150 fill: for field in fields, replacingOccurrences(of: "{key}")
    public void Fill_replaces_every_occurrence_in_field_order_and_leaves_tokens_of_non_fields_alone()
    {
        // Field order matters: a's default introduces {b}, which the later field b then fills.
        var entry = Entry(Field("a", "{b}"), Field("b", "fallback"), Field("c"));
        Assert.Equal("fallback/fallback//{unknown}", entry.Fill("{a}/{a}/{c}/{unknown}", M(("a", ""))));
        Assert.Equal(" typed / typed //{unknown}", entry.Fill("{a}/{a}/{c}/{unknown}", M(("a", " typed "))));
        Assert.Equal("fallback", entry.Fill("{a}", M()));
        // The reverse order: b is filled first, so a's default "{b}" arrives after b's turn and stays.
        var reversed = Entry(Field("b", "fallback"), Field("a", "{b}"));
        Assert.Equal("{b}", reversed.Fill("{a}", M()));
        // A field the text does not reference has no effect.
        Assert.Equal("plain {x} text", entry.Fill("plain {x} text", M(("a", "A"), ("b", "B"), ("c", "C"))));
    }

    [Fact]
    [Trait("Req", "PR-MCP-039")] // MCP.swift:138 values[field.key].flatMap { $0.isEmpty ? nil : $0 } ?? field.defaultValue ?? ""
    public void Fill_takes_a_nonempty_typed_value_else_the_default_else_the_empty_string()
    {
        var withDefault = Entry(Field("a", "dflt"));
        Assert.Equal("[typed]", withDefault.Fill("[{a}]", M(("a", "typed"))));
        Assert.Equal("[dflt]", withDefault.Fill("[{a}]", M(("a", ""))));      // empty falls through to the default
        Assert.Equal("[dflt]", withDefault.Fill("[{a}]", M()));                // missing too
        Assert.Equal("[ ]", withDefault.Fill("[{a}]", M(("a", " "))));        // only isEmpty is checked: a space is a value
        Assert.Equal("[\t\n]", withDefault.Fill("[{a}]", M(("a", "\t\n")))); // and so is other whitespace (not after "://")
        var noDefault = Entry(Field("a"));
        Assert.Equal("[]", noDefault.Fill("[{a}]", M()));
        Assert.Equal("[]", noDefault.Fill("[{a}]", M(("a", ""))));
    }

    [Fact]
    [Trait("Req", "PR-MCP-039")] // MCP.swift:147 replacingOccurrences(of:with:) has no .caseInsensitive option
    public void Fill_matches_tokens_case_sensitively()
    {
        var entry = Entry(Field("a", "dflt"));
        Assert.Equal("{A}|V", entry.Fill("{A}|{a}", M(("a", "V"))));
        Assert.Equal("{A}", entry.Fill("{A}", M(("A", "V"))));
    }

    [Fact]
    [Trait("Req", "PR-MCP-039")] // MCP.swift:138 values[field.key] (Swift String keys), :147 non-literal replacingOccurrences
    public void Fill_matches_keys_and_tokens_by_canonical_equivalence()
    {
        const string composed = "caf\U000000E9";    // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        const string decomposed = "cafe\U00000301"; // U+0301 COMBINING ACUTE ACCENT
        var entry = Entry(Field(composed));
        var text = "{" + composed + "}|{" + decomposed + "}";
        Assert.Equal("V|V", entry.Fill(text, M((composed, "V"))));
        Assert.Equal("V|V", entry.Fill(text, M((decomposed, "V"))));
    }

    // ---- PR-MCP-040 -------------------------------------------------------------------------------------

    [Theory]
    [Trait("Req", "PR-MCP-040")] // MCP.swift:140-146 trimmed, the ordered scheme loop over lowercased(), trailing "/" removal
    [InlineData("  HTTPS://gitlab.example.com///\r\n", "https://gitlab.example.com/api")]
    [InlineData("  https://gitlab.example.com/  ", "https://gitlab.example.com/api")]
    [InlineData("http://gitlab.example.com", "https://gitlab.example.com/api")]
    [InlineData("https://http://x", "https://x/api")]       // both schemes, in the loop's order
    [InlineData("http://https://x", "https://https://x/api")] // https is tested first, so it survives here
    [InlineData("///", "https:///api")]
    [InlineData("\t/\n", "https:///api")]
    [InlineData("//host/", "https:////host/api")]             // only TRAILING slashes go
    [InlineData("host\\", "https://host\\/api")]              // a backslash is not a slash
    [InlineData("\U00000085https://x/\U00002028", "https://x/api")] // U+0085 NEXT LINE, U+2028 LINE SEPARATOR: Foundation newlines
    [InlineData("HTTPS://\U00000301x", "https://HTTPS://\U00000301x/api")] // U+0301 COMBINING ACUTE ACCENT joins the "/" into one Character
    [InlineData("x\U00000600/", "https://x\U00000600//api")] // U+0600 ARABIC NUMBER SIGN (Prepend) joins the "/" into one Character
    public void Host_normalization_matches_the_ordered_source_scheme_loop(string input, string expected)
    {
        var entry = Entry(new MCPField(Key: "instance", Label: "Instance", Placeholder: "", FieldKind: MCPField.Kind.Text));
        var values = M(("instance", input));
        Assert.Equal(expected, entry.Fill("https://{instance}/api", values));
        Assert.Equal(input, entry.Fill("{instance}", values));        // no "://" before the token: untouched
        Assert.Equal("x " + input, entry.Fill("x {instance}", values));
    }

    [Fact]
    [Trait("Req", "PR-MCP-040")] // MCP.swift:140 out.contains("://" + token), with out rewritten at :147 by earlier fields
    public void Host_normalization_is_decided_on_the_running_output_not_the_original_text()
    {
        var entry = Entry(Field("a", "https://{b}"), Field("b"));
        Assert.Equal("https://h", entry.Fill("{a}", M(("b", "https://h/"))));
        // An earlier field's typed value that puts "://" right before a later token counts too; without it, no change.
        var other = Entry(Field("a"), Field("b"));
        Assert.Equal("xhttps://h/", other.Fill("x{a}{b}", M(("a", ""), ("b", "https://h/"))));
        Assert.Equal("x://h", other.Fill("x{a}{b}", M(("a", "://"), ("b", "https://h/"))));
    }

    [Fact]
    [Trait("Rule", "R-STR-2")]
    [Trait("Rule", "R-STR-4")]
    public void Negative_control_dotnet_prefix_and_suffix_tests_split_Characters_that_Swift_keeps_whole()
    {
        // MCP.swift:142 and :145 test whole Characters (SwiftString.HasPrefix/HasSuffix). The .NET calls the earlier
        // port used report a match inside a Character, which is why the two theory rows above expect no stripping.
        Assert.StartsWith("https://", "HTTPS://\U00000301x", StringComparison.OrdinalIgnoreCase); // U+0301 COMBINING ACUTE ACCENT
        Assert.EndsWith("/", "x\U00000600/", StringComparison.Ordinal);                            // U+0600 ARABIC NUMBER SIGN
        Assert.False(SwiftString.HasPrefix(SwiftString.Lowercased("HTTPS://\U00000301x"), "https://"));
        Assert.False(SwiftString.HasSuffix("x\U00000600/", "/"));
    }

    // ---- PR-MCP-044 -------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-044")] // MCP.swift:127-130 oauthURL(values: [:]) fills an .oauth url, nil otherwise
    public void OAuth_URL_is_the_filled_remote_address_for_an_oauth_transport_and_nil_otherwise()
    {
        var gitlabLike = Entry(new MCPTransport.Oauth("https://{instance}/api/v4/mcp"),
            L(new MCPField(Key: "instance", Label: "GitLab address", Placeholder: "gitlab.com", FieldKind: MCPField.Kind.Text, DefaultValue: "gitlab.com", IsRequired: false)));
        Assert.Equal("https://gitlab.com/api/v4/mcp", gitlabLike.OauthURL());
        Assert.Equal("https://gitlab.com/api/v4/mcp", gitlabLike.OauthURL(M()));
        Assert.Equal("https://self.host/api/v4/mcp", gitlabLike.OauthURL(M(("instance", "https://self.host/"))));
        Assert.Equal("https://gitlab.com/api/v4/mcp", MCPCatalog.Entry("gitlab")!.OauthURL());
        foreach (var transport in new MCPTransport[] { new MCPTransport.Stdio("npx", L("{instance}")), new MCPTransport.Http("https://{instance}", M()) })
            Assert.Null((gitlabLike with { Transport = transport }).OauthURL(M(("instance", "x"))));
    }

    // ---- PR-MCP-045 -------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-045")] // MCP.swift:95 needsInput = fields.contains { $0.isRequired }; :34 isRequired = true
    public void NeedsInput_is_true_only_when_some_field_is_required()
    {
        Assert.False(Entry().NeedsInput);
        Assert.False(Entry(Field("a", isRequired: false), Field("b", isRequired: false)).NeedsInput);
        Assert.True(Entry(Field("a", isRequired: false), Field("b")).NeedsInput);
        var plain = new MCPField(Key: "k", Label: "K", Placeholder: "");
        Assert.True(plain.IsRequired);                   // :34
        Assert.Equal(MCPField.Kind.Secret, plain.FieldKind); // :29
        Assert.Null(plain.Help);                          // :31
        Assert.Null(plain.DefaultValue);                  // :33
        Assert.Equal("k", plain.Id);                      // :36
        Assert.True(Entry(plain).NeedsInput);
        // The shipped catalog (MCPCatalog.swift:32, :71, :424 optional fields; :148 a required key).
        Assert.False(MCPCatalog.Entry("gitlab")!.NeedsInput);
        Assert.False(MCPCatalog.Entry("context7")!.NeedsInput);
        Assert.False(MCPCatalog.Entry("huggingface")!.NeedsInput);
        Assert.True(MCPCatalog.Entry("brave-search")!.NeedsInput);
    }

    // ---- PR-MCP-046 -------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-046")] // MCP.swift:97-100 isOAuth; :118 isRemote = url != nil
    public void IsOAuth_follows_the_transport_case_and_isRemote_is_exactly_url_not_nil()
    {
        MCPTransport[] samples = [new MCPTransport.Stdio("npx", L<string>()), new MCPTransport.Http("https://h", M()), new MCPTransport.Oauth("https://o")];
        foreach (var transport in UnionCoverage.AssertCovers(samples))
            Assert.Equal(transport is MCPTransport.Oauth, Entry(transport, L<MCPField>()).IsOAuth);
        Assert.False(new MCPResolvedServer(Id: "id", Name: "name").IsRemote);
        Assert.True(new MCPResolvedServer(Id: "id", Name: "name") { Url = "" }.IsRemote);
        Assert.False(new MCPResolvedServer(Id: "id", Name: "name") { Command = "npx", OauthUpstream = "https://o" }.IsRemote);
    }

    // ---- PR-MCP-047 -------------------------------------------------------------------------------------

    private static MCPConnection SampleConnection() =>
        new MCPConnection(CatalogID: "github")
        {
            IsEnabled = false,
            Values = M(("path", "/repo")),
            ConnectedAt = new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.Zero),
            ServerVersion = "1.2",
            Tools = L(new MCPToolSummary(Name: "search", Summary: "Find")),
        };

    [Fact]
    [Trait("Req", "PR-MCP-047")] // MCP.swift:188-199 the six Codable fields, ISO-8601 date (Coding.swift:19-26 storage coder)
    public void Connection_JSON_uses_the_six_exact_keys_and_round_trips()
    {
        const string expected = "{\"catalogID\":\"github\",\"connectedAt\":\"2026-09-22T01:02:03Z\",\"isEnabled\":false,\"serverVersion\":\"1.2\",\"tools\":[{\"name\":\"search\",\"summary\":\"Find\"}],\"values\":{\"path\":\"\\/repo\"}}";
        Assert.Equal(Encoding.UTF8.GetBytes(expected), Coding.Encode(SampleConnection()));
        var restored = Coding.Decode<MCPConnection>(Encoding.UTF8.GetBytes(expected))!;
        Assert.Equal(SampleConnection(), restored);
        Assert.Equal("github", restored.Id);
        Assert.False(restored.IsEnabled);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.Zero), restored.ConnectedAt);
        Assert.Equal("/repo", restored.Values["path"]);
        Assert.Equal("1.2", restored.ServerVersion);
        Assert.Equal(new MCPToolSummary(Name: "search", Summary: "Find"), Assert.Single(restored.Tools));
        Assert.Equal("search", new MCPToolSummary(Name: "search", Summary: null).Id);
    }

    [Fact]
    [Trait("Req", "PR-MCP-047")] // MCP.swift:191 isEnabled = true, :193 values = [:], :196 tools = []; optionals nil and omitted
    public void A_new_connection_is_enabled_and_empty_and_omits_its_nil_optionals()
    {
        var empty = new MCPConnection(CatalogID: "id");
        Assert.True(empty.IsEnabled);
        Assert.Empty(empty.Values);
        Assert.Empty(empty.Tools);
        Assert.Null(empty.ConnectedAt);
        Assert.Null(empty.ServerVersion);
        Assert.Equal("id", empty.Id);
        Assert.Equal("{\"catalogID\":\"id\",\"isEnabled\":true,\"tools\":[],\"values\":{}}", Encoding.UTF8.GetString(Coding.Encode(empty)));
    }

    [Fact]
    [Trait("Req", "PR-MCP-047")] // MCPStore.swift:262-268 writes mcp.json with [.prettyPrinted, .sortedKeys] and .iso8601
    public void The_mcp_json_bytes_of_a_connection_list_are_Apples_pretty_sorted_layout()
    {
        const string expected =
            "[\n" +
            "  {\n" +
            "    \"catalogID\" : \"github\",\n" +
            "    \"connectedAt\" : \"2026-09-22T01:02:03Z\",\n" +
            "    \"isEnabled\" : false,\n" +
            "    \"serverVersion\" : \"1.2\",\n" +
            "    \"tools\" : [\n" +
            "      {\n" +
            "        \"name\" : \"search\",\n" +
            "        \"summary\" : \"Find\"\n" +
            "      }\n" +
            "    ],\n" +
            "    \"values\" : {\n" +
            "      \"path\" : \"\\/repo\"\n" +
            "    }\n" +
            "  }\n" +
            "]";
        var bytes = SwiftJson.Encode(L(SampleConnection()), Coding.StorageOptions, SwiftJsonStyle.PrettySorted);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), bytes);
        Assert.Equal(SampleConnection(), Assert.Single(Coding.Decode<MCPConnection[]>(bytes)!));
    }

    [Theory] // Synthesized Decodable: a non-optional var is required even with an initial value (R-CODE-1).
    [Trait("Req", "PR-MCP-047")] // MCP.swift:188-199; MCPStore.swift:251-252 a failed decode leaves connections empty
    [Trait("Rule", "R-CODE-1")]
    [InlineData("{}")]
    [InlineData("{\"catalogID\":\"x\"}")]
    [InlineData("{\"isEnabled\":true,\"values\":{},\"tools\":[]}")]                        // catalogID missing
    [InlineData("{\"catalogID\":\"x\",\"values\":{},\"tools\":[]}")]                       // isEnabled missing
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"tools\":[]}")]                  // values missing
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{}}")]                 // tools missing
    [InlineData("{\"catalogID\":null,\"isEnabled\":true,\"values\":{},\"tools\":[]}")]
    [InlineData("{\"catalogID\":1,\"isEnabled\":true,\"values\":{},\"tools\":[]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":null,\"values\":{},\"tools\":[]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":1,\"values\":{},\"tools\":[]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":\"true\",\"values\":{},\"tools\":[]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":null,\"tools\":[]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":[],\"tools\":[]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{\"a\":null},\"tools\":[]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{\"a\":1},\"tools\":[]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{},\"tools\":null}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{},\"tools\":{}}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{},\"tools\":[{}]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{},\"tools\":[null]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{},\"tools\":[{\"name\":\"t\",\"summary\":1}]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{},\"tools\":[],\"serverVersion\":1}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{},\"tools\":[],\"connectedAt\":1}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{},\"tools\":[],\"connectedAt\":\"yesterday\"}")]
    public void Malformed_or_missing_connection_fields_fail_decoding(string json) =>
        Assert.ThrowsAny<JsonException>(() => Coding.Decode<MCPConnection>(Encoding.UTF8.GetBytes(json)));

    [Theory]
    [Trait("Req", "PR-MCP-047")] // MCP.swift:194-195 connectedAt and serverVersion are Optional: decodeIfPresent
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{},\"tools\":[]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{},\"tools\":[],\"connectedAt\":null,\"serverVersion\":null}")]
    public void Absent_or_null_optional_connection_fields_decode_as_nil(string json)
    {
        var connection = Coding.Decode<MCPConnection>(Encoding.UTF8.GetBytes(json))!;
        Assert.Null(connection.ConnectedAt);
        Assert.Null(connection.ServerVersion);
        Assert.Equal(new MCPConnection(CatalogID: "x"), connection);
    }

    [Fact] // PARKED, not a requirement proof: MCPStore.swift:251 decodes with .iso8601; whether that rejects fractional
    [Trait("Rule", "R-CODE-6")]
    [Trait("Oracle", "O-02")] // seconds is O-02, parked as accepted (R-CODE-6). When O-02 settles, this expectation is replaced.
    public void Fractional_seconds_in_connectedAt_follow_the_parked_O_02_rule()
    {
        var json = "{\"catalogID\":\"x\",\"connectedAt\":\"2026-01-02T03:04:05.123Z\",\"isEnabled\":true,\"tools\":[],\"values\":{}}";
        var connection = Coding.Decode<MCPConnection>(Encoding.UTF8.GetBytes(json))!;
        Assert.NotNull(connection.ConnectedAt);
    }

    // ---- PR-MCP-048 -------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-048")] // MCP.swift:218-232 five cases; isConnected only .connected; isConnecting only .connecting
    public void Connection_state_has_five_cases_and_the_two_flags_are_true_for_one_case_each()
    {
        var guide = new MCPSetupGuide(Title: "t", Reason: "r", Steps: L("s"), PageLabel: "l", PageURL: "u", Note: null);
        MCPConnectionState[] samples = [
            new MCPConnectionState.NotConnected(),
            new MCPConnectionState.Connecting(),
            new MCPConnectionState.Connected(ToolCount: 3),
            new MCPConnectionState.Failed(Message: "boom"),
            new MCPConnectionState.NeedsSetup(guide),
        ];
        Assert.Equal(5, UnionCoverage.Cases<MCPConnectionState>().Count);
        foreach (var state in UnionCoverage.AssertCovers(samples))
        {
            Assert.Equal(state is MCPConnectionState.Connected, state.IsConnected);
            Assert.Equal(state is MCPConnectionState.Connecting, state.IsConnecting);
        }
        Assert.True(new MCPConnectionState.Connected(ToolCount: 0).IsConnected);
    }

    [Fact]
    [Trait("Req", "PR-MCP-048")] // MCP.swift:218 Hashable: the tool count and message ride inside their cases
    public void Connection_states_compare_by_case_and_payload()
    {
        Assert.Equal(new MCPConnectionState.Connecting(), new MCPConnectionState.Connecting());
        Assert.Equal(new MCPConnectionState.NotConnected(), new MCPConnectionState.NotConnected());
        Assert.NotEqual<MCPConnectionState>(new MCPConnectionState.NotConnected(), new MCPConnectionState.Connecting());
        Assert.Equal(new MCPConnectionState.Connected(ToolCount: 3), new MCPConnectionState.Connected(ToolCount: 3));
        Assert.NotEqual(new MCPConnectionState.Connected(ToolCount: 3), new MCPConnectionState.Connected(ToolCount: 2));
        Assert.Equal(3L, new MCPConnectionState.Connected(ToolCount: 3).ToolCount);
        Assert.NotEqual(new MCPConnectionState.Failed(Message: "a"), new MCPConnectionState.Failed(Message: "b"));
        Assert.Equal("a", new MCPConnectionState.Failed(Message: "a").Message);
    }

    // ---- PR-MCP-119 -------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-119")] // MCP.swift:203-215 six fields, steps in order, note optional; Sendable, Hashable, not Codable
    public void Setup_guide_carries_six_fields_in_order_and_is_not_a_persisted_type()
    {
        var guide = new MCPSetupGuide(Title: "Turn on GitLab's MCP server", Reason: "why", Steps: L("one", "two"),
            PageLabel: "Open my groups", PageURL: "https://gitlab.com/dashboard/groups", Note: null);
        Assert.Equal("Turn on GitLab's MCP server", guide.Title);
        Assert.Equal("why", guide.Reason);
        Assert.Equal(new[] { "one", "two" }, guide.Steps);
        Assert.Equal("Open my groups", guide.PageLabel);
        Assert.Equal("https://gitlab.com/dashboard/groups", guide.PageURL);
        Assert.Null(guide.Note);
        Assert.Equal("later", (guide with { Note = "later" }).Note);
        Assert.Equal(guide, guide with { Steps = L("one", "two") });
        Assert.NotEqual(guide, guide with { Steps = L("two", "one") });
        Assert.NotEqual(guide, guide with { Note = "later" });
        Assert.Equal(new MCPConnectionState.NeedsSetup(guide), new MCPConnectionState.NeedsSetup(guide with { Steps = L("one", "two") }));
        // Neither the guide nor the state is Codable in Swift, so neither carries a JSON converter.
        Assert.Empty(typeof(MCPSetupGuide).GetCustomAttributes(typeof(JsonConverterAttribute), inherit: true));
        Assert.Empty(typeof(MCPConnectionState).GetCustomAttributes(typeof(JsonConverterAttribute), inherit: true));
    }
}

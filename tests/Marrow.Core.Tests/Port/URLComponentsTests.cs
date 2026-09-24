using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// ED-169: URLComponents and URLQueryItem stand in for Foundation's, as the pinned source uses them. The call sites,
// every URLComponents use at B2 1fbf81e:
//   Core/Models/MCPCustom.swift:174-176             URLComponents(string:), scheme, host
//   Services/MCP/MCPOAuth.swift:317                  URLComponents(string:), path
//   Services/MCP/MCPOAuth.swift:526-528              URLComponents(string:)?.queryItems, URLQueryItem name and value
//   Services/MCP/MCPOAuth.swift:581-598              URLComponents(string:), queryItems read, append, set, url
//   Services/MCP/MCPOAuth.swift:772-778              URLComponents(), queryItems set, percentEncodedQuery (the form body)
//   Services/MCP/MCPOAuth.swift:801-810              URLComponents(string:), scheme, host, port
//   App/AppModel+HydraMerge.swift:384-387            URLComponents(url:resolvingAgainstBaseURL: false), user = nil,
//                                                    password = nil, url
//   UI/Markdown/LinkTitles.swift:25-26               URLComponents(url:resolvingAgainstBaseURL: false)?.queryItems
//   UI/Markdown/LinkTitles.swift:63-67               URLComponents(string:), queryItems set, url
// Expected values are derived by hand from swift-foundation, the open-source Foundation that macOS 26's URL and
// URLComponents are built from: tag swift-6.2-RELEASE (the revision SwiftURL ports), Sources/FoundationEssentials/URL/
// URLComponents.swift and URLParser.swift, cited per test. The same logic stands unchanged at main b898bbce
// (2026-09-22) except where a test says otherwise. Whether the macOS 26 binary matches the source line for line is the
// transfer question of ED-086; tests whose value rests on a behaviour ED-169 names as unsettled carry an Oracle trait:
//   O-57  the queryItems setter's encoding (URLComponents.swift:638-667 with the queryItem set, URLParser.swift:1218)
//   O-58  the queryItems getter's shapes (URLComponents.swift:590-636)
//   O-59  URLComponents.host is IDNA-decoded and keeps IP-literal brackets (URLComponents.swift:184-193)
//   O-60  url is URL(stringOrEmpty:) of the recomputed string (URLComponents.swift:434-490, :765-773)
// (O-57 to O-60 are the numbers proposed to the orchestrator for these items.)
// Every non-ASCII character is written as a \U escape (R-STR-9).
public sealed class URLComponentsTests
{
    private const string EAcute = "\U000000E9"; // U+00E9 LATIN SMALL LETTER E WITH ACUTE
    private const string UUmlaut = "\U000000FC"; // U+00FC LATIN SMALL LETTER U WITH DIAERESIS

    private static (string Name, string? Value)[] Pairs(IEnumerable<URLQueryItem>? items)
    {
        Assert.NotNull(items);
        return items.Select(item => (item.Name, item.Value)).ToArray();
    }

    // ---- Parsing a string ----

    [Fact]
    [Trait("Decision", "ED-169")] // MCPCustom.swift:174-176, MCPOAuth.swift:317, :801-810; URLComponents.swift:139-143, :160-182, :230-248
    public void A_parsed_string_gives_its_scheme_user_password_host_port_decoded_path_and_encoded_query()
    {
        var components = URLComponents.Create("https://al%40ice:p%3Ass@example.com:8443/a%20b/c?x=1&y=%20#frag");
        Assert.NotNull(components);
        Assert.Equal("https", components.Scheme);
        Assert.Equal("al@ice", components.User);
        Assert.Equal("p:ss", components.Password);
        Assert.Equal("example.com", components.Host);
        Assert.Equal(8443L, components.Port);
        Assert.Equal("/a b/c", components.Path);
        Assert.Equal("x=1&y=%20", components.PercentEncodedQuery);
    }

    [Fact]
    [Trait("Decision", "ED-169")] // MCPCustom.swift:175 lowercases what it reads; URLComponents.swift:139-143 returns the parsed substring
    public void The_scheme_is_kept_as_written()
    {
        var components = URLComponents.Create("HTTPS://example.com/mcp");
        Assert.NotNull(components);
        Assert.Equal("HTTPS", components.Scheme);
    }

    [Fact]
    [Trait("Decision", "ED-169")] // MCPOAuth.swift:317 !components.path.isEmpty, components.path != "/"; URLComponents.swift:242-245, :363-367
    public void The_path_is_empty_with_no_path_a_slash_with_only_a_slash_and_the_decoded_path_otherwise()
    {
        Assert.Equal("", URLComponents.Create("https://auth.example.com")!.Path);
        Assert.Equal("/", URLComponents.Create("https://auth.example.com/")!.Path);
        Assert.Equal("/mcp", URLComponents.Create("https://access.stripe.com/mcp")!.Path);
        Assert.Equal("/a/b c", URLComponents.Create("https://x.example/a%2Fb%20c")!.Path);
    }

    [Fact]
    [Trait("Decision", "ED-169")] // MCPOAuth.swift:808-810 if let port; URLComponents.swift:230-232, URLParser.swift:139-144 Int(portString)
    public void The_port_is_the_number_written_or_nil()
    {
        Assert.Equal(8443L, URLComponents.Create("https://auth.example.com:8443/x")!.Port);
        Assert.Equal(443L, URLComponents.Create("https://auth.example.com:0443/x")!.Port);
        Assert.Null(URLComponents.Create("https://auth.example.com/x")!.Port);
    }

    [Fact]
    [Trait("Decision", "ED-169")] // MCPCustom.swift:176 let host = parsed.host, !host.isEmpty; URLComponents.swift:184-187, :343-348
    public void An_authority_with_no_host_gives_an_empty_host_and_no_authority_gives_nil()
    {
        Assert.Equal("", URLComponents.Create("https:///path")!.Host);
        var mail = URLComponents.Create("mailto:someone@example.com");
        Assert.NotNull(mail);
        Assert.Equal("mailto", mail.Scheme);
        Assert.Null(mail.Host);
    }

    [Fact]
    [Trait("Decision", "ED-169")] // MCPCustom.swift:173-174 an empty field; URLParser.swift:777-781 an empty buffer parses to an empty path
    public void An_empty_string_parses_to_components_with_no_scheme_no_host_and_an_empty_path()
    {
        var components = URLComponents.Create("");
        Assert.NotNull(components);
        Assert.Null(components.Scheme);
        Assert.Null(components.Host);
        Assert.Equal("", components.Path);
        Assert.Null(components.PercentEncodedQuery);
    }

    // Oracle O-59: the host getter IDNA-decodes a host it did not percent-encode (ICU here, Windows icu.dll), which
    // keeps ASCII as written and an IP literal's brackets; URL.host drops them (SwiftURL, O-40).
    [Fact]
    [Trait("Decision", "ED-169")] // MCPCustom.swift:178-179 ["localhost", "127.0.0.1", "::1", "[::1]"].contains(host.lowercased())
    [Trait("Oracle", "O-59")]
    public void The_host_keeps_ip_literal_brackets_and_its_case()
    {
        Assert.Equal("[::1]", URLComponents.Create("http://[::1]:8080/mcp")!.Host);
        Assert.Equal("LOCALHOST", URLComponents.Create("http://LOCALHOST:3000/mcp")!.Host);
        Assert.Equal("127.0.0.1", URLComponents.Create("http://127.0.0.1:8080")!.Host);
        Assert.Equal("localhost", URLComponents.Create("http://localhost")!.Host);
    }

    [Fact]
    [Trait("Decision", "ED-169")] // MCPCustom.swift:174 guard let; URLComponents.swift:95-101, :720-725; URLParser.swift:409-418, :492-504
    public void Text_the_parser_refuses_gives_nil()
    {
        // A port that is not all digits (the scheme is not "addressbook"), and a scheme that does not start with a letter.
        Assert.Null(URLComponents.Create("http://example.com:abc/"));
        Assert.Null(URLComponents.Create("1http://example.com/"));
    }

    // ---- init(url:resolvingAgainstBaseURL: false) ----

    [Fact]
    [Trait("Decision", "ED-169")] // LinkTitles.swift:25-26; URLComponents.swift:704-715 parses url.relativeString
    public void Components_from_a_url_read_its_query_items()
    {
        var url = SwiftURL.Create("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s");
        Assert.NotNull(url);
        var components = URLComponents.Create(url, resolvingAgainstBaseURL: false);
        Assert.NotNull(components);
        Assert.Equal([("v", "dQw4w9WgXcQ"), ("t", "42s")], Pairs(components.QueryItems));
    }

    [Fact]
    [Trait("Decision", "ED-169")] // LinkTitles.swift:25; URLComponents.swift:704-715; URL(string:) encoded the space (SwiftURL, R-PATH-1)
    public void Components_from_a_url_start_from_the_encoded_string_the_url_holds()
    {
        var url = SwiftURL.Create("https://www.youtube.com/watch?v=abc def");
        Assert.NotNull(url);
        var components = URLComponents.Create(url, resolvingAgainstBaseURL: false);
        Assert.NotNull(components);
        Assert.Equal("v=abc%20def", components.PercentEncodedQuery);
        Assert.Equal([("v", "abc def")], Pairs(components.QueryItems));
    }

    // ---- queryItems read ----

    [Fact]
    [Trait("Decision", "ED-169")] // MCPOAuth.swift:526-528 first(where: name ==); URLComponents.swift:590-636
    public void Query_items_read_in_order_with_duplicates_kept()
    {
        var components = URLComponents.Create("http://127.0.0.1/callback?code=abc123&state=xyz&code=second");
        Assert.NotNull(components);
        Assert.Equal([("code", "abc123"), ("state", "xyz"), ("code", "second")], Pairs(components.QueryItems));
    }

    [Fact]
    [Trait("Decision", "ED-169")] // MCPOAuth.swift:526, :530-531 error_description; URLComponents.swift:614-617 percentDecode name and value
    public void Query_item_names_and_values_are_percent_decoded()
    {
        var components = URLComponents.Create("http://127.0.0.1/callback?error=access_denied&error_description=Access%20denied%2C%20sorry&caf%C3%A9=%C3%BC");
        Assert.NotNull(components);
        Assert.Equal(
            [("error", "access_denied"), ("error_description", "Access denied, sorry"), ("caf" + EAcute, UUmlaut)],
            Pairs(components.QueryItems));
    }

    [Fact]
    [Trait("Decision", "ED-169")] // MCPOAuth.swift:526 "http://127.0.0.1" + target; URLParser.swift:714-720 an invalid query is encoded whole
    public void A_raw_space_in_the_callback_target_reads_back_as_a_space()
    {
        var components = URLComponents.Create("http://127.0.0.1/callback?error_description=User denied it");
        Assert.NotNull(components);
        Assert.Equal("error_description=User%20denied%20it", components.PercentEncodedQuery);
        Assert.Equal([("error_description", "User denied it")], Pairs(components.QueryItems));
    }

    // Oracle O-58: the getter splits on "&", takes the first "=" as the separator, gives a nil value to an item with no
    // "=", and does not turn "+" into a space.
    [Fact]
    [Trait("Decision", "ED-169")] // MCPOAuth.swift:527-529 value(name); URLComponents.swift:600-635
    [Trait("Oracle", "O-58")]
    public void Items_without_an_equals_sign_have_a_nil_value_and_plus_stays_a_plus()
    {
        var components = URLComponents.Create("http://127.0.0.1/callback?flag&x=1&=v&k=&&v=a=b&q=a+b");
        Assert.NotNull(components);
        Assert.Equal(
            [("flag", null), ("x", "1"), ("", "v"), ("k", ""), ("", null), ("v", "a=b"), ("q", "a+b")],
            Pairs(components.QueryItems));
    }

    // Oracle O-58: an empty query reads as no items, no query as nil (URLComponents.swift:591-592).
    [Fact]
    [Trait("Decision", "ED-169")] // MCPOAuth.swift:520 target "/callback?" passes the guard, :526 ?? []; LinkTitles.swift:25-26
    [Trait("Oracle", "O-58")]
    public void An_empty_query_reads_as_no_items_and_no_query_as_nil()
    {
        var empty = URLComponents.Create("http://127.0.0.1/callback?");
        Assert.NotNull(empty);
        Assert.Equal("", empty.PercentEncodedQuery);
        Assert.NotNull(empty.QueryItems);
        Assert.Empty(empty.QueryItems);

        var none = URLComponents.Create("http://127.0.0.1/callback");
        Assert.NotNull(none);
        Assert.Null(none.PercentEncodedQuery);
        Assert.Null(none.QueryItems);
    }

    // Oracle O-58: an escape that is not UTF-8 decodes to nil: the name becomes "" and the value nil (URLComponents.swift:614-617).
    [Fact]
    [Trait("Decision", "ED-169")] // MCPOAuth.swift:527-529; URLParser.swift:229-233 percentDecode, :1082-1132
    [Trait("Oracle", "O-58")]
    public void An_escape_that_is_not_utf8_reads_as_an_empty_name_or_a_nil_value()
    {
        var components = URLComponents.Create("http://127.0.0.1/callback?%FF=1&a=%FF");
        Assert.NotNull(components);
        Assert.Equal([("", "1"), ("a", null)], Pairs(components.QueryItems));
    }

    // ---- queryItems set ----

    // Oracle O-57: the setter keeps a name or value made only of the queryItem set (the query set less "&" and "="),
    // and otherwise percent-encodes every byte outside it, "%" included, with upper-case hex (URLParser.swift:1043-1080).
    [Fact]
    [Trait("Decision", "ED-169")] // MCPOAuth.swift:597, :773, LinkTitles.swift:64-67; URLComponents.swift:638-667, URLParser.swift:1218-1266
    [Trait("Oracle", "O-57")]
    public void Setting_query_items_encodes_names_and_values_with_the_query_item_set()
    {
        var components = URLComponents.Create("https://example.com/p")! with
        {
            QueryItems = new URLQueryItem[]
            {
                new("a b", "c d"),
                new("p+q", "1+1=2"),
                new("x&y", "m&n"),
                new("path", "/a/b?c"),
                new("h#", "#frag"),
                new("caf" + EAcute, UUmlaut),
                new("pct", "100%"),
                new("esc", "%41"),
                new("br", "[x]"),
                new("ok", "a:b@c!$'()*,;~-._"),
                new("flag", null),
            },
        };
        Assert.Equal(
            "a%20b=c%20d&p+q=1+1%3D2&x%26y=m%26n&path=/a/b?c&h%23=%23frag&caf%C3%A9=%C3%BC&pct=100%25&esc=%2541&br=%5Bx%5D"
            + "&ok=a:b@c!$'()*,;~-._&flag",
            components.PercentEncodedQuery);
        Assert.Equal(
            [
                ("a b", "c d"), ("p+q", "1+1=2"), ("x&y", "m&n"), ("path", "/a/b?c"), ("h#", "#frag"), ("caf" + EAcute, UUmlaut),
                ("pct", "100%"), ("esc", "%41"), ("br", "[x]"), ("ok", "a:b@c!$'()*,;~-._"), ("flag", null),
            ],
            Pairs(components.QueryItems));
        Assert.Equal(
            "https://example.com/p?a%20b=c%20d&p+q=1+1%3D2&x%26y=m%26n&path=/a/b?c&h%23=%23frag&caf%C3%A9=%C3%BC&pct=100%25"
            + "&esc=%2541&br=%5Bx%5D&ok=a:b@c!$'()*,;~-._&flag",
            components.Url?.AbsoluteString);
    }

    // Oracle O-57: the negative control. Uri.EscapeDataString encodes "+", "/" and "?" as well; the queryItem set keeps them.
    [Fact]
    [Trait("Decision", "ED-169")] // MCPOAuth.swift:773-778 the form body; URLParser.swift:1238 "+", :1246 "/", :1256 "?" are in the queryItem set
    [Trait("Oracle", "O-57")]
    public void Uri_escape_data_string_encodes_differently_from_the_query_item_setter()
    {
        Assert.Equal("1%2B1%3D2%20%2F%3F", Uri.EscapeDataString("1+1=2 /?"));
        var components = new URLComponents { QueryItems = new URLQueryItem[] { new("v", "1+1=2 /?") } };
        Assert.Equal("v=1+1%3D2%20/?", components.PercentEncodedQuery);
    }

    [Fact]
    [Trait("Decision", "ED-169")] // URLComponents.swift:638-647 nil removes the query, an empty array sets an empty one
    public void Setting_no_items_gives_an_empty_query_and_setting_nil_removes_it()
    {
        var parsed = URLComponents.Create("https://example.com/p?a=1")!;
        var emptied = parsed with { QueryItems = ValueList<URLQueryItem>.Empty };
        Assert.Equal("", emptied.PercentEncodedQuery);
        Assert.Equal("https://example.com/p?", emptied.Url?.AbsoluteString);
        var removed = parsed with { QueryItems = null };
        Assert.Null(removed.PercentEncodedQuery);
        Assert.Equal("https://example.com/p", removed.Url?.AbsoluteString);
        Assert.Equal("a=1", parsed.PercentEncodedQuery);
    }

    // Oracle O-57 (the value "watch?v=..." keeps "?" and encodes "="); O-60 (url re-parses the recomputed string).
    [Fact]
    [Trait("Decision", "ED-169")] // LinkTitles.swift:62-68 oembedURL(for:)
    [Trait("Oracle", "O-57")]
    public void The_youtube_oembed_url_is_built_with_the_video_url_encoded_as_a_query_item_value()
    {
        const string id = "dQw4w9WgXcQ";
        var components = URLComponents.Create("https://www.youtube.com/oembed");
        Assert.NotNull(components);
        components = components with
        {
            QueryItems = new URLQueryItem[] { new("url", $"https://www.youtube.com/watch?v={id}"), new("format", "json") },
        };
        var url = components.Url;
        Assert.NotNull(url);
        Assert.Equal("https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v%3DdQw4w9WgXcQ&format=json", url.AbsoluteString);
        Assert.Equal("www.youtube.com", url.Host);
        Assert.Equal("/oembed", url.Path);
    }

    // Oracle O-57 and O-58: the endpoint's own items are read decoded and set again, so "%2B" comes back as "+".
    [Fact]
    [Trait("Decision", "ED-169")] // MCPOAuth.swift:581-598 authorizationURL
    [Trait("Oracle", "O-57")]
    public void The_oauth_authorize_url_keeps_the_endpoint_items_and_appends_the_pkce_items()
    {
        var components = URLComponents.Create("https://auth.example.com/oauth/authorize?audience=api%2Bv2");
        Assert.NotNull(components);
        var items = components.QueryItems ?? ValueList<URLQueryItem>.Empty;
        items = items.Mutating(list => list.AddRange(
        [
            new("response_type", "code"),
            new("client_id", "client-123"),
            new("redirect_uri", "http://127.0.0.1:53682/callback"),
            new("code_challenge", "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"),
            new("code_challenge_method", "S256"),
            new("state", "Xy_9-state"),
        ]));
        items = items.Appending(new URLQueryItem("resource", "https://mcp.example.com/mcp"));
        items = items.Appending(new URLQueryItem("scope", string.Join(" ", new[] { "read", "write" })));
        components = components with { QueryItems = items };
        Assert.Equal(
            "https://auth.example.com/oauth/authorize?audience=api+v2&response_type=code&client_id=client-123"
            + "&redirect_uri=http://127.0.0.1:53682/callback&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"
            + "&code_challenge_method=S256&state=Xy_9-state&resource=https://mcp.example.com/mcp&scope=read%20write",
            components.Url?.AbsoluteString);
    }

    // ---- init() with queryItems: the form body ----

    // Oracle O-57: a "+" in a field goes out unencoded, so a form decoder reads it as a space.
    [Fact]
    [Trait("Decision", "ED-169")] // MCPOAuth.swift:609-622 exchangeCode fields, :772-778 postForm
    [Trait("Oracle", "O-57")]
    public void The_form_body_is_the_percent_encoded_query_of_empty_components_with_the_fields_set()
    {
        (string, string)[] fields =
        [
            ("grant_type", "authorization_code"),
            ("code", "4/0AbC+dEf="),
            ("redirect_uri", "http://127.0.0.1:53682/callback"),
            ("client_id", "client-123"),
            ("code_verifier", "dBjftJeZ4CVP-mJ92K9wWQ_x"),
            ("client_secret", "s3cr&t +="),
            ("resource", "https://mcp.example.com/mcp"),
        ];
        var components = new URLComponents { QueryItems = fields.Select(field => new URLQueryItem(field.Item1, field.Item2)).ToArray() };
        var body = System.Text.Encoding.UTF8.GetBytes(components.PercentEncodedQuery ?? "");
        Assert.Equal(
            System.Text.Encoding.UTF8.GetBytes(
                "grant_type=authorization_code&code=4/0AbC+dEf%3D&redirect_uri=http://127.0.0.1:53682/callback&client_id=client-123"
                + "&code_verifier=dBjftJeZ4CVP-mJ92K9wWQ_x&client_secret=s3cr%26t%20+%3D&resource=https://mcp.example.com/mcp"),
            body);
    }

    [Fact]
    [Trait("Decision", "ED-169")] // MCPOAuth.swift:772, :778 ?? ""; URLComponents.swift:83-93, :697-699 every component undefined
    public void Empty_components_have_no_query_no_items_and_an_empty_url()
    {
        var components = new URLComponents();
        Assert.Null(components.PercentEncodedQuery);
        Assert.Null(components.QueryItems);
        Assert.Null(components.Scheme);
        Assert.Null(components.Host);
        Assert.Null(components.User);
        Assert.Null(components.Password);
        Assert.Null(components.Port);
        Assert.Equal("", components.Path);
        // URLComponents.swift:765-773 URL(stringOrEmpty:), URL_Swift.swift:83-93: the empty string makes a URL.
        Assert.Equal("", components.Url?.AbsoluteString);
    }

    // ---- user and password set to nil, then url ----

    [Fact]
    [Trait("Decision", "ED-169")] // AppModel+HydraMerge.swift:384-387; URLComponents.swift:160-182, :434-490
    public void Clearing_user_and_password_gives_the_url_without_credentials()
    {
        var web = SwiftURL.Create("https://oauth2:glpat-secret@gitlab.example.com/group/project");
        Assert.NotNull(web);
        var components = URLComponents.Create(web, resolvingAgainstBaseURL: false);
        Assert.NotNull(components);
        var stripped = components with { User = null, Password = null };
        Assert.Null(stripped.User);
        Assert.Null(stripped.Password);
        Assert.Equal("gitlab.example.com", stripped.Host);
        Assert.Equal("https://gitlab.example.com/group/project", stripped.Url?.AbsoluteString);
        // A struct copy: the components it came from keep their credentials (R-VAL-1).
        Assert.Equal("oauth2", components.User);
        Assert.Equal("glpat-secret", components.Password);
        Assert.Equal("https://oauth2:glpat-secret@gitlab.example.com/group/project", components.Url?.AbsoluteString);
    }

    [Fact]
    [Trait("Decision", "ED-169")] // AppModel+HydraMerge.swift:384-387 with a user and no password; URLComponents.swift:274-279, :462-470
    public void Clearing_a_user_with_no_password_keeps_the_query_and_fragment()
    {
        var web = SwiftURL.Create("https://git@gitlab.com/group/project?ref=main#readme");
        Assert.NotNull(web);
        var stripped = URLComponents.Create(web, resolvingAgainstBaseURL: false)! with { User = null, Password = null };
        Assert.Equal("https://gitlab.com/group/project?ref=main#readme", stripped.Url?.AbsoluteString);
    }

    // Oracle O-60: the recomputed string writes the port from Int(portString), so a leading zero goes.
    [Fact]
    [Trait("Decision", "ED-169")] // AppModel+HydraMerge.swift:384-387; URLComponents.swift:230-232, :471-476
    [Trait("Oracle", "O-60")]
    public void Clearing_credentials_writes_the_port_as_a_number()
    {
        var web = SwiftURL.Create("https://u:p@gitlab.example.com:0443/g/p");
        Assert.NotNull(web);
        var stripped = URLComponents.Create(web, resolvingAgainstBaseURL: false)! with { User = null, Password = null };
        Assert.Equal("https://gitlab.example.com:443/g/p", stripped.Url?.AbsoluteString);
    }

    [Fact]
    [Trait("Decision", "ED-169")] // AppModel+HydraMerge.swift:385-386 the setters encode what they are given; URLComponents.swift:160-182
    public void Setting_a_user_percent_encodes_it_with_the_user_set()
    {
        var components = URLComponents.Create("https://gitlab.com/g")! with { User = "a b@c", Password = "p:w/d" };
        Assert.Equal("a b@c", components.User);
        Assert.Equal("p:w/d", components.Password);
        Assert.Equal("https://a%20b%40c:p:w%2Fd@gitlab.com/g", components.Url?.AbsoluteString);
    }
}

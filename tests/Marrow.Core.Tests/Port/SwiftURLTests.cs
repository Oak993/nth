using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// R-PATH-1 (IMPLEMENTING.md 3.13, 4.3 "URL(string:)"): SwiftURL stands in for Foundation `URL(string:)` as built for
// macOS 26 (project.yml:5, :20, deployment target 26.0). R-PATH-1 records the leniency (invalid characters are
// percent-encoded, relative references accepted), that `url.host` keeps its case and drops IPv6 brackets, and that
// `url.path` is decoded. Conventions survey section 20 adds that `url.path` loses its trailing slash.
// Where a test pins more than those two texts say, it cites the Foundation source the rule describes:
// swift-foundation tag swift-6.2-RELEASE, Sources/FoundationEssentials/URL/ (URL.swift, URL_Swift.swift, URLParser.swift)
// and Sources/FoundationEssentials/String/String+Path.swift. On Darwin, URL.swift:611-614 routes URL to that
// implementation when `_foundation_swift_url_feature_enabled()`; whether the macOS 26 binary matches this tag
// line for line is the transfer question ED-086 leaves open, so those citations are source reading, not a capture.
// The rest of each expectation comes from what the Swift call sites rely on, cited per test; never from the port.
// Proposed surface (port-only, R-NAME-5 for the failable init):
//   SwiftURL.Create(string) -> SwiftURL?        URL(string:)
//   AbsoluteString, Scheme, Host                url.absoluteString, url.scheme, url.host
//   HostComponent(bool percentEncoded = true)   url.host(percentEncoded:), the host() the source calls
//   Path                                        url.path
//   Equals, GetHashCode                         URL is Hashable; equality is textual
// Every non-ASCII character is written as a \U escape (R-STR-9).
public sealed class SwiftURLTests
{
    private const string EAcute = "\U000000E9"; // U+00E9 LATIN SMALL LETTER E WITH ACUTE
    private const string UUmlaut = "\U000000FC"; // U+00FC LATIN SMALL LETTER U WITH DIAERESIS

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MCPOAuth.swift:201 guard URL(string: url) != nil; survey 20 "foo bar" gives foo%20bar
    public void Foo_bar_is_accepted_as_a_relative_reference_with_the_space_percent_encoded()
    {
        var url = SwiftURL.Create("foo bar");
        Assert.NotNull(url);
        Assert.Equal("foo%20bar", url.AbsoluteString);
        Assert.Null(url.Scheme);
        Assert.Null(url.Host);
        Assert.Null(url.HostComponent());
        Assert.Equal("foo bar", url.Path);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MCPOAuth.swift:201-203, :718-720; URL_Swift.swift:95-98 guard !string.isEmpty else { return nil }
    public void An_empty_string_gives_nil()
    {
        Assert.Null(SwiftURL.Create(""));
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MCPOAuth.swift:201-203 keeps a live nil branch; URLParser.swift:493-504, :859-869
    public void A_port_that_is_not_all_digits_gives_nil()
    {
        // parse(buffer:) validates the port of an authority: only ASCII digits, unless the scheme is "addressbook".
        // A failed buffer parse returns nil before any encoding (URLParser.swift:635-637).
        Assert.Null(SwiftURL.Create("http://example.com:abc/"));
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MCPOAuth.swift:201-203 guard URL(string: url) != nil keeps a live nil branch
    public void Text_before_the_first_colon_that_is_not_a_valid_scheme_gives_nil()
    {
        // Everything before the first ":" is the scheme unless "/", "?", "#", "[", "]" or "@" comes first
        // (URLParser.swift:787-816). "my server" holds a space, which schemeAllowed (ALPHA, DIGIT and "+-.",
        // URLParser.swift:1209) does not contain, so validate(schemeBuffer:) fails (:409-416) and parse returns nil
        // (:821-824). Nothing falls back to a percent-encoded relative path such as "my%20server:8080".
        Assert.Null(SwiftURL.Create("my server:8080"));
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MCPCustom.swift:85, MCPSettingsPage.swift:1063 host() ?? url; MCPOAuth.swift:201
    public void An_at_sign_before_the_first_colon_makes_a_relative_reference_with_no_scheme_and_no_host()
    {
        // "@" before any ":" is the authority heuristic of URLParser.swift:811-816: no scheme, and with no leading "//"
        // no authority either (:834-838), so the whole text is the path. ":" and "@" are pchar (:1206, :1215), so the
        // path is valid and the string is kept as written (URLParser.swift:647-649).
        var url = SwiftURL.Create("user@example.com:22");
        Assert.NotNull(url);
        Assert.Null(url.Scheme);
        Assert.Null(url.Host);
        Assert.Null(url.HostComponent());
        Assert.Equal("user@example.com:22", url.AbsoluteString);
        Assert.Equal("user@example.com:22", url.Path);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MCPCustom.swift:85 URL(string: url)?.host() ?? url
    public void Free_text_parses_without_a_host_so_the_caller_falls_back_to_the_raw_text()
    {
        var url = SwiftURL.Create("not a url");
        Assert.NotNull(url);
        Assert.Equal("not%20a%20url", url.AbsoluteString);
        Assert.Null(url.HostComponent());
        Assert.Null(url.Host);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MCPCustom.swift:85, MCPSettingsPage.swift:1063 host() ?? url; MCPOAuth.swift:201
    public void Localhost_colon_port_reads_as_a_scheme_and_a_path_with_no_host()
    {
        // RFC 3986: the text before the first ":" is the scheme (URLParser.swift:786-801, :819-824); no "//" follows,
        // so there is no authority and "8080" is the path (URLParser.swift:873-889). host() is nil without an
        // authority or a base (URL_Swift.swift:449), so the caller shows the raw text.
        var url = SwiftURL.Create("localhost:8080");
        Assert.NotNull(url);
        Assert.Equal("localhost", url.Scheme);
        Assert.Null(url.Host);
        Assert.Null(url.HostComponent());
        Assert.Equal("8080", url.Path);
        Assert.Equal("localhost:8080", url.AbsoluteString);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MCPCustom.swift:85, MCPSettingsPage.swift:1063 host() ?? url; MCPOAuth.swift:201
    public void A_host_and_path_without_a_scheme_is_a_relative_reference_with_no_host()
    {
        // A "/" before any ":" means no scheme (URLParser.swift:806-816); no leading "//" means no authority, so the
        // whole text is the path. Nothing guesses a scheme.
        var url = SwiftURL.Create("example.com/mcp");
        Assert.NotNull(url);
        Assert.Null(url.Scheme);
        Assert.Null(url.Host);
        Assert.Null(url.HostComponent());
        Assert.Equal("example.com/mcp", url.Path);
        Assert.Equal("example.com/mcp", url.AbsoluteString);
    }

    [Theory]
    [Trait("Rule", "R-PATH-1")] // UpdateChecker.swift:68, :70; Provider.swift:83-96; ZaiAPI.swift:11; MarkdownView.swift:743
    [InlineData("https://gitlab.com/droppyformac1/droppy-code")]
    [InlineData("https://gitlab.com/api/v4/projects/123/releases?per_page=20")]
    [InlineData("https://claude.com/product/claude-code")]
    [InlineData("https://dev.meta.ai/")]
    [InlineData("https://api.z.ai/api/biz/customer-package-reset/list?targetType=PERSONAL")]
    [InlineData("https://example.com/a%20b#frag")]
    public void A_valid_URL_string_is_kept_byte_for_byte_as_the_absolute_string(string text)
    {
        var url = SwiftURL.Create(text);
        Assert.NotNull(url);
        Assert.Equal(text, url.AbsoluteString);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // R-PATH-1 leniency: invalid characters are percent-encoded, not refused
    public void An_invalid_character_in_an_absolute_URL_is_percent_encoded_instead_of_failing()
    {
        var url = SwiftURL.Create("https://example.com/a b");
        Assert.NotNull(url);
        Assert.Equal("https://example.com/a%20b", url.AbsoluteString);
        Assert.Equal("https", url.Scheme);
        Assert.Equal("example.com", url.Host);
        Assert.Equal("/a b", url.Path);

        var query = SwiftURL.Create("https://example.com/?q=a b");
        Assert.NotNull(query);
        Assert.Equal("https://example.com/?q=a%20b", query.AbsoluteString);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // R-PATH-1 leniency; URLParser.swift:708-713 percent-encodes the path, hex upper case (:985-986)
    public void A_non_ASCII_path_is_UTF8_percent_encoded_and_the_path_reads_back_decoded()
    {
        var url = SwiftURL.Create("https://example.com/caf" + EAcute);
        Assert.NotNull(url);
        Assert.Equal("https://example.com/caf%C3%A9", url.AbsoluteString);
        Assert.Equal("/caf" + EAcute, url.Path);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // R-PATH-1 leniency; MarkdownView.swift:677 decodes url.path once more
    public void An_invalid_path_is_encoded_whole_so_an_existing_escape_becomes_percent_25()
    {
        // URL(string:) takes the non-compatibility parse (URL_Swift.swift:75-76, :83-87). An invalid path is rebuilt
        // with percentEncode(path, component: .path) (URLParser.swift:709), whose skipAlreadyEncoded defaults to false
        // (:204, :326-335). "%" is not in pathAllowed (:1205, :1215), so the escape-emitting branch of
        // addingPercentEncoding (:1073-1074) turns "%" into "%25". URLParser.swift:1336-1337 contrasts this with
        // CFURL, which left an existing escape alone.
        var mixed = SwiftURL.Create("https://example.com/a%20b c");
        Assert.NotNull(mixed);
        Assert.Equal("https://example.com/a%2520b%20c", mixed.AbsoluteString);
        Assert.Equal("/a%20b c", mixed.Path);

        // A "%" without two hex digits after it fails validation (URLParser.swift:385-405), so the path is encoded.
        var bare = SwiftURL.Create("https://example.com/100%");
        Assert.NotNull(bare);
        Assert.Equal("https://example.com/100%25", bare.AbsoluteString);
        Assert.Equal("/100%", bare.Path);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MarkdownView.swift:677 url.path.removingPercentEncoding ?? url.path, :680 host alone
    public void A_path_whose_escapes_are_not_UTF8_decodes_to_an_empty_path()
    {
        // "%E9" is valid percent-encoding, so the string is kept as written. path is
        // Parser.percentDecode(path)?._droppingTrailingSlashes ?? "" (URL_Swift.swift:527-530), and the decoder ends in
        // String(bytes:encoding: .utf8) (URLParser.swift:1130), which is nil for the lone byte 0xE9. No U+FFFD, and the
        // escape is not kept: the path is empty, so prettyTitle shows the host alone.
        var url = SwiftURL.Create("https://example.com/caf%E9");
        Assert.NotNull(url);
        Assert.Equal("https://example.com/caf%E9", url.AbsoluteString);
        Assert.Equal("", url.Path);
    }

    [Theory]
    [Trait("Rule", "R-PATH-1")] // MCPProbe.swift:362-363 origin from url.scheme and url.host; MarkdownView.swift:673 lowercases itself
    [InlineData("https://GitLab.Example.COM/api/v4/mcp", "GitLab.Example.COM")]
    [InlineData("https://www.Example.com/watch?v=x", "www.Example.com")]
    [InlineData("https://GitLab.Example.COM:8443/api/v4/mcp", "GitLab.Example.COM")]
    [InlineData("http://user@Host.Example:80/x", "Host.Example")]
    public void Host_keeps_its_case_and_leaves_out_the_port_and_user(string text, string host)
    {
        var url = SwiftURL.Create(text);
        Assert.NotNull(url);
        Assert.Equal(host, url.Host);
        Assert.Equal(host, url.HostComponent());
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // Hydra.swift:448, MCPCustom.swift:85, MCPSettingsPage.swift:1063 call host(); URL_Swift.swift:444-477
    public void Host_component_stays_percent_encoded_while_host_is_decoded()
    {
        // URL.host is host(percentEncoded: false) (URL_Swift.swift:444-446); host() defaults percentEncoded to true
        // (URL.swift:886). A host already holding "%" sets didPercentEncodeHost (URLParser.swift:956) and is valid as
        // written, so host() returns it unchanged (URL_Swift.swift:461-463) and host decodes it (URL_Swift.swift:477).
        var url = SwiftURL.Create("https://a%2Db.example/");
        Assert.NotNull(url);
        Assert.Equal("a%2Db.example", url.HostComponent());
        Assert.Equal("a%2Db.example", url.HostComponent(percentEncoded: true));
        Assert.Equal("a-b.example", url.HostComponent(percentEncoded: false));
        Assert.Equal("a-b.example", url.Host);
        Assert.Equal("https://a%2Db.example/", url.AbsoluteString);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MarkdownView.swift:673 url.host?.lowercased(); MCPCustom.swift:85, MCPSettingsPage.swift:1063 host() ?? url
    public void A_non_ASCII_host_is_IDNA_encoded_and_host_component_percent_encodes_the_decoded_name()
    {
        // The host is invalid, not an IP literal, and "https" is not on schemesToPercentEncodeHost
        // (URLParser.swift:235-253, :264-277), so it is IDNA-encoded (:695-697). url.host then returns the IDNA text
        // ("Return IDNA-encoded host", URL_Swift.swift:473-475); host() IDNA-decodes and then percent-encodes
        // (URL_Swift.swift:465-469), UTF-8 bytes in upper-case hex. The IDNA hook exists because the Darwin framework
        // build returns UIDNAHookICU (URLParser.swift:190-193); without it the host would be percent-encoded (:265-268).
        var url = SwiftURL.Create("https://b" + UUmlaut + "cher.de/");
        Assert.NotNull(url);
        Assert.Equal("https://xn--bcher-kva.de/", url.AbsoluteString);
        Assert.Equal("xn--bcher-kva.de", url.Host);
        Assert.Equal("b%C3%BCcher.de", url.HostComponent());
        Assert.Equal("b%C3%BCcher.de", url.HostComponent(percentEncoded: true));
        Assert.Equal("xn--bcher-kva.de", url.HostComponent(percentEncoded: false));
    }

    [Theory]
    [Trait("Rule", "R-PATH-1")] // CopilotSession.swift:888; MCPProbe.swift:264 URL(string: upstream)?.host; MCPCustom.swift:85, MCPSettingsPage.swift:1063 host()
    [InlineData("http://[::1]:8080/x", "::1")]
    [InlineData("http://[::1]/", "::1")]
    [InlineData("https://[2001:db8::1]/mcp", "2001:db8::1")]
    [InlineData("https://[2001:DB8::A]:443/", "2001:DB8::A")]
    public void Host_and_host_component_of_an_IPv6_literal_drop_the_brackets_and_keep_the_case(string text, string host)
    {
        // Both forms go through host(percentEncoded:), which strips the brackets of an IP literal after choosing the
        // encoded or decoded text (URL_Swift.swift:485-488); a valid literal is kept as written (URLParser.swift:646-649).
        var url = SwiftURL.Create(text);
        Assert.NotNull(url);
        Assert.Equal(host, url.Host);
        Assert.Equal(host, url.HostComponent());
        Assert.Equal(host, url.HostComponent(percentEncoded: false));
        Assert.Equal(text, url.AbsoluteString);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MCPProbe.swift:363 "\(url.scheme ?? "https")://\(host)"; URL_Swift.swift:385-388
    public void Scheme_is_returned_as_written()
    {
        // scheme is String(scheme) over the parsed range (URL_Swift.swift:385-388); the parser only validates it
        // (URLParser.swift:409-418, :819-824) and never lowercases it.
        Assert.Equal("https", SwiftURL.Create("https://gitlab.com/api/v4/mcp")?.Scheme);
        Assert.Equal("HTTPS", SwiftURL.Create("HTTPS://gitlab.com/api/v4/mcp")?.Scheme);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // Hydra.swift:445-451 HydraMentionTarget.url and threadID(in:)
    public void A_hydra_mention_link_carries_its_scheme_and_the_uppercase_UUID_as_host()
    {
        var url = SwiftURL.Create("droppycode-head://0123ABCD-89AB-CDEF-0123-456789ABCDEF");
        Assert.NotNull(url);
        Assert.Equal("droppycode-head", url.Scheme);
        Assert.Equal("0123ABCD-89AB-CDEF-0123-456789ABCDEF", url.HostComponent());
        Assert.Equal("0123ABCD-89AB-CDEF-0123-456789ABCDEF", url.Host);
        Assert.Equal("", url.Path);
        Assert.Equal("droppycode-head://0123ABCD-89AB-CDEF-0123-456789ABCDEF", url.AbsoluteString);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MarkdownView.swift:725-729 hostless URLs (mailto:, custom schemes) fall back to absoluteString
    public void A_mailto_link_has_a_scheme_but_no_host()
    {
        var url = SwiftURL.Create("mailto:someone@example.com");
        Assert.NotNull(url);
        Assert.Equal("mailto", url.Scheme);
        Assert.Null(url.Host);
        Assert.Null(url.HostComponent());
        Assert.Equal("mailto:someone@example.com", url.AbsoluteString);
    }

    [Theory]
    [Trait("Rule", "R-PATH-1")] // MarkdownView.swift:677, :706 and LinkTitles.swift:16 split url.path on "/"
    [InlineData("https://example.com/docs/My%20File.pdf", "/docs/My File.pdf")]
    [InlineData("https://example.com/caf%C3%A9/menu", "/caf" + EAcute + "/menu")]
    [InlineData("https://gitlab.com/group/project/-/merge_requests/3190", "/group/project/-/merge_requests/3190")]
    [InlineData("https://github.com/owner/repo/pull/123?tab=files#top", "/owner/repo/pull/123")]
    public void Path_is_percent_decoded_and_excludes_query_and_fragment(string text, string path)
    {
        var url = SwiftURL.Create(text);
        Assert.NotNull(url);
        Assert.Equal(path, url.Path);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MarkdownView.swift:677 (url.path.removingPercentEncoding ?? url.path)
    public void Path_decodes_exactly_once_so_the_markdown_title_can_decode_a_second_time()
    {
        // "%2541" is "%25" then "41": one decode gives "%41"; the caller's removingPercentEncoding then gives "A".
        var url = SwiftURL.Create("https://example.com/%2541");
        Assert.NotNull(url);
        Assert.Equal("/%41", url.Path);
        Assert.Equal("https://example.com/%2541", url.AbsoluteString);
    }

    [Theory]
    [Trait("Rule", "R-PATH-1")] // AttachmentPreview.swift:66-67 absolute = URL(string: trimmed)?.path ?? trimmed; survey 20
    [InlineData("file:///Users/me/shot%201.png", "/Users/me/shot 1.png")]
    [InlineData("file:///Users/me/a%2Fb.png", "/Users/me/a%2Fb.png")]
    [InlineData("file:///tmp/a%00b", "/tmp/a%00b")]
    [InlineData("file:///Users/me/dir%2F", "/Users/me/dir%2F")]
    [InlineData("file:///Users/me/dir//", "/Users/me/dir")]
    public void A_file_URL_path_drops_trailing_slashes_then_decodes_all_but_slash_and_NUL(string text, string path)
    {
        // For a file URL, path is the file-system path (URL_Swift.swift:525): all trailing slashes dropped first
        // (_droppingTrailingSlashes, not the compatibility singular, :609-614), then decoded with "%2F" and "%00" left
        // encoded (:574-578, :616). Only the POSIX shape the Swift sees is pinned here; the Windows shape of a
        // file:///C:/... candidate has no engineering decision yet and is not chosen by this test.
        var url = SwiftURL.Create(text);
        Assert.NotNull(url);
        Assert.Equal("file", url.Scheme);
        Assert.Equal(path, url.Path);
        Assert.Equal(text, url.AbsoluteString);
    }

    [Theory]
    [Trait("Rule", "R-PATH-1")] // MCPProbe.swift:355-357 url.path.lowercased().hasSuffix("/api/v4/mcp"); MCPOAuth.swift:218; survey 20
    [InlineData("https://gitlab.example.com/api/v4/mcp/", "/api/v4/mcp")]
    [InlineData("https://gitlab.example.com/API/V4/MCP", "/API/V4/MCP")]
    [InlineData("https://example.com/", "/")]
    [InlineData("https://example.com", "")]
    [InlineData("https://example.com?x=1", "")]
    [InlineData("https://example.com/api/v4/mcp//", "/api/v4/mcp")]
    [InlineData("https://example.com//", "/")]
    [InlineData("https://example.com/docs%2F", "/docs")]
    public void Path_drops_a_trailing_slash_keeps_a_lone_root_and_is_empty_without_one(string text, string path)
    {
        // Survey 20: url.path "loses its trailing slash". For a non-file URL the path is decoded first and then every
        // trailing slash is dropped (URL_Swift.swift:528, _droppingTrailingSlashes at String+Path.swift:420-428); an
        // all-slash path becomes "/". The singular _droppingTrailingSlash (String+Path.swift:413-418) belongs only to
        // compatibility mode (URL_Swift.swift:610-611), so "/api/v4/mcp//" loses both slashes, and "%2F" is decoded
        // before the drop, so "/docs%2F" gives "/docs".
        var url = SwiftURL.Create(text);
        Assert.NotNull(url);
        Assert.Equal(path, url.Path);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // MarkdownView.swift:1171, ProseRunView.swift:37 target.url == url; URL.swift:1371-1378
    public void Equality_is_textual_so_host_case_and_a_trailing_slash_make_different_URLs()
    {
        // URL hashes and compares its relativeString (and base URL, none here), so equality is textual.
        var a = SwiftURL.Create("https://example.com/x");
        var b = SwiftURL.Create("https://example.com/x");
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, SwiftURL.Create("https://Example.com/x"));
        Assert.NotEqual(a, SwiftURL.Create("https://example.com/x/"));
        var set = new HashSet<SwiftURL> { a, b };
        Assert.Single(set);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // Hydra.swift:445 URL(string: "\(Self.scheme)://\(threadID.uuidString)"); MarkdownView.swift:1171
    public void Two_mention_links_built_from_the_same_thread_are_equal()
    {
        // Built the way Hydra.swift:445 builds them: scheme "droppycode-head" (Hydra.swift:444), then "://", then the
        // upper-case uuidString; the hover code compares two such values with ==.
        const string uuid = "0123ABCD-89AB-CDEF-0123-456789ABCDEF";
        var first = SwiftURL.Create("droppycode-head" + "://" + uuid);
        var second = SwiftURL.Create("droppycode-head" + "://" + uuid);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first, second);
        Assert.True(first.Equals(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, SwiftURL.Create("droppycode-head://" + "11111111-2222-3333-4444-555555555555"));
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // negative control (R-TEST-4): why a call site never uses System.Uri
    public void Dotnet_Uri_lowercases_the_host_keeps_IPv6_brackets_and_leaves_the_path_encoded()
    {
        Assert.Equal("gitlab.example.com", new Uri("https://GitLab.Example.COM/api/v4/mcp").Host);
        Assert.Equal("[::1]", new Uri("http://[::1]:8080/x").Host);
        Assert.Equal("/a%20b/", new Uri("https://example.com/a%20b/").AbsolutePath);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")] // negative control (R-TEST-4): Uri.TryCreate draws the nil line elsewhere
    public void Dotnet_Uri_refuses_foo_bar_as_absolute_and_accepts_an_empty_string()
    {
        Assert.False(Uri.TryCreate("foo bar", UriKind.Absolute, out _));
        Assert.True(Uri.TryCreate("", UriKind.RelativeOrAbsolute, out _));
    }
}

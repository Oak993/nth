using System.Text;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Markdown;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/LinkTitles.swift (B2 1fbf81e), :1-113.
// Every expected value below is transcribed from that file, never from the port:
//   :13-31 videoID(for:): host lowercased, a leading "www." dropped; youtu.be takes the first path segment;
//          youtube.com, m.youtube.com, music.youtube.com and youtube-nocookie.com take the second path segment
//          when the first is "shorts", "embed", "live" or "v"; any other host is nil.
//   :35-38 cached(for:): nil for a non-video url, else Store.shared.title(id).
//   :71-77 checked(_:): at least 8 characters, every character a letter, a digit, "-" or "_".
//   :79-111 Store: title(_:) reads titles[id]; claim(_:) is claimed.insert(id).inserted (true once, then
//          false); finish(_:title:) stores a title (discarding every stored title at 400 entries) and always
//          inserts into claimed, so a losing or failed claim is never retried (upstream defect 48/49,
//          porting/upstream_defects.md entries 48-49).
//   :25-27 videoID(for:)'s "?v=<id>" fallback: the first "v" query item's value, still passed through checked(_:).
//   :43-56 title(forVideo:): a known title returns immediately; a losing claim returns nil without a request; a
//          200 with a decodable, non-empty-after-trim title is cached; anything else is remembered as no title.
//   :62-69 oembedURL(for:): https://www.youtube.com/oembed with query items url=<canonical watch URL> and
//          format=json.
// The oEmbed fetch is exercised against LoopbackHttpServer (R-TEST-10), never the real network: TitleForVideo
// takes a PORT-ONLY trailing `oembedUrl` override (MetaAPI.cs precedent) that every production call site leaves
// null. The Store and OembedURL are Swift `private`, reached here only through LinkTitlesStoreAccess (Fixtures),
// by reflection.
public sealed class LinkTitlesTests
{
    private static int idCounter;

    // A fresh, Checked()-valid id (>= 8 chars, letters/digits/hyphens only) for each test, so tests sharing the
    // process-lifetime Store (:82-83 static let shared) never collide with each other's entries.
    private static string UniqueId(string tag) => $"{tag}{System.Threading.Interlocked.Increment(ref idCounter):D6}";

    private static SwiftURL Url(string text) =>
        SwiftURL.Create(text) ?? throw new InvalidOperationException($"test setup: could not parse '{text}'");

    // --- videoID(for:) (:13-31) ---

    [Theory]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/live/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/v/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube-nocookie.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/shorts/dQw4w9WgXcQ/extra", "dQw4w9WgXcQ")]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:13-31
    public void A_video_id_is_read_from_every_recognised_host_and_path_shape(string link, string expected)
    {
        Assert.Equal(expected, LinkTitles.VideoID(Url(link)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:14-15
    public void The_host_is_lowercased_and_exactly_one_leading_www_dot_is_dropped()
    {
        Assert.Equal("dQw4w9WgXcQ", LinkTitles.VideoID(Url("https://WWW.YouTube.COM/embed/dQw4w9WgXcQ")));
        Assert.Equal("dQw4w9WgXcQ", LinkTitles.VideoID(Url("https://WWW.YOUTU.BE/dQw4w9WgXcQ")));
        // "www.www.youtu.be": only the first "www." is dropped (:15 dropFirst(4), not a repeated strip), leaving
        // a host this switch does not recognise (:29).
        Assert.Null(LinkTitles.VideoID(Url("https://www.www.youtu.be/dQw4w9WgXcQ")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:29 default: return nil
    public void An_unrecognised_host_carries_no_video_id()
    {
        Assert.Null(LinkTitles.VideoID(Url("https://example.com/watch?v=dQw4w9WgXcQ")));
        Assert.Null(LinkTitles.VideoID(Url("https://vimeo.com/dQw4w9WgXcQ")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:19 guard let id = segments.first else { return nil }
    public void A_bare_youtu_be_link_with_no_path_carries_no_video_id()
    {
        Assert.Null(LinkTitles.VideoID(Url("https://youtu.be/")));
        Assert.Null(LinkTitles.VideoID(Url("https://youtu.be")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:22 segments.count >= 2 && [...].contains(segments[0])
    public void A_youtube_com_path_whose_first_segment_is_not_a_recognised_prefix_falls_back_to_the_query()
    {
        // No "v" query item on either link, so the fallback (:25-27) also yields nothing.
        Assert.Null(LinkTitles.VideoID(Url("https://youtube.com/playlist?list=PL1234567890abcdef")));
        Assert.Null(LinkTitles.VideoID(Url("https://youtube.com/results?search_query=cats")));
    }

    // --- videoID(for:)'s "?v=<id>" fallback (:25-27) ---

    [Theory]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?foo=bar&v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube-nocookie.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:25-27
    public void A_v_query_item_supplies_the_id_when_the_path_has_no_recognised_prefix(string link, string expected)
    {
        Assert.Equal(expected, LinkTitles.VideoID(Url(link)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:26 query?.first(where: { $0.name == "v" })
    public void The_first_v_query_item_wins_when_there_is_more_than_one()
    {
        Assert.Equal("dQw4w9WgXcQ", LinkTitles.VideoID(Url("https://youtube.com/watch?v=dQw4w9WgXcQ&v=zzzzzzzz")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:26 guard let value = query?.first(where:)?.value else return nil
    public void A_youtube_com_link_with_no_v_query_item_carries_no_video_id()
    {
        Assert.Null(LinkTitles.VideoID(Url("https://youtube.com/watch?foo=bar")));
        Assert.Null(LinkTitles.VideoID(Url("https://youtube.com/watch")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:27 checked(value) still applies to the fallback
    public void The_v_query_items_value_is_still_checked_for_length_and_characters()
    {
        Assert.Null(LinkTitles.VideoID(Url("https://youtube.com/watch?v=short")));
        Assert.Null(LinkTitles.VideoID(Url("https://youtube.com/watch?v=dQw4w9Wg.cQ")));
    }

    // --- checked(_:), read through videoID(for:) (:71-77) ---

    [Theory]
    [InlineData("https://youtu.be/dQw4w9W")]
    [InlineData("https://youtu.be/x")]
    [InlineData("https://youtu.be/")]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:74 value.count >= 8
    public void A_candidate_shorter_than_eight_characters_is_rejected(string link)
    {
        Assert.Null(LinkTitles.VideoID(Url(link)));
    }

    [Theory]
    [InlineData("https://youtu.be/dQw4w9Wg.cQ")]
    [InlineData("https://youtu.be/dQw4w9Wg%cQ")]
    [InlineData("https://youtu.be/dQw4w9Wg+cQ")]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:75 allSatisfy(isLetter || isNumber || "-" || "_")
    public void A_candidate_with_a_disallowed_character_is_rejected(string link)
    {
        Assert.Null(LinkTitles.VideoID(Url(link)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:75 $0 == "-" || $0 == "_"
    public void Hyphens_and_underscores_are_allowed_in_a_video_id()
    {
        Assert.Equal("dQw4-9_gXcQ", LinkTitles.VideoID(Url("https://youtu.be/dQw4-9_gXcQ")));
    }

    // --- cached(for:) (:35-38) ---

    [Fact]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:36 guard let id = videoID(for: url) else { return nil }
    public void Cached_returns_null_for_a_url_that_is_not_a_video_link()
    {
        Assert.Null(LinkTitles.Cached(Url("https://example.com/")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-134")] // UI/Markdown/LinkTitles.swift:37 Store.shared.title(id)
    public void Cached_reads_whatever_the_store_holds_for_that_video_ids_url()
    {
        var id = UniqueId("cachedvid");
        var url = Url($"https://youtu.be/{id}");
        Assert.Null(LinkTitles.Cached(url));

        LinkTitlesStoreAccess.Claim(id);
        LinkTitlesStoreAccess.Finish(id, "A cached title");

        Assert.Equal("A cached title", LinkTitles.Cached(url));
    }

    // --- the Store (:79-111) ---

    [Fact]
    [Trait("Req", "PR-THEME-135")] // UI/Markdown/LinkTitles.swift:96-99 claimed.insert(id).inserted
    public void Claiming_an_id_returns_true_once_and_false_on_every_later_call()
    {
        var id = UniqueId("claimonce");

        Assert.True(LinkTitlesStoreAccess.Claim(id));
        Assert.False(LinkTitlesStoreAccess.Claim(id));
        Assert.False(LinkTitlesStoreAccess.Claim(id));
    }

    [Fact]
    [Trait("Req", "PR-THEME-135")] // UI/Markdown/LinkTitles.swift:92-94 titles[id]
    public void Title_reads_nil_until_finish_stores_one()
    {
        var id = UniqueId("titlenil");

        Assert.Null(LinkTitlesStoreAccess.Title(id));
        LinkTitlesStoreAccess.Finish(id, "The stored title");
        Assert.Equal("The stored title", LinkTitlesStoreAccess.Title(id));
    }

    [Fact]
    [Trait("Req", "PR-THEME-135")] // UI/Markdown/LinkTitles.swift:104,108 finish(_:title: nil) still inserts into claimed
    public void Finishing_with_no_title_stores_nothing_but_still_marks_the_id_claimed_so_it_is_never_retried()
    {
        var id = UniqueId("nofetch");

        Assert.True(LinkTitlesStoreAccess.Claim(id));
        LinkTitlesStoreAccess.Finish(id, null);

        Assert.Null(LinkTitlesStoreAccess.Title(id));
        // upstream defect 48 (porting/upstream_defects.md:109): a failed fetch is never retried, because
        // finish() claims even when title is nil.
        Assert.False(LinkTitlesStoreAccess.Claim(id));
    }

    [Fact]
    [Trait("Req", "PR-THEME-136")] // UI/Markdown/LinkTitles.swift:105-106,108
    public void Every_stored_title_is_discarded_in_one_go_at_the_four_hundredth_entry_but_claimed_is_kept()
    {
        const int Threshold = 400;
        var (startTitles, _) = LinkTitlesStoreAccess.RawCounts();

        var survivorId = UniqueId("flushsurvivor");
        LinkTitlesStoreAccess.Claim(survivorId);
        LinkTitlesStoreAccess.Finish(survivorId, "Stored just under the threshold");

        var toFill = Math.Max(0, Threshold - startTitles - 1);
        for (var i = 0; i < toFill; i++)
        {
            var padId = UniqueId($"flushpad{i:D5}");
            LinkTitlesStoreAccess.Claim(padId);
            LinkTitlesStoreAccess.Finish(padId, $"Pad title {i}");
        }

        var (atThreshold, _) = LinkTitlesStoreAccess.RawCounts();
        Assert.Equal(Threshold, atThreshold); // :105 count is not yet >= 400 on the insert that reaches it
        Assert.Equal("Stored just under the threshold", LinkTitlesStoreAccess.Title(survivorId));

        var flushingId = UniqueId("flushtrigger");
        LinkTitlesStoreAccess.Claim(flushingId);
        LinkTitlesStoreAccess.Finish(flushingId, "Triggers the wholesale flush");

        var (afterFlush, _) = LinkTitlesStoreAccess.RawCounts();
        Assert.Equal(1, afterFlush); // :105 removeAll(keepingCapacity: true), then :106 stores only the new one
        Assert.Null(LinkTitlesStoreAccess.Title(survivorId)); // discarded wholesale, not evicted one at a time
        Assert.Equal("Triggers the wholesale flush", LinkTitlesStoreAccess.Title(flushingId));

        // upstream defect 48/49 (porting/upstream_defects.md:109,111): claimed is never cleared by the flush,
        // so a video whose title was just discarded can never be claimed, and so never re-fetched, again.
        Assert.False(LinkTitlesStoreAccess.Claim(survivorId));
    }

    // --- oembedURL(for:) (:62-69) ---

    [Fact]
    [Trait("Req", "PR-THEME-135")] // UI/Markdown/LinkTitles.swift:62-69
    public void The_oembed_request_url_carries_the_canonical_watch_url_and_format_json()
    {
        var id = UniqueId("oembedurl");
        var url = LinkTitlesStoreAccess.OembedUrl(id);
        Assert.NotNull(url);

        // Read the produced URL back through URLComponents (rather than asserting its exact percent-encoded
        // bytes, an open oracle item, O-57) to check the decoded shape :63-67 actually builds.
        var components = URLComponents.Create(url!.AbsoluteUri);
        Assert.NotNull(components);
        Assert.Equal("https", components!.Scheme);
        Assert.Equal("www.youtube.com", components.Host);
        Assert.Equal("/oembed", components.Path);

        var items = components.QueryItems;
        Assert.NotNull(items);
        Assert.Equal(2, items!.Count);
        Assert.Equal("url", items[0].Name);
        Assert.Equal($"https://www.youtube.com/watch?v={id}", items[0].Value);
        Assert.Equal("format", items[1].Name);
        Assert.Equal("json", items[1].Value);
    }

    // --- title(forVideo:) (:43-56), against LoopbackHttpServer (R-TEST-10) ---

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    [Fact]
    [Trait("Req", "PR-THEME-135")] // UI/Markdown/LinkTitles.swift:44 if let known = Store.shared.title(id) { return known }
    public async Task A_known_title_is_returned_without_a_request_or_reclaiming()
    {
        var id = UniqueId("known");
        LinkTitlesStoreAccess.Claim(id);
        LinkTitlesStoreAccess.Finish(id, "Known already");

        var result = await LinkTitles.TitleForVideo(id, new Uri("http://127.0.0.1:1/unreachable"));

        Assert.Equal("Known already", result);
    }

    [Fact]
    [Trait("Req", "PR-THEME-135")] // UI/Markdown/LinkTitles.swift:45 guard Store.shared.claim(id) else { return nil }
    public async Task A_losing_claimant_returns_nil_without_a_request()
    {
        var id = UniqueId("loseclaim");
        Assert.True(LinkTitlesStoreAccess.Claim(id)); // some other caller already won the claim

        var result = await LinkTitles.TitleForVideo(id, new Uri("http://127.0.0.1:1/unreachable"));

        Assert.Null(result);
    }

    [Fact]
    [Trait("Req", "PR-THEME-135")] // UI/Markdown/LinkTitles.swift:47-54
    public async Task A_200_response_with_a_decodable_title_is_trimmed_and_cached()
    {
        await using var server = new LoopbackHttpServer();
        var id = UniqueId("fetchok");

        var pending = LinkTitles.TitleForVideo(id, server.Url("/oembed"));
        var exchange = await server.NextExchange();
        Assert.Equal("GET", exchange.Method);
        await exchange.Respond(200, "OK", Ascii("{\"title\":\"  The Title  \"}"));

        var title = await pending.WaitAsync(LoopbackHttpServer.Guard);

        Assert.Equal("The Title", title); // :53 payload.title.trimmed
        Assert.Equal("The Title", LinkTitlesStoreAccess.Title(id)); // :54
        Assert.False(LinkTitlesStoreAccess.Claim(id)); // already claimed
    }

    [Fact]
    [Trait("Req", "PR-THEME-135")] // UI/Markdown/LinkTitles.swift:48,50-51
    public async Task A_non_200_response_is_remembered_as_no_title_and_never_retried()
    {
        await using var server = new LoopbackHttpServer();
        var id = UniqueId("fetch404");

        var pending = LinkTitles.TitleForVideo(id, server.Url("/oembed"));
        var exchange = await server.NextExchange();
        await exchange.Respond(404, "Not Found", []);

        var title = await pending.WaitAsync(LoopbackHttpServer.Guard);

        Assert.Null(title);
        Assert.Null(LinkTitlesStoreAccess.Title(id));
        Assert.False(LinkTitlesStoreAccess.Claim(id)); // upstream defect 48: never retried
    }

    [Fact]
    [Trait("Req", "PR-THEME-135")] // UI/Markdown/LinkTitles.swift:49-51 try? JSONDecoder().decode(OEmbed.self, from: data)
    public async Task An_undecodable_body_is_remembered_as_no_title()
    {
        await using var server = new LoopbackHttpServer();
        var id = UniqueId("fetchbad");

        var pending = LinkTitles.TitleForVideo(id, server.Url("/oembed"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("not json"));

        var title = await pending.WaitAsync(LoopbackHttpServer.Guard);

        Assert.Null(title);
        Assert.Null(LinkTitlesStoreAccess.Title(id));
    }

    [Fact]
    [Trait("Req", "PR-THEME-135")] // UI/Markdown/LinkTitles.swift:49 a body decoding without a "title" field
    public async Task A_body_with_no_title_field_is_remembered_as_no_title()
    {
        await using var server = new LoopbackHttpServer();
        var id = UniqueId("fetchmiss");

        var pending = LinkTitles.TitleForVideo(id, server.Url("/oembed"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("{\"other\":\"x\"}"));

        var title = await pending.WaitAsync(LoopbackHttpServer.Guard);

        Assert.Null(title);
        Assert.Null(LinkTitlesStoreAccess.Title(id));
    }

    [Fact]
    [Trait("Req", "PR-THEME-135")] // UI/Markdown/LinkTitles.swift:53-54 title.isEmpty ? nil : title
    public async Task An_empty_title_after_trimming_is_remembered_as_no_title()
    {
        await using var server = new LoopbackHttpServer();
        var id = UniqueId("fetchempty");

        var pending = LinkTitles.TitleForVideo(id, server.Url("/oembed"));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("{\"title\":\"   \"}"));

        var title = await pending.WaitAsync(LoopbackHttpServer.Guard);

        Assert.Null(title);
        Assert.Null(LinkTitlesStoreAccess.Title(id));
    }
}

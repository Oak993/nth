using Marrow.Core.Port;
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
// VideoID's "?v=<id>" fallback (:25-27) and the network fetch title(forVideo:)/oembedURL/OEmbed (:43-69) are
// not ported: see the STOP notes on LinkTitles.VideoID and LinkTitles (src/Marrow.UI/Markdown/LinkTitles.cs).
// The Store is Swift `private`, reached here only through LinkTitlesStoreAccess (Fixtures), by reflection.
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
    public void A_youtube_com_path_whose_first_segment_is_not_a_recognised_prefix_extracts_nothing_today()
    {
        // Swift also reads this shape from the "?v=<id>" query item (:25-27); that fallback is not ported (see
        // the STOP note on LinkTitles.VideoID), so only the "no id extracted" half of this input is asserted.
        Assert.Null(LinkTitles.VideoID(Url("https://youtube.com/playlist?list=PL1234567890abcdef")));
        Assert.Null(LinkTitles.VideoID(Url("https://youtube.com/results?search_query=cats")));
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
}

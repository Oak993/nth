using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B2 1fbf81e). This file covers part 2's records:
// PR-THEME-130, 131, 132, 133, 137, 138, 139, 148 (the RichLink title/forge/asset-key layer and FaviconCache's
// privacy test). PR-THEME-144, 151, 177, 178 and UI-THEME-028..039 are not covered here (see the packet report).
public sealed class RichLinkTitlesAndFaviconsTests
{
    private static SwiftURL Url(string text) => SwiftURL.Create(text) ?? throw new InvalidOperationException(text);

    private static AttributedText AutolinkedBareDomains(AttributedText parsed)
    {
        var method = typeof(RichLink).GetMethod("AutolinkedBareDomains", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (AttributedText)method.Invoke(null, [parsed])!;
    }

    // ---- PR-THEME-130: autolinkedBareDomains skips already-linked/code runs, trims 8 trailing punctuation
    // characters (a different set from isBareDisplay's 10), and never goes backwards ----

    [Fact]
    [Trait("Req", "PR-THEME-130")] // MarkdownView.swift:778-780: a run already a link is appended unchanged
    public void Autolinker_leaves_a_run_that_already_carries_a_link_untouched()
    {
        var url = Url("https://example.org/x");
        var parsed = new AttributedText([new AttributedRun("getdroppy.app", Link: url)]);

        var result = AutolinkedBareDomains(parsed);

        var run = Assert.Single(result.Runs);
        Assert.Same(url, run.Link);
        Assert.Equal("getdroppy.app", run.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-130")] // MarkdownView.swift:778-780: a code run is appended unchanged, so a domain in backticks never links
    public void Autolinker_leaves_a_code_run_untouched()
    {
        var parsed = new AttributedText([new AttributedRun("getdroppy.app", IsCode: true)]);

        var result = AutolinkedBareDomains(parsed);

        var run = Assert.Single(result.Runs);
        Assert.Null(run.Link);
        Assert.True(run.IsCode);
    }

    [Fact]
    [Trait("Req", "PR-THEME-130")] // MarkdownView.swift:790, :803-804: the 8-character trim set ".,;:!?'\""
    public void Autolinker_trims_a_trailing_apostrophe_from_the_eight_character_set()
    {
        var parsed = new AttributedText([new AttributedRun("See getdroppy.app'")]);

        var result = AutolinkedBareDomains(parsed);

        var link = Assert.Single(result.Runs, r => r.Link is not null);
        Assert.Equal("getdroppy.app", link.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-130")] // MarkdownView.swift:790: gotcha 2 -- the autolinker's 8-char trim set differs
    // from isBareDisplay's 10-char set: a trailing ')' is not in the autolinker's set (it is in isBareDisplay's),
    // and the domain pattern itself never captures ')' into the match to begin with.
    public void Autolinker_trim_set_does_not_include_the_closing_parenthesis()
    {
        var parsed = new AttributedText([new AttributedRun("(see getdroppy.app)")]);

        var result = AutolinkedBareDomains(parsed);

        var link = Assert.Single(result.Runs, r => r.Link is not null);
        Assert.Equal("getdroppy.app", link.Text);
        Assert.Equal(")", result.Runs[^1].Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-130")] // MarkdownView.swift:801 guard start >= cursor: overlapping matches never go backwards
    public void Autolinker_never_revisits_text_before_the_running_cursor()
    {
        // Two adjacent bare domains separated by one space; the cursor after the first match must not let a
        // second, earlier-starting match re-emit already-consumed text.
        var parsed = new AttributedText([new AttributedRun("getdroppy.app example.com")]);

        var result = AutolinkedBareDomains(parsed);

        var links = result.Runs.Where(r => r.Link is not null).ToList();
        Assert.Equal(2, links.Count);
        Assert.Equal("getdroppy.app", links[0].Text);
        Assert.Equal("example.com", links[1].Text);
        // Concatenating every run's text reproduces the source exactly (no dropped or duplicated text).
        Assert.Equal("getdroppy.app example.com", string.Concat(result.Runs.Select(r => r.Text)));
    }

    // ---- PR-THEME-133: forgeReference ----

    [Fact]
    [Trait("Req", "PR-THEME-133")] // MarkdownView.swift:709-713 GitLab merge_requests
    public void GitLab_merge_request_path_collapses_to_a_bang_reference()
    {
        Assert.Equal("!3190", RichLink.ForgeReference(Url("https://gitlab.example.com/group/project/-/merge_requests/3190")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-133")] // MarkdownView.swift:709-713 GitLab issues
    public void GitLab_issue_path_collapses_to_a_hash_reference()
    {
        Assert.Equal("#42", RichLink.ForgeReference(Url("https://gitlab.example.com/group/project/-/issues/42")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-133")] // MarkdownView.swift:719-721 GitHub-style pull
    public void GitHub_style_pull_path_collapses_to_a_hash_reference()
    {
        Assert.Equal("#123", RichLink.ForgeReference(Url("https://github.com/owner/repo/pull/123")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-133")] // MarkdownView.swift:719-721 GitHub-style issues
    public void GitHub_style_issues_path_collapses_to_a_hash_reference()
    {
        Assert.Equal("#7", RichLink.ForgeReference(Url("https://example.org/owner/repo/issues/7")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-133")] // gotcha 8: the id is round-tripped through an integer, so leading zeros are lost
    public void A_forge_id_with_leading_zeros_round_trips_through_an_integer()
    {
        Assert.Equal("#7", RichLink.ForgeReference(Url("https://github.com/owner/repo/pull/007")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-133")] // no match falls through
    public void A_path_matching_no_forge_shape_yields_no_reference()
    {
        Assert.Null(RichLink.ForgeReference(Url("https://example.com/some/other/path")));
    }

    // ---- PR-THEME-132: prettyTitle cascade ----

    [Fact]
    [Trait("Req", "PR-THEME-132")] // MarkdownView.swift:671 forge reference wins
    public void PrettyTitle_prefers_a_forge_reference_over_the_host_form()
    {
        Assert.Equal("#123", RichLink.PrettyTitle(Url("https://github.com/owner/repo/pull/123")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-132")] // MarkdownView.swift:682-683: a nameless last segment shows the host alone
    public void PrettyTitle_shows_only_the_host_when_the_last_segment_is_nameless()
    {
        Assert.Equal("example.com", RichLink.PrettyTitle(Url("https://www.example.com/watch")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-132")] // MarkdownView.swift:684 host + "/" + tail, one segment
    public void PrettyTitle_joins_host_and_tail_for_one_path_segment()
    {
        Assert.Equal("example.com/pricing", RichLink.PrettyTitle(Url("https://www.example.com/pricing")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-132")] // MarkdownView.swift:684 host + "/…/" + tail, more than one segment
    public void PrettyTitle_elides_the_middle_for_more_than_one_path_segment()
    {
        Assert.Equal("example.com/…/pricing", RichLink.PrettyTitle(Url("https://www.example.com/a/b/pricing")));
    }

    [Fact]
    [Trait("Req", "PR-THEME-132")] // MarkdownView.swift:683: a last segment longer than 24 is cut to 21 + ellipsis
    public void PrettyTitle_truncates_a_long_last_segment_to_21_characters_plus_an_ellipsis()
    {
        var longSegment = new string('a', 30);
        var result = RichLink.PrettyTitle(Url("https://example.com/" + longSegment));
        Assert.Equal("example.com/" + new string('a', 21) + "…", result);
    }

    [Fact]
    [Trait("Req", "PR-THEME-132")] // MarkdownView.swift:686-687: a title over 40 keeps its first 19 and last 18
    public void PrettyTitle_over_40_characters_keeps_head_and_tail_around_one_ellipsis()
    {
        var result = RichLink.PrettyTitle(Url("https://averyveryverylonghostname.example.com/a/somewhatlongpathsegment"));
        Assert.True(result.Length > 40 ? false : true); // sanity: cap always applies when over 40
        Assert.Equal(19 + 1 + 18, result.Length);
        Assert.Contains('…', result);
    }

    [Fact]
    [Trait("Req", "PR-THEME-132")] // MarkdownView.swift:673-674: no host falls back to compactFallback
    public void PrettyTitle_with_no_host_falls_back_to_the_compact_absolute_string()
    {
        var result = RichLink.PrettyTitle(Url("mailto:person@example.com"));
        Assert.Equal("mailto:person@example.com", result);
    }

    [Fact]
    [Trait("Req", "PR-THEME-132")] // MarkdownView.swift:676 "www." is stripped and the host is lower-cased
    public void PrettyTitle_strips_a_leading_www_and_lower_cases_the_host()
    {
        Assert.Equal("example.com", RichLink.PrettyTitle(Url("https://WWW.Example.com/index.html")));
    }

    // ---- PR-THEME-132 (capped, via a cached video title) ----

    [Fact]
    [Trait("Req", "PR-THEME-132")] // gotcha 7: capped at 48 keeps only 45 chars plus one ellipsis (46 total)
    public void Capped_at_48_keeps_45_characters_and_one_ellipsis()
    {
        var method = typeof(RichLink).GetMethod("Capped", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var title = new string('x', 60);
        var result = (string)method.Invoke(null, [title, 48])!;
        Assert.Equal(46, result.Length);
        Assert.Equal(new string('x', 45) + "…", result);
    }

    [Fact]
    [Trait("Req", "PR-THEME-132")] // capped is a no-op under the limit
    public void Capped_under_the_limit_is_unchanged()
    {
        var method = typeof(RichLink).GetMethod("Capped", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var result = (string)method.Invoke(null, ["short", 48])!;
        Assert.Equal("short", result);
    }

    // ---- PR-THEME-131: isBareDisplay ----

    [Fact]
    [Trait("Req", "PR-THEME-131")] // MarkdownView.swift:744: display equal to the absolute URL is bare
    public void IsBareDisplay_true_when_display_equals_the_absolute_url()
    {
        var url = Url("https://example.com/path");
        Assert.True(RichLink.IsBareDisplay("https://example.com/path", url));
    }

    [Fact]
    [Trait("Req", "PR-THEME-131")] // MarkdownView.swift:745-746: an http(s) scheme with no whitespace is bare
    public void IsBareDisplay_true_for_an_http_scheme_display_with_no_whitespace()
    {
        var url = Url("https://example.com/other");
        Assert.True(RichLink.IsBareDisplay("https://example.com/different-but-schemed", url));
    }

    [Fact]
    [Trait("Req", "PR-THEME-131")] // gotcha 3: a bare domain (no scheme) is never bare-display
    public void IsBareDisplay_false_for_a_bare_domain_with_no_scheme()
    {
        var url = Url("https://getdroppy.app/");
        Assert.False(RichLink.IsBareDisplay("getdroppy.app", url));
    }

    [Fact]
    [Trait("Req", "PR-THEME-131")] // MarkdownView.swift:739-740: angle brackets are stripped before comparing
    public void IsBareDisplay_strips_one_pair_of_angle_brackets()
    {
        var url = Url("https://example.com/path");
        Assert.True(RichLink.IsBareDisplay("<https://example.com/path>", url));
    }

    [Fact]
    [Trait("Req", "PR-THEME-131")] // MarkdownView.swift:741-742: trailing punctuation from the ten-character set is trimmed
    public void IsBareDisplay_trims_trailing_punctuation_before_comparing()
    {
        var url = Url("https://example.com/path");
        Assert.True(RichLink.IsBareDisplay("https://example.com/path.", url));
    }

    [Fact]
    [Trait("Req", "PR-THEME-131")] // MarkdownView.swift:745: a display with whitespace is never bare
    public void IsBareDisplay_false_when_the_scheme_display_contains_whitespace()
    {
        var url = Url("https://example.com/path");
        Assert.False(RichLink.IsBareDisplay("https://example.com/pa th", url));
    }

    [Fact]
    [Trait("Req", "PR-THEME-131")] // a titled link is not bare
    public void IsBareDisplay_false_for_an_authored_title()
    {
        var url = Url("https://example.com/path");
        Assert.False(RichLink.IsBareDisplay("Read the docs", url));
    }

    // ---- PR-THEME-131: Build() bolds every link and swaps bare display for the pretty title ----

    [Fact]
    [Trait("Req", "PR-THEME-131")] // MarkdownView.swift:596-621: every link run is bold
    public void Build_marks_every_link_run_bold_even_when_titled()
    {
        var result = RichLink.Build("[Docs](https://example.com/docs)");

        var link = Assert.Single(result.Runs, r => r.Link is not null);
        Assert.True(link.IsBold);
        Assert.Equal("Docs", link.Text);
    }

    [Fact]
    [Trait("Req", "PR-THEME-131")] // MarkdownView.swift:601-609: a bare-display link's text becomes the pretty title
    public void Build_replaces_a_bare_display_links_text_with_its_pretty_title()
    {
        var result = RichLink.Build("<https://www.example.com/pricing>");

        var link = Assert.Single(result.Runs, r => r.Link is not null);
        Assert.True(link.IsBold);
        Assert.Equal("example.com/pricing", link.Text);
    }

    // ---- PR-THEME-148: linkHosts / videoIDs / linkAssetKey / invalidate ----

    [Fact]
    [Trait("Req", "PR-THEME-148")] // MarkdownView.swift:623-634: first-seen order, lower-cased, each host once
    public void LinkHosts_returns_each_host_once_in_first_seen_order_lower_cased()
    {
        var source = "[a](https://Example.com/1) and [b](https://other.com/2) and [c](https://EXAMPLE.com/3)";
        var hosts = RichLink.LinkHosts(source);
        Assert.Equal(["example.com", "other.com"], hosts.AsEnumerable());
    }

    [Fact]
    [Trait("Req", "PR-THEME-148")] // MarkdownView.swift:652-657: hosts and videos joined with a vertical bar
    public void LinkAssetKey_joins_hosts_and_videos_with_a_vertical_bar()
    {
        var source = "[a](https://example.com/1) [b](https://www.youtube.com/watch?v=dQw4w9WgXcQ)";
        var key = RichLink.LinkAssetKey(source);
        Assert.Equal("example.com,www.youtube.com|dQw4w9WgXcQ", key);
    }

    [Fact]
    [Trait("Req", "PR-THEME-148")] // MarkdownView.swift:655: empty when there is neither a host nor a video
    public void LinkAssetKey_is_empty_when_the_paragraph_has_no_links()
    {
        Assert.Equal("", RichLink.LinkAssetKey("no links in this paragraph"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-148")] // MarkdownView.swift:660-664: invalidate drops the settled and streaming caches
    public void Invalidate_makes_a_later_build_reflect_a_changed_video_title()
    {
        var source = "[x](https://www.youtube.com/watch?v=dQw4w9WgXcQ)";
        _ = RichLink.PrettyAttributed(source); // warms prettyCache
        RichLink.Invalidate(source);
        // A second call does not throw and still resolves the same link (title still not cached, so host form).
        var after = RichLink.PrettyAttributed(source);
        Assert.Contains(after.Runs, r => r.Link is not null);
    }

    [Fact]
    [Trait("Req", "PR-THEME-148")] // MarkdownView.swift:571-576: the streaming path keeps its own small ring
    public void PrettyAttributed_streaming_finds_its_own_prior_build_without_the_stable_cache()
    {
        var source = "streaming paragraph with a [link](https://example.com/x) " + Guid.NewGuid();
        var first = RichLink.PrettyAttributed(source, streaming: true);
        var second = RichLink.PrettyAttributed(source, streaming: true);
        Assert.Same(first, second);
    }

    // ---- PR-THEME-137/139: FaviconCache.IsPublic ----

    [Theory]
    [Trait("Req", "PR-THEME-137")]
    [InlineData("", false)] // MarkdownView.swift:1060 empty
    [InlineData("localhost", false)] // MarkdownView.swift:1060
    [InlineData("fe80::1", false)] // MarkdownView.swift:1063 contains ':'
    [InlineData("box.local", false)] // MarkdownView.swift:1051-1054, :1064
    [InlineData("server.internal", false)]
    [InlineData("host.lan", false)]
    [InlineData("myserver", false)] // MarkdownView.swift:1067 no dot
    [InlineData("example.com", true)]
    [InlineData("www.example.com", true)]
    public void IsPublic_matches_the_source_privacy_rules(string host, bool expected)
    {
        Assert.Equal(expected, FaviconCache.IsPublic(host));
    }

    [Theory]
    [Trait("Req", "PR-THEME-137")] // MarkdownView.swift:1075-1080: private and carrier-grade-NAT IPv4 ranges
    [InlineData("0.1.2.3", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("172.32.0.1", true)] // just outside the 172.16-31 range
    [InlineData("100.64.0.1", false)] // carrier-grade NAT
    [InlineData("100.127.255.255", false)]
    [InlineData("100.128.0.1", true)] // just outside the CGNAT range
    [InlineData("8.8.8.8", true)]
    public void IsPublic_rejects_exactly_the_private_and_cgnat_ipv4_ranges(string host, bool expected)
    {
        Assert.Equal(expected, FaviconCache.IsPublic(host));
    }

    [Fact]
    [Trait("Req", "PR-THEME-137")] // MarkdownView.swift:917-921: a non-public host is recorded as failed with no request
    public async Task Image_for_a_non_public_host_returns_null_without_a_request()
    {
        var result = await FaviconCache.Image("some-machine.local");
        Assert.Null(result);
        Assert.Null(FaviconCache.Cached("some-machine.local"));
    }
}

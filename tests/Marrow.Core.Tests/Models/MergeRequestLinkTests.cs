using System.Diagnostics;
using System.Globalization;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models;

// Source: DroppyCode/Core/Models/MergeRequestLink.swift (B2 1fbf81e), :1-62.
// Expected values are transcribed from the Swift and from PR-GIT-052 to PR-GIT-055, never from the port.
// Names follow IMPLEMENTING.md: the failable init?(url:) is Create (R-NAME-5); the cases gitlab, github, gitea are
// Gitlab, Github, Gitea (R-NAME-2); the property forge, named like its nested type Forge, is LinkForge (R-NAME-3,
// as ApprovalRequest.Kind is RequestKind and Option.Role is OptionRole in Requests.cs); Int is long (64-bit Int).
// The Swift parameter is a Foundation URL, so the port takes the F.URL foundation type SwiftURL (R-PATH-1: never
// System.Uri). Every URL is made by SwiftURL.Create, the port of URL(string:), which on macOS 14 and later accepts
// every non-empty string (percent-encoding what RFC 3986 does not allow), so Url(text) asserts a URL exists.
// Rows under a Req trait give the same result whichever way url.pathComponents (:24) splits and decodes the path.
// Rows whose result depends on that (an encoded "/", an empty, "." or ".." segment, "\", a relative reference) are
// in the Oracle-labelled tests at the end (R-TEST-6, O-44 and ED-143).
public sealed class MergeRequestLinkTests
{
    private static SwiftURL Url(string text)
    {
        var url = SwiftURL.Create(text);
        Assert.NotNull(url);
        return url;
    }

    private static MergeRequestLink? Link(string text) => MergeRequestLink.Create(Url(text));

    private const string SyncTail = " Once it is merged, sync the checkout: if this directory is on main with a clean working tree, run `git pull --ff-only`; otherwise leave the working tree alone and say so. Do not rebuild, install or relaunch anything. Reply in a few lines with the merged commit and the request's link.";

    private static string Landing(MergeRequestLink.Forge forge, string number) => forge switch
    {
        MergeRequestLink.Forge.Gitlab => "Use glab from this directory: `glab mr merge " + number + " --yes`. If GitLab answers that the request is still being checked (a 405), wait a few seconds and try again; if it reports conflicts, stop and say so instead of resolving them.",
        MergeRequestLink.Forge.Github => "Use gh from this directory: `gh pr merge " + number + " --merge`. If GitHub reports the pull request is not mergeable, stop and say so instead of resolving it.",
        MergeRequestLink.Forge.Gitea => "Use tea from this directory: `tea pulls merge " + number + "`. If tea is not installed or signed in to this host, or the pull request is not mergeable, stop and say so instead of working around it.",
        _ => throw new UnreachableException(),
    };

    // Rows: the verification_method table of PR-GIT-052 (GitLab subgroup, /diffs suffix, github.com, a GitHub
    // Enterprise host, Codeberg), plus: the branch order of :25-33 (merge_requests is tried first, so a path with both
    // shapes is GitLab); firstIndex (:25) finds a merge_requests at index 0 and stops there, so the pull branch
    // decides; Int's leading "+" (Int("+95") is 95, R-NUM-1), written as is and percent-encoded (%2B decodes to "+");
    // the 64-bit Int maximum; numbers past 999 (the label interpolates \(number) with no grouping, :18); a trailing
    // "/" (the "/" filter of :24); and fall-through rows where merge_requests sits at index 1 or later but
    // fails the rest of :25-26. :25-26 is one condition list, so any failing condition drops to the else-if at :29 or
    // :32: number > 0 fails ("/0"), index + 1 < parts.count fails (merge_requests is last), Int fails ("bad").
    [Theory]
    [Trait("Req", "PR-GIT-052")] // Core/Models/MergeRequestLink.swift:23-39
    [Trait("Req", "PR-GIT-053")] // Core/Models/MergeRequestLink.swift:17-18
    [Trait("Req", "PR-GIT-054")] // Core/Models/MergeRequestLink.swift:41-42
    [InlineData("https://gitlab.example/group/sub/project/-/merge_requests/95/diffs", MergeRequestLink.Forge.Gitlab, 95L, "!95")]
    [InlineData("https://gitlab.com/group/project/-/merge_requests/95", MergeRequestLink.Forge.Gitlab, 95L, "!95")]
    [InlineData("https://gitlab.example/x/merge_requests/1", MergeRequestLink.Forge.Gitlab, 1L, "!1")]
    [InlineData("https://github.com/owner/repo/pull/95", MergeRequestLink.Forge.Github, 95L, "#95")]
    [InlineData("https://github.com/owner/repo/pull/95/", MergeRequestLink.Forge.Github, 95L, "#95")]
    [InlineData("https://enterprise.example/owner/repo/pull/+95/files?x=1#discussion", MergeRequestLink.Forge.Github, 95L, "#95")]
    [InlineData("https://codeberg.org/owner/repo/pulls/95", MergeRequestLink.Forge.Gitea, 95L, "#95")]
    [InlineData("https://forgejo.example/owner/repo/pulls/9223372036854775807", MergeRequestLink.Forge.Gitea, long.MaxValue, "#9223372036854775807")]
    [InlineData("https://github.com/owner/repo/pull/1000", MergeRequestLink.Forge.Github, 1000L, "#1000")]
    [InlineData("https://gitlab.example/g/p/-/merge_requests/1234567", MergeRequestLink.Forge.Gitlab, 1234567L, "!1234567")]
    [InlineData("https://any.example/merge_requests/bad/pull/5", MergeRequestLink.Forge.Github, 5L, "#5")]
    [InlineData("https://host/o/r/pull/5/merge_requests/7", MergeRequestLink.Forge.Gitlab, 7L, "!7")]
    [InlineData("https://host/o/r/pulls/5/merge_requests/7", MergeRequestLink.Forge.Gitlab, 7L, "!7")]
    [InlineData("https://host/o/r/pull/5/merge_requests/0", MergeRequestLink.Forge.Github, 5L, "#5")]
    [InlineData("https://host/o/r/pulls/5/merge_requests", MergeRequestLink.Forge.Gitea, 5L, "#5")]
    [InlineData("https://host/o/r/pull/5/merge_requests/bad", MergeRequestLink.Forge.Github, 5L, "#5")]
    public void Recognizes_the_three_path_shapes_on_any_host_with_the_forge_label_and_title(string text, MergeRequestLink.Forge forge, long number, string label)
    {
        var link = Link(text);
        Assert.NotNull(link);
        Assert.Equal(forge, link.LinkForge);
        Assert.Equal(number, link.Number);
        Assert.Equal(Encoding.UTF8.GetBytes(label), Encoding.UTF8.GetBytes(link.Label));
        Assert.Equal(Encoding.UTF8.GetBytes("Merge " + label), Encoding.UTF8.GetBytes(link.Title));
        // :38 self.url = url: the URL is kept as given, query, fragment and case included, compared as text. Every
        // row is a valid URL string, which URL(string:) keeps byte for byte as its absoluteString (SwiftURLTests).
        Assert.Equal(Encoding.UTF8.GetBytes(text), Encoding.UTF8.GetBytes(link.Url.AbsoluteString));
        Assert.Equal(Url(text), link.Url);
    }

    // Every row is nil in Swift whether or not the path component is percent-decoded: a component that is not an
    // Int either way, a number that is not above zero, or a shape that does not match.
    [Theory]
    [Trait("Req", "PR-GIT-052")] // Core/Models/MergeRequestLink.swift:23-39
    [InlineData("https://host/merge_requests/95")] // :25 index >= 1 fails at index 0
    [InlineData("https://host/g/p/-/merge_requests")] // :25 index + 1 < parts.count fails: merge_requests is last
    [InlineData("https://host/g/p/-/merge_requests/")] // the same after the "/" filter of :24
    [InlineData("https://host/x/merge_requests/bad/merge_requests/95")] // :25 firstIndex only: the first one is not followed by an Int
    [InlineData("https://host/g/p/-/merge_requests/0")] // :26 number > 0
    [InlineData("https://host/g/p/-/merge_requests/-3")] // :26 number > 0
    [InlineData("https://host/o/r/pull/0")] // :29 number > 0
    [InlineData("https://host/o/r/pull/-5")] // :29 number > 0
    [InlineData("https://host/o/r/pulls/-1")] // :32 number > 0
    [InlineData("https://host/o/r/pulls/0")] // :32 number > 0
    [InlineData("https://host/o/r/Pull/95")] // :29 parts[2] == "pull" is case-sensitive
    [InlineData("https://host/o/r/PULLS/95")] // :32 parts[2] == "pulls" is case-sensitive
    [InlineData("https://host/g/p/-/MERGE_REQUESTS/95")] // :25 firstIndex(of: "merge_requests") is exact String equality
    [InlineData("https://host/o/r/pull/abc")] // Int("abc") is nil
    [InlineData("https://host/o/r/pull/1.0")] // Int("1.0") is nil
    [InlineData("https://host/o/r/pull/9223372036854775808")] // Int overflows to nil
    [InlineData("https://host/o/r/pull/%2095")] // Int(" 95") is nil (no whitespace, R-NUM-1)
    [InlineData("https://host/o/r/pull/95%20")] // Int("95 ") is nil
    [InlineData("https://host/o/r/pull/95%00")] // Int("95\0") is nil: no trailing NUL, unlike long.TryParse
    [InlineData("https://host/o/r/pulls/95%00")] // the same for the Gitea branch
    [InlineData("https://host/g/p/-/merge_requests/95%00")] // the same for the GitLab branch
    [InlineData("https://host/o/r/pull/1_000")] // Int("1_000") is nil
    [InlineData("https://host/o/r/pull/1,000")] // Int("1,000") is nil
    [InlineData("https://host/o/r/pull/%EF%BC%99%EF%BC%95")] // FULLWIDTH DIGIT NINE, FIVE (U+FF19 U+FF15): Int takes ASCII digits only (R-NUM-1)
    [InlineData("https://host/o/r/pull/%D9%A5")] // ARABIC-INDIC DIGIT FIVE (U+0665): Int is nil (R-NUM-1)
    [InlineData("https://host/o/r/pull")] // :29 parts.count >= 4
    [InlineData("https://host/o/r/issues/95")] // no shape matches
    [InlineData("https://host/prefix/o/r/pull/95")] // :29 the index is fixed at 2
    [InlineData("https://host/")] // no components
    [InlineData("https://host")] // no path at all
    public void Rejects_other_shapes_and_numbers_that_Swift_Int_refuses_or_that_are_not_positive(string text)
        => Assert.Null(Link(text));

    // Byte-for-byte prompt of :47-61. Swift strips the closing delimiter's indentation from the multi-line literal and
    // ends it without a final line break, so the text is: the first line, "\n\n", the landing paragraph, then the
    // shared tail. The pinned file is checked out with CRLF line terminators, but Swift normalizes every line break
    // inside a multi-line string literal (:56-60) to U+000A, so the separator is "\n\n", never "\r\n\r\n".
    // The number is interpolated with no grouping (\(number), :50, :52, :54).
    [Theory]
    [Trait("Req", "PR-GIT-055")] // Core/Models/MergeRequestLink.swift:47-61
    [InlineData("https://host/g/p/-/merge_requests/95", MergeRequestLink.Forge.Gitlab, "95", "!95")]
    [InlineData("https://host/o/r/pull/95", MergeRequestLink.Forge.Github, "95", "#95")]
    [InlineData("https://host/o/r/pulls/95", MergeRequestLink.Forge.Gitea, "95", "#95")]
    [InlineData("https://host/g/p/-/merge_requests/1000", MergeRequestLink.Forge.Gitlab, "1000", "!1000")]
    [InlineData("https://host/o/r/pull/1000", MergeRequestLink.Forge.Github, "1000", "#1000")]
    [InlineData("https://host/o/r/pulls/1000", MergeRequestLink.Forge.Gitea, "1000", "#1000")]
    [InlineData("https://host/g/p/-/merge_requests/9223372036854775807", MergeRequestLink.Forge.Gitlab, "9223372036854775807", "!9223372036854775807")]
    [InlineData("https://host/o/r/pull/9223372036854775807", MergeRequestLink.Forge.Github, "9223372036854775807", "#9223372036854775807")]
    [InlineData("https://host/o/r/pulls/9223372036854775807", MergeRequestLink.Forge.Gitea, "9223372036854775807", "#9223372036854775807")]
    public void Prompt_is_the_source_text_for_each_forge_with_the_number_ungrouped(string text, MergeRequestLink.Forge forge, string number, string label)
    {
        var expected = "Merge " + label + " now: " + text + "\n\n" + Landing(forge, number) + SyncTail;
        var link = Link(text);
        Assert.NotNull(link);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(link.Prompt));
    }

    // :57 interpolates url.absoluteString, the URL's own text as stored at :38, not a canonical absolute URI
    // (PR-GIT-055 expected_result and spec_fix): scheme and host case, an explicit default port, the query, the
    // fragment and a percent-encoded byte are written as given (a relative reference is in the Oracle tests at the
    // end). The number in the landing paragraph is the parsed Int (+95 and %2B95 both give 95).
    [Theory]
    [Trait("Req", "PR-GIT-055")] // Core/Models/MergeRequestLink.swift:57
    [InlineData("HTTPS://GitHub.COM:443/o/r/pull/5", MergeRequestLink.Forge.Github, "5", "#5")]
    [InlineData("https://GitLab.Example/Group/Project/-/merge_requests/95/diffs?view=inline#note_1", MergeRequestLink.Forge.Gitlab, "95", "!95")]
    [InlineData("https://codeberg.org/o/r/pulls/7#issuecomment-3", MergeRequestLink.Forge.Gitea, "7", "#7")]
    [InlineData("https://host/o/r/pull/+95", MergeRequestLink.Forge.Github, "95", "#95")]
    public void Prompt_first_line_carries_the_url_text_as_given(string text, MergeRequestLink.Forge forge, string number, string label)
    {
        var link = Link(text);
        Assert.NotNull(link);
        var expected = "Merge " + label + " now: " + text + "\n\n" + Landing(forge, number) + SyncTail;
        Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(link.Prompt));
        Assert.StartsWith("Merge " + label + " now: " + text + "\n\n", link.Prompt, StringComparison.Ordinal);
    }

    // Swift synthesizes == over url, forge and number (:5, :12-15); URL equality is textual (SwiftURLTests), so two
    // links that differ only in the fragment or the host's case are different values.
    [Fact]
    [Trait("Rule", "R-VAL-1")] // Core/Models/MergeRequestLink.swift:5, :12-15
    public void Links_are_equal_exactly_when_their_url_text_is_equal()
    {
        var a = Link("https://github.com/o/r/pull/5#a");
        var b = Link("https://github.com/o/r/pull/5#a");
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, Link("https://github.com/o/r/pull/5#b"));
        Assert.NotEqual(a, Link("https://GitHub.com/o/r/pull/5#a"));
    }

    // Swift's explicit init?(url:) (:23) suppresses the memberwise initializer, so every value comes from a parsed
    // URL: no public constructor may build a link with an arbitrary forge and number.
    [Fact]
    [Trait("Rule", "R-NAME-5")] // Core/Models/MergeRequestLink.swift:23
    public void The_only_way_to_make_a_link_is_the_failable_Create()
        => Assert.Empty(typeof(MergeRequestLink).GetConstructors());

    // Negative control (R-TEST-4): the obvious .NET parse accepts a trailing NUL that Swift Int(String) refuses,
    // which is why the port parses with SwiftNumber.ParseInt.
    [Fact]
    [Trait("Rule", "R-NUM-1")] // Core/Models/MergeRequestLink.swift:26, :29, :32
    public void Dotnet_long_TryParse_accepts_a_trailing_NUL_that_Swift_Int_refuses()
    {
        Assert.True(long.TryParse("95\0", NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value));
        Assert.Equal(95L, value);
        Assert.Null(SwiftNumber.ParseInt("95\0"));
    }

    // Negative control (R-TEST-4): System.Uri removes dot segments, turns "\" into "/" and canonicalizes the text,
    // which is why the link takes a SwiftURL (R-PATH-1) and the prompt uses its absolute string (PR-GIT-055). What
    // Swift does with dot segments and "\" is the Oracle item of the tests below; this test pins only .NET.
    [Fact]
    [Trait("Rule", "R-PATH-1")] // Core/Models/MergeRequestLink.swift:24, :57
    public void Dotnet_Uri_rewrites_the_path_and_text()
    {
        Assert.Equal("/x/o/pull/5", new Uri("https://host/x/o/r/../pull/5").AbsolutePath);
        Assert.Equal("/o/r/pull/5", new Uri("https://host/o/r/./pull/5").AbsolutePath);
        Assert.Equal("/o/r/pull/5", new Uri("https://host/o/r\\pull\\5").AbsolutePath);
        Assert.Equal("https://github.com/o/r/pull/5", new Uri("HTTPS://GitHub.COM:443/o/r/pull/5").AbsoluteUri);
    }

    // UNVERIFIED (R-TEST-6). Every row below rests on how url.pathComponents (:24) splits and decodes the path, and
    // O-44 and ED-143 record a swift-foundation 6.2 reading, pending macOS 26 capture. No Req trait until then.
    // Parked expectation, a source reading and not a capture (the macOS 26 transfer question of ED-086 applies):
    // swift-foundation tag swift-6.2-RELEASE, Sources/FoundationEssentials/URL/URL_Swift.swift:641-647 takes
    // absolutePath(percentEncoded: true).pathComponents and percent-decodes each component, and
    // Sources/FoundationEssentials/String/String+Path.swift:53-77 splits on "/", collapsing runs of "/" (no empty
    // component) and keeping "." and "..". URL(string:) removes no dot segments, and "\" is not a URL path
    // character, so it is percent-encoded and stays inside one component. Older Foundation split the decoded path
    // instead, which gives GitHub 5 for both encoded-slash rows.
    [Theory]
    [Trait("Oracle", "O-44")] // Core/Models/MergeRequestLink.swift:24, :25-37
    [InlineData("https://host/o/r/pull%2F5")] // components o, r, "pull/5": fewer than four
    [InlineData("https://host/o/r%2Fpull/5")] // components o, "r/pull", 5: fewer than four
    [InlineData("https://host/x/o/r/../pull/5")] // ".." kept: parts[2] is "r"
    [InlineData("https://host/o/r/./pull/5")] // "." kept: parts[2] is "r"
    [InlineData("https://host/a/../o/r/pull/95")] // ".." kept: parts[2] is "o"
    [InlineData("https://host/o/r\\pull\\5")] // one component "r\pull\5": fewer than four
    public void Oracle_path_shapes_that_pathComponents_decides_are_rejected(string text)
        => Assert.Null(Link(text));

    // UNVERIFIED (R-TEST-6), the same open item and parked source reading as above: the empty segment between two
    // slashes is dropped, and a relative reference (no leading "/") splits into its four components. The prompt row
    // also pins that :57 writes the relative text as given.
    [Theory]
    [Trait("Oracle", "O-44")] // Core/Models/MergeRequestLink.swift:24, :29, :57
    [InlineData("https://host/o//r/pull/95", 95L, "95", "#95")]
    [InlineData("o/r/pull/5", 5L, "5", "#5")]
    [InlineData("https://host/o/r/pull/%2B95", 95L, "95", "#95")]
    [InlineData("https://host/o/%FF/pull/5", 5L, "5", "#5")]
    [InlineData("https://host/o/%2F/r/pull/5", 5L, "5", "#5")]
    public void Oracle_path_shapes_that_pathComponents_decides_are_github(string text, long number, string digits, string label)
    {
        var link = Link(text);
        Assert.NotNull(link);
        Assert.Equal(MergeRequestLink.Forge.Github, link.LinkForge);
        Assert.Equal(number, link.Number);
        Assert.Equal(Encoding.UTF8.GetBytes(label), Encoding.UTF8.GetBytes(link.Label));
        var expected = "Merge " + label + " now: " + text + "\n\n" + Landing(MergeRequestLink.Forge.Github, digits) + SyncTail;
        Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(link.Prompt));
    }
}

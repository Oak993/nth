using Marrow.App;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using static Marrow.App.Tests.Fixtures.ReleaseFeed;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/UpdateChecker.swift (B2 1fbf81e), :1-327. Part 1 of 2: PR-SETTINGS-105, PR-UPDATE-001 to
// PR-UPDATE-023. This file holds the pure parts (AppVersion, newestRelease, the asset predicate);
// UpdateCheckerSecondTests holds the checker (persistence, check(), the schedule, the HTTP session).
// Every expected value is transcribed from the Swift line cited on the test or from the record, never from a C#
// implementation. Non-ASCII text is written as \u escapes (R-STR-9).
//
// The C# API these tests fix (namespace Marrow.App, file src/Marrow.App/UpdateChecker.cs; R-NAME-1 to R-NAME-7):
//   sealed record AppVersion : IComparable<AppVersion>                        :7-38
//     ValueList<long> Components { get; }                                      :8 (Swift Int is long, R-NUM-4)
//     static AppVersion? Create(string raw)                                    :10-22 init?(_:) (R-NAME-5)
//     string Description { get; }                                              :24-26
//     static string Text(string ofTag)                                         :29-33
//     int CompareTo(AppVersion? other); operators <, >, <=, >=                 :35-37 (R-VAL-4 Comparable)
//     value equality over Components (synthesized Hashable)                    :7
//   sealed record AvailableUpdate                                              :41-51
//     string Version; string Tag; SwiftURL DownloadURL; long? Size; string Notes;
//     DateTimeOffset? ReleasedAt; SwiftURL PageURL                             (init properties, R-VAL-1)
//   sealed class UpdateChecker : MainActorModel                                :61-263
//     static Ambient<UpdateChecker> Shared                                     :64 (R-LIFE-2)
//     const string ProjectID = "86415974"; static SwiftURL ProjectURL, ReleasesURL   :67-69
//     const string DefaultFeed = ""                                            DEVIATION OD-0012 I1, replaces apiURL :70
//     UpdateChecker(AppInfo appInfo, IReadOnlyList<string> arguments, string feed)
//         PORT-ONLY constructor for private init() :112-126: AppInfo.version (:83, :108), CommandLine.arguments
//         (:122) and the feed value are parameters; Shared passes AppInfo.Current, the process arguments and
//         DefaultFeed. It reads UserDefaults.Standard.Current as :113 does.
//     string CurrentVersion                                                    :83
//     AvailableUpdate? Update; bool IsChecking; DateTimeOffset? LastCheckedAt; string? LastError   :85-89
//     bool UpdateAvailable                                                     :93-96
//     void StartBackgroundChecks()                                             :132-150
//     Task<bool> Check(bool background = false)                                :156-187 (R-NAME-7)
//     static AvailableUpdate? NewestRelease(IReadOnlyList<GitLabRelease> @in)  :199-219
//   sealed record GitLabRelease                                                :278-327
//     static GitLabRelease Decode(JsonElement element)                         :313-321 (R-CODE-1 reader)
//     nested sealed record Link                                                :283-297
//       static string ArtifactSuffix  DEVIATION OD-0008 C6: the Windows artifact suffix that replaces ".dmg" at
//                                     :290. Its value is not fixed by the ruling (PR-UPDATE-016 windows_path), so
//                                     these tests use it symbolically and assert only the two-branch shape.
// SwiftURL (Core/Port, unit F.URL) supplies AbsoluteString; ReleasesURL.appending(path:) (:69, :213) needs
// SwiftURL.Appending(string path), which F.URL does not yet list (reported as assumed API).
public sealed class UpdateCheckerTests
{
    private static string Suffix => GitLabRelease.Link.ArtifactSuffix;

    // ---- AppVersion (PR-UPDATE-001 to PR-UPDATE-003) ----

    [Theory]
    [Trait("Req", "PR-UPDATE-001")] // UpdateChecker.swift:10-22
    [InlineData("v1.2.3", 1L, 2L, 3L)]
    [InlineData("1.2", 1L, 2L, 0L)]
    [InlineData("7", 7L, 0L, 0L)]
    [InlineData("V4.5.6", 4L, 5L, 6L)]
    [InlineData("01.002.0003", 1L, 2L, 3L)]
    [InlineData(" \n\tv1.2.3 \r\n", 1L, 2L, 3L)]
    [InlineData("\u00A0v1.2\u2028", 1L, 2L, 0L)]
    [InlineData("\u0085v3\u2029", 3L, 0L, 0L)]
    [InlineData("9223372036854775807", 9223372036854775807L, 0L, 0L)]
    public void A_tag_of_one_to_three_numeric_parts_parses_and_is_padded_to_three(string raw, long major, long minor, long patch)
    {
        // :11 raw.trimmed is whitespacesAndNewlines (Core/Support/Text.swift:132): U+00A0 and U+0085 are in it,
        // and U+2028/U+2029 are newlines. :12 drops one leading v or V. :20 pads with 0 until count == 3.
        var version = AppVersion.Create(raw);
        Assert.NotNull(version);
        Assert.Equal(new long[] { major, minor, patch }, version.Components.ToArray());
    }

    [Theory]
    [Trait("Req", "PR-UPDATE-001")] // UpdateChecker.swift:13-19
    [InlineData("")]
    [InlineData("v")]
    [InlineData("vv1.2")]
    [InlineData("1..2")]
    [InlineData("1.")]
    [InlineData(".1")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3-beta.1")]
    [InlineData("1.2.3-nightly.4")]
    [InlineData("1.2.3rc")]
    [InlineData("-1.2")]
    [InlineData("+1.2")]
    [InlineData("1. 2")]
    [InlineData("v 1.2")]
    [InlineData("١.٢")]
    [InlineData("１.２")]
    [InlineData("½")]
    [InlineData("9223372036854775808")]
    [InlineData("99999999999999999999.0")]
    public void A_tag_that_is_empty_has_too_many_parts_an_empty_part_or_a_non_digit_does_not_parse(string raw)
    {
        // :13 omittingEmptySubsequences: false keeps empty parts, :14 requires 1...3 parts, :17 rejects an empty
        // part, a part with a non-number Character, or one Int() refuses. Arabic-Indic (U+0661), fullwidth (U+FF11)
        // digits and U+00BD are isNumber but not ASCII digits, so Int(part) is nil; a part above Int.max is nil.
        Assert.Null(AppVersion.Create(raw));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-002")] // UpdateChecker.swift:35-37 lexicographicallyPrecedes over padded components
    public void Versions_compare_component_by_component_over_three_padded_numbers()
    {
        var v1_9 = AppVersion.Create("1.9.0")!;
        var v1_10 = AppVersion.Create("1.10.0")!;
        Assert.True(v1_10 > v1_9);
        Assert.True(v1_9 < v1_10);
        Assert.True(AppVersion.Create("2")! > AppVersion.Create("1.99.99")!);
        Assert.True(AppVersion.Create("1.2.3")! < AppVersion.Create("1.2.4")!);
        Assert.True(AppVersion.Create("v1.2")! >= AppVersion.Create("1.2.0")!);
        Assert.True(AppVersion.Create("v1.2")! <= AppVersion.Create("1.2.0")!);
        Assert.False(AppVersion.Create("1.2")! > AppVersion.Create("1.2.0")!);
        Assert.Equal(0, AppVersion.Create("1.2")!.CompareTo(AppVersion.Create("1.2.0")));
        Assert.True(v1_9.CompareTo(v1_10) < 0);
        Assert.True(v1_10.CompareTo(v1_9) > 0);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-002")] // UpdateChecker.swift:7 Hashable over components, padded at :20
    public void A_short_tag_equals_its_padded_form()
    {
        var shortForm = AppVersion.Create("1.2")!;
        var padded = AppVersion.Create("v1.2.0")!;
        Assert.Equal(shortForm, padded);
        Assert.Equal(shortForm.GetHashCode(), padded.GetHashCode());
        Assert.NotEqual(shortForm, AppVersion.Create("1.2.1"));
    }

    [Theory]
    [Trait("Req", "PR-UPDATE-002")] // UpdateChecker.swift:24-26 components joined with "."
    [InlineData("v1.2", "1.2.0")]
    [InlineData("7", "7.0.0")]
    [InlineData("01.02.03", "1.2.3")]
    [InlineData(" V10.20.30 ", "10.20.30")]
    public void The_description_is_the_padded_components_joined_by_dots(string raw, string expected)
    {
        Assert.Equal(expected, AppVersion.Create(raw)!.Description);
    }

    [Theory]
    [Trait("Req", "PR-UPDATE-003")] // UpdateChecker.swift:29-33
    [InlineData("v1.3", "1.3")]
    [InlineData("V2", "2")]
    [InlineData(" v1.3\n", "1.3")]
    [InlineData("\u00A0v1.3\u2028", "1.3")]
    [InlineData("vv1", "v1")]
    [InlineData("1.2.3-beta", "1.2.3-beta")]
    [InlineData("01.2", "01.2")]
    [InlineData("", "")]
    public void The_version_text_of_a_tag_drops_one_leading_v_and_the_surrounding_whitespace_only(string tag, string expected)
    {
        Assert.Equal(expected, AppVersion.Text(ofTag: tag));
    }

    // ---- newestRelease (PR-UPDATE-015, PR-UPDATE-003, PR-UPDATE-004, PR-UPDATE-017) ----

    [Fact]
    [Trait("Req", "PR-UPDATE-015")] // UpdateChecker.swift:199-219
    [Trait("Req", "PR-UPDATE-003")] // UpdateChecker.swift:207 version: AppVersion.text(ofTag:)
    [Trait("Req", "PR-UPDATE-004")] // UpdateChecker.swift:206-214 the seven fields of the candidate
    public void The_selected_release_carries_the_unpadded_version_the_raw_tag_no_size_the_notes_the_date_and_the_page()
    {
        using var runtime = new TestRuntime();
        var releases = Decode(Release("v1.3",
            [Link("Marrow-1.3" + Suffix, "https://example.com/files/Marrow-1.3" + Suffix)],
            releasedAt: "2026-09-13T18:28:42Z", description: "## Fixes\n- one", name: "Marrow 1.3"));
        var found = runtime.OnMain(() => UpdateChecker.NewestRelease(releases));
        Assert.NotNull(found);
        Assert.Equal("1.3", found.Version);
        Assert.Equal("v1.3", found.Tag);
        Assert.Equal("https://example.com/files/Marrow-1.3" + Suffix, found.DownloadURL.AbsoluteString);
        Assert.Null(found.Size); // :210 size: nil
        Assert.Equal("## Fixes\n- one", found.Notes);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 18, 28, 42, TimeSpan.Zero), found.ReleasedAt);
        // :213 releasesURL.appending(path: release.tagName), releasesURL = projectURL + "-/releases" (:68-69).
        Assert.Equal("https://gitlab.com/droppyformac1/droppy-code/-/releases/v1.3", found.PageURL.AbsoluteString);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-004")] // UpdateChecker.swift:211 notes: release.description ?? ""
    public void A_release_without_a_description_has_empty_notes_and_one_without_a_date_has_no_date()
    {
        using var runtime = new TestRuntime();
        var releases = Decode(Release("v1.3", [Link("Marrow" + Suffix, "https://example.com/Marrow" + Suffix)]));
        var found = runtime.OnMain(() => UpdateChecker.NewestRelease(releases));
        Assert.NotNull(found);
        Assert.Equal("", found.Notes);
        Assert.Null(found.ReleasedAt);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-015")] // UpdateChecker.swift:215 if let current = best, current.0 >= version { continue }
    public void The_highest_version_wins_whatever_its_place_in_the_feed()
    {
        using var runtime = new TestRuntime();
        var releases = Decode(
            Release("v1.9.0", [Link("a" + Suffix, "https://example.com/a" + Suffix)]),
            Release("v1.10.0", [Link("b" + Suffix, "https://example.com/b" + Suffix)]),
            Release("v1.2", [Link("c" + Suffix, "https://example.com/c" + Suffix)]));
        Assert.Equal("v1.10.0", runtime.OnMain(() => UpdateChecker.NewestRelease(releases))!.Tag);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-015")] // UpdateChecker.swift:215 a later equal version is skipped (>=)
    public void On_an_exact_tie_the_first_release_in_feed_order_is_kept()
    {
        using var runtime = new TestRuntime();
        var releases = Decode(
            Release("v1.7", [Link("first" + Suffix, "https://example.com/first" + Suffix)]),
            Release("1.7.0", [Link("second" + Suffix, "https://example.com/second" + Suffix)]));
        var found = runtime.OnMain(() => UpdateChecker.NewestRelease(releases))!;
        Assert.Equal("v1.7", found.Tag);
        Assert.Equal("https://example.com/first" + Suffix, found.DownloadURL.AbsoluteString);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-015")] // UpdateChecker.swift:202 guard !release.upcomingRelease, let version = AppVersion(...)
    public void An_upcoming_release_and_a_prerelease_tag_never_count_even_when_higher()
    {
        using var runtime = new TestRuntime();
        var releases = Decode(
            Release("v9.0.0", [Link("up" + Suffix, "https://example.com/up" + Suffix)], upcomingRelease: true),
            Release("v2.0.0-beta.1", [Link("beta" + Suffix, "https://example.com/beta" + Suffix)]),
            Release("v1.4.0", [Link("stable" + Suffix, "https://example.com/stable" + Suffix)], upcomingRelease: false));
        Assert.Equal("v1.4.0", runtime.OnMain(() => UpdateChecker.NewestRelease(releases))!.Tag);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-015")] // UpdateChecker.swift:203 if let releasedAt, releasedAt > .now { continue }
    public void A_release_dated_after_now_is_skipped_and_one_dated_exactly_now_is_kept()
    {
        using var runtime = new TestRuntime(); // the manual clock stands at 2026-09-22T12:00:00Z
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero), SwiftDate.Now);
        var future = Decode(
            Release("v3.0.0", [Link("a" + Suffix, "https://example.com/a" + Suffix)], releasedAt: "2026-09-22T12:00:01Z"),
            Release("v2.9.0", [Link("b" + Suffix, "https://example.com/b" + Suffix)], releasedAt: "2026-09-22T12:00:00.001Z"),
            Release("v2.0.0", [Link("c" + Suffix, "https://example.com/c" + Suffix)], releasedAt: "2026-09-22T12:00:00Z"));
        Assert.Equal("v2.0.0", runtime.OnMain(() => UpdateChecker.NewestRelease(future))!.Tag);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-015")] // UpdateChecker.swift:199-219 returns best?.1, nil when nothing survives
    public void No_release_or_no_surviving_release_gives_nil()
    {
        using var runtime = new TestRuntime();
        Assert.Null(runtime.OnMain(() => UpdateChecker.NewestRelease(Decode())));
        var none = Decode(
            Release("v5", [Link("notes.txt", "https://example.com/notes.txt")]),
            Release("v6", []),
            Release("latest", [Link("x" + Suffix, "https://example.com/x" + Suffix)]));
        Assert.Null(runtime.OnMain(() => UpdateChecker.NewestRelease(none)));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-015")] // UpdateChecker.swift:204 first(where: isDiskImage), :205 URL(string:) or skip
    [Trait("Req", "PR-UPDATE-017")] // UpdateChecker.swift:205 directAssetURL ?? url, no fallback when it fails
    public void A_release_whose_chosen_link_text_is_not_a_URL_is_skipped_without_falling_back_to_the_other_field()
    {
        using var runtime = new TestRuntime();
        // URL(string: "") is nil (SwiftURL, R-PATH-1). The name qualifies the link, the empty direct_asset_url is
        // the one :205 parses, and the valid url is never tried, so the lower release wins.
        var releases = Decode(
            Release("v2.0.0", [Link("Marrow" + Suffix, "https://example.com/Marrow" + Suffix, directAssetUrl: "")]),
            Release("v1.0.0", [Link("Marrow" + Suffix, "https://example.com/old" + Suffix)]));
        Assert.Equal("v1.0.0", runtime.OnMain(() => UpdateChecker.NewestRelease(releases))!.Tag);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-017")] // UpdateChecker.swift:205 link.directAssetURL ?? link.url
    public void The_download_URL_is_the_direct_asset_URL_when_present_and_the_url_otherwise()
    {
        using var runtime = new TestRuntime();
        var direct = Decode(Release("v1.0.0", [Link("Marrow" + Suffix, "https://example.com/plain/Marrow" + Suffix,
            directAssetUrl: "https://example.com/direct/Marrow" + Suffix)]));
        Assert.Equal("https://example.com/direct/Marrow" + Suffix,
            runtime.OnMain(() => UpdateChecker.NewestRelease(direct))!.DownloadURL.AbsoluteString);
        var plain = Decode(Release("v1.0.0", [Link("Marrow" + Suffix, "https://example.com/plain/Marrow" + Suffix)]));
        Assert.Equal("https://example.com/plain/Marrow" + Suffix,
            runtime.OnMain(() => UpdateChecker.NewestRelease(plain))!.DownloadURL.AbsoluteString);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-015")] // UpdateChecker.swift:204 links.first(where:)
    public void The_first_qualifying_link_of_a_release_is_the_one_used()
    {
        using var runtime = new TestRuntime();
        var releases = Decode(Release("v1.0.0", [
            Link("notes.txt", "https://example.com/notes.txt"),
            Link("A" + Suffix, "https://example.com/a" + Suffix),
            Link("B" + Suffix, "https://example.com/b" + Suffix)]));
        Assert.Equal("https://example.com/a" + Suffix,
            runtime.OnMain(() => UpdateChecker.NewestRelease(releases))!.DownloadURL.AbsoluteString);
    }

    // ---- The installable-asset predicate (PR-UPDATE-016) ----

    [Fact]
    [Trait("Req", "PR-UPDATE-016")] // UpdateChecker.swift:288-291 name.lowercased().hasSuffix, independent of the URL
    public void A_link_whose_name_ends_in_the_suffix_qualifies_whatever_its_URL()
    {
        Assert.True(Qualifies(Link("Marrow-1.7.0" + Suffix, "https://example.com/download/1")));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-016")] // UpdateChecker.swift:289-290 (directAssetURL ?? url).lowercased().hasSuffix
    public void A_link_whose_url_ends_in_the_suffix_qualifies_whatever_its_name()
    {
        Assert.True(Qualifies(Link("Setup", "https://example.com/Marrow-1.7.0" + Suffix)));
        Assert.True(Qualifies(Link("Setup", "https://example.com/x.zip", directAssetUrl: "https://example.com/x" + Suffix)));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-016")] // UpdateChecker.swift:289 the URL test reads directAssetURL in preference to url
    public void The_url_test_reads_the_direct_asset_URL_when_present_so_a_qualifying_url_behind_it_does_not_count()
    {
        Assert.False(Qualifies(Link("Setup", "https://example.com/x" + Suffix, directAssetUrl: "https://example.com/x.zip")));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-016")] // UpdateChecker.swift:289-290 both sides lowercased before hasSuffix
    public void Both_tests_ignore_case()
    {
        var upper = SwiftString.Uppercased(Suffix);
        Assert.True(Qualifies(Link("MARROW" + upper, "https://example.com/1")));
        Assert.True(Qualifies(Link("Setup", "https://EXAMPLE.com/MARROW" + upper)));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-016")] // UpdateChecker.swift:289-290 the whole URL text, query included, must end in it
    public void A_url_with_a_query_after_the_suffix_does_not_qualify_and_neither_does_a_plain_name()
    {
        Assert.False(Qualifies(Link("Setup", "https://example.com/Marrow" + Suffix + "?download=1")));
        Assert.False(Qualifies(Link("Marrow" + Suffix + " (signed)", "https://example.com/1")));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-016")] // DEVIATION OD-0008 C6: the Windows artifact replaces the .dmg of :290
    public void A_macOS_disk_image_does_not_qualify_in_the_port()
    {
        // PR-UPDATE-015/016 deviation: "on Windows the qualifying asset is the Windows release artifact rather than a
        // .dmg". The suffix itself is unresolved; that a .dmg no longer qualifies is what the record fixes.
        Assert.NotEqual(".dmg", SwiftString.Lowercased(Suffix));
        Assert.False(Qualifies(Link("Droppy-Code-1.6.2.dmg", "https://example.com/Droppy-Code-1.6.2.dmg")));
    }

    /// <summary>Whether a release carrying only <paramref name="link"/> is selected (UpdateChecker.swift:204).</summary>
    private static bool Qualifies(System.Text.Json.Nodes.JsonObject link)
    {
        using var runtime = new TestRuntime();
        var releases = Decode(Release("v1.0.0", [link]));
        return runtime.OnMain(() => UpdateChecker.NewestRelease(releases)) is not null;
    }
}

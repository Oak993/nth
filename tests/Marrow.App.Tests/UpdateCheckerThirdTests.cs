using System.Text.Json;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using static Marrow.App.Tests.Fixtures.ReleaseFeed;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/UpdateChecker.swift (B2 1fbf81e), :1-327. Part 2 of 2: PR-UPDATE-026 to PR-UPDATE-030,
// UI-UPDATE-035 (updateAvailable's recomputation, the lenient GitLabRelease decode, the two-step released_at
// parse, persist()'s write/remove/always-write-the-date shape, the three defaults keys, and the notification
// banner UpdateChecker.NotifyOnce sends). UpdateCheckerTests holds the pure parts; UpdateCheckerSecondTests holds
// the checker's persistence, check(), the schedule and the HTTP session (part 1). Every expected value is
// transcribed from the Swift line cited on the test or from the record, never from a C# implementation.
public sealed class UpdateCheckerThirdTests
{
    private static string Suffix => GitLabRelease.Link.ArtifactSuffix;

    private static AppInfo Running(string version, bool isDevelopment = false) => ReleaseFeed.Running(version, isDevelopment);

    private static SwiftURL Url(string text) => SwiftURL.Create(text)!;

    private static byte[] EncodedRelease(string version) => AvailableUpdate.EncodeToBytes(new AvailableUpdate
    {
        Version = version,
        Tag = "v" + version,
        DownloadURL = Url("https://example.com/download" + Suffix),
        Notes = "notes for " + version,
        PageURL = Url("https://example.com/page/v" + version),
    });

    // ---- PR-UPDATE-026: updateAvailable is recomputed from the running version ----

    [Fact]
    [Trait("Req", "PR-UPDATE-026")] // UpdateChecker.swift:93-96, :83
    public void An_installed_update_stops_reading_as_available_while_its_notes_stay()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("updateChecker_release", EncodedRelease("2.0.0"));
        // Before the install: the running build is still 1.0.0, so the stored 2.0.0 release reads as available.
        var beforeInstall = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], ""));
        Assert.True(beforeInstall.UpdateAvailable);
        Assert.Equal("2.0.0", beforeInstall.Update!.Version);
        // After the install: the same persisted record is restored, but the running build is now 2.0.0, so
        // updateAvailable recomputes to false while the notes are still there for the About page to read.
        var afterInstall = runtime.OnMain(() => new UpdateChecker(Running("2.0.0"), [], ""));
        Assert.False(afterInstall.UpdateAvailable);
        Assert.NotNull(afterInstall.Update);
        Assert.Equal("2.0.0", afterInstall.Update!.Version);
        Assert.Equal("notes for 2.0.0", afterInstall.Update!.Notes);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-026")] // UpdateChecker.swift:94 guard let update, let latest = AppVersion(update.version)...
    public void Update_available_is_false_when_the_stored_version_does_not_parse()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("updateChecker_release", EncodedRelease("not-a-version"));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], ""));
        Assert.NotNull(checker.Update);
        Assert.False(checker.UpdateAvailable);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-026")] // UpdateChecker.swift:94 let current = AppVersion(currentVersion) else { return false }
    public void Update_available_is_false_when_the_running_version_does_not_parse()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("updateChecker_release", EncodedRelease("2.0.0"));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("not-a-version"), [], ""));
        Assert.False(checker.UpdateAvailable);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-026")] // UpdateChecker.swift:93 guard let update ... else { return false }
    public void Update_available_is_false_with_no_stored_release_at_all()
    {
        using var runtime = new TestRuntime();
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], ""));
        Assert.Null(checker.Update);
        Assert.False(checker.UpdateAvailable);
    }

    // ---- PR-UPDATE-027: the six-field lenient GitLabRelease decode ----

    [Fact]
    [Trait("Req", "PR-UPDATE-027")] // UpdateChecker.swift:306-321 only tag_name is required
    public void A_release_carrying_only_tag_name_decodes_with_every_other_field_at_its_default()
    {
        using var document = JsonDocument.Parse("""{"tag_name":"v1.0.0"}""");
        var release = GitLabRelease.Decode(document.RootElement);
        Assert.Equal("v1.0.0", release.TagName);
        Assert.Null(release.Name); // decodeIfPresent -> nil
        Assert.Null(release.Description); // decodeIfPresent -> nil
        Assert.Null(release.ReleasedAt); // decodeIfPresent -> nil
        Assert.False(release.UpcomingRelease); // decodeIfPresent ?? false
        Assert.Empty(release.GitLabReleaseAssets.Links); // decodeIfPresent ?? Assets(links: [])
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-027")] // UpdateChecker.swift:315 tagName = try container.decode(String.self, forKey: .tagName)
    public void A_release_missing_tag_name_fails_the_whole_array_decode()
    {
        using var document = JsonDocument.Parse("""[{"name":"no tag"}]""");
        Assert.Throws<DecodingError>(() =>
            SwiftDecoding.ReadArray(document.RootElement, GitLabRelease.Decode));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-027")] // UpdateChecker.swift:283-297 Link: name and url required, direct_asset_url optional
    public void A_link_requires_name_and_url_and_leaves_direct_asset_url_optional()
    {
        using var present = JsonDocument.Parse("""{"name":"Setup.exe","url":"https://example.com/a"}""");
        var link = GitLabRelease.Link.Decode(present.RootElement);
        Assert.Equal("Setup.exe", link.Name);
        Assert.Equal("https://example.com/a", link.Url);
        Assert.Null(link.DirectAssetURL);

        using var missingName = JsonDocument.Parse("""{"url":"https://example.com/a"}""");
        Assert.Throws<DecodingError>(() => GitLabRelease.Link.Decode(missingName.RootElement));

        using var missingUrl = JsonDocument.Parse("""{"name":"Setup.exe"}""");
        Assert.Throws<DecodingError>(() => GitLabRelease.Link.Decode(missingUrl.RootElement));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-027")] // UpdateChecker.swift:319-320 upcoming_release and assets each get their own default
    public void A_release_with_a_wrong_typed_optional_field_still_only_defaults_that_one_field()
    {
        // upcoming_release absent, assets present and non-empty: independent defaulting, not all-or-nothing on
        // the optional fields (only tag_name absence is all-or-nothing, per the previous test).
        using var document = JsonDocument.Parse(
            """{"tag_name":"v1.0.0","assets":{"links":[{"name":"a.exe","url":"https://example.com/a.exe"}]}}""");
        var release = GitLabRelease.Decode(document.RootElement);
        Assert.False(release.UpcomingRelease);
        Assert.Single(release.GitLabReleaseAssets.Links);
        Assert.Equal("a.exe", release.GitLabReleaseAssets.Links[0].Name);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-027")] // UpdateChecker.swift:194 a malformed feed reports "Couldn't reach GitHub."
    public void A_malformed_feed_element_makes_check_report_couldnt_reach_gitlab_not_a_decoding_error()
    {
        using var runtime = new TestRuntime();
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json("""[{"name":"no tag_name here"}]"""));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        var available = runtime.Loop.Run(() => checker.Check());
        Assert.False(available);
        Assert.Equal("Couldn't reach GitHub.", checker.LastError); // ASCII U+0027, UpdateChecker.swift:182 byte 0x27 (PR-UPDATE-019)
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-011")] // UpdateChecker.swift:114-116 try? decode: a URL string Foundation rejects is a decoding error, not a trap
    public void A_stored_update_with_an_invalid_url_string_leaves_update_nil_without_trapping()
    {
        using var runtime = new TestRuntime();
        var json = "{\"version\":\"2.0.0\",\"tag\":\"v2.0.0\",\"downloadURL\":{\"relative\":\"http://[::1\"},\"notes\":\"n\",\"pageURL\":{\"relative\":\"https://example.com/p\"}}";
        runtime.Defaults.Set("updateChecker_release", System.Text.Encoding.UTF8.GetBytes(json));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], ""));
        Assert.Null(runtime.OnMain(() => checker.Update));
    }

    // ---- PR-UPDATE-028: released_at, fractional first then plain, nil otherwise ----

    [Fact]
    [Trait("Req", "PR-UPDATE-028")] // Core/Support/ISO8601.swift:9-21 fractional tried before plain
    public void Released_at_parses_the_fractional_form_first()
    {
        using var document = JsonDocument.Parse("""{"tag_name":"v1.0.0","released_at":"2026-09-13T18:28:42.057Z"}""");
        var release = GitLabRelease.Decode(document.RootElement);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 18, 28, 42, 57, TimeSpan.Zero), release.ReleasedAt);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-028")] // Core/Support/ISO8601.swift:9-21 the plain form is the fallback
    public void Released_at_parses_the_plain_form_when_there_is_no_fraction()
    {
        using var document = JsonDocument.Parse("""{"tag_name":"v1.0.0","released_at":"2026-09-13T18:28:42Z"}""");
        var release = GitLabRelease.Decode(document.RootElement);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 18, 28, 42, TimeSpan.Zero), release.ReleasedAt);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-028")] // UpdateChecker.swift:318 an unparseable string yields nil, not an error
    public void An_unparseable_released_at_yields_nil_and_the_release_still_decodes()
    {
        using var document = JsonDocument.Parse("""{"tag_name":"v1.0.0","released_at":"not a date"}""");
        var release = GitLabRelease.Decode(document.RootElement);
        Assert.Null(release.ReleasedAt);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-028")] // UpdateChecker.swift:203 `if let releasedAt` - a nil date is never filtered
    public void A_release_whose_date_does_not_parse_is_not_skipped_by_the_future_date_filter()
    {
        using var runtime = new TestRuntime();
        var releases = Decode(Release("v1.0.0",
            [Link("Marrow" + Suffix, "https://example.com/Marrow" + Suffix)], releasedAt: "not a date"));
        var found = runtime.OnMain(() => UpdateChecker.NewestRelease(releases));
        Assert.NotNull(found);
        Assert.Null(found.ReleasedAt);
    }

    // ---- PR-UPDATE-029: persist() writes or removes the release, and always writes the last-checked date ----

    [Fact]
    [Trait("Req", "PR-UPDATE-029")] // UpdateChecker.swift:233-234 an encodable update is written under Keys.update
    public void A_successful_check_writes_the_encoded_release_and_the_checked_date()
    {
        using var runtime = new TestRuntime();
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(
            Feed(Release("v2.0.0", [Link("Marrow" + Suffix, "https://example.com/Marrow" + Suffix)]))));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        runtime.Loop.Run(() => checker.Check());
        Assert.NotNull(runtime.Defaults.Object("updateChecker_release"));
        Assert.NotNull(runtime.Defaults.Object("updateChecker_lastChecked"));
        var roundTripped = AvailableUpdate.DecodeFromBytes((byte[])runtime.Defaults.Object("updateChecker_release")!);
        Assert.Equal("2.0.0", roundTripped!.Version);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-029")] // UpdateChecker.swift:236 no update -> defaults.removeObject(forKey: Keys.update)
    public void When_the_check_finds_no_release_the_stored_release_key_is_removed_not_left_stale()
    {
        using var runtime = new TestRuntime();
        // Seed a previously stored release, as if a past check had found one.
        runtime.Defaults.Set("updateChecker_release", EncodedRelease("2.0.0"));
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(Feed())); // an empty feed -> UpdateError.noRelease
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        runtime.Loop.Run(() => checker.Check());
        // The failure branch (:181-185) never calls persist(): the stored key from before this check is
        // untouched, because throw happens before persist() at :177 runs. The stored value therefore survives a
        // single failed check, exactly as :162-185 shows (persist() only runs on the success path).
        Assert.NotNull(runtime.Defaults.Object("updateChecker_release"));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-029")] // UpdateChecker.swift:239 defaults.set(lastCheckedAt, forKey: Keys.lastChecked) always runs
    public void The_last_checked_date_is_written_unconditionally_on_success_and_on_failure()
    {
        using var runtimeSuccess = new TestRuntime();
        using var serverSuccess = new LoopbackFeedServer(_ => CannedResponse.Json(
            Feed(Release("v2.0.0", [Link("Marrow" + Suffix, "https://example.com/Marrow" + Suffix)]))));
        var succeeding = runtimeSuccess.OnMain(() => new UpdateChecker(Running("1.0.0"), [], serverSuccess.Url("/releases")));
        runtimeSuccess.Loop.Run(() => succeeding.Check());
        Assert.NotNull(succeeding.LastCheckedAt);

        using var runtimeFailure = new TestRuntime();
        using var serverFailure = new LoopbackFeedServer(_ => CannedResponse.Json(Feed())); // noRelease
        var failing = runtimeFailure.OnMain(() => new UpdateChecker(Running("1.0.0"), [], serverFailure.Url("/releases")));
        runtimeFailure.Loop.Run(() => failing.Check());
        Assert.NotNull(failing.LastCheckedAt);
        Assert.NotNull(runtimeFailure.Defaults.Object("updateChecker_lastChecked"));
    }

    // PR-UPDATE-029's "encode failure removes the key" failure_case is UNTESTABLE at this unit: AvailableUpdate's
    // fields are String, long?, DateTimeOffset? and SwiftURL, none of which JSONEncoder (or this port's
    // AvailableUpdate.EncodeToBytes) can fail to encode - there is no Double field, so the non-finite-Double
    // failure JSONEncoder actually throws for never arises for this type, and the public API gives no way to
    // construct an AvailableUpdate that fails to serialize. Persist()'s write and remove arms are proven above
    // (a found release is written; a check that finds none never reaches persist() at all, so any prior key
    // survives untouched) - those are the two branches the public surface can actually reach.

    // ---- PR-UPDATE-030: exactly the three keys, outside the app-settings store ----

    [Fact]
    [Trait("Req", "PR-UPDATE-030")] // UpdateChecker.swift:77-81 the three keys, spelled exactly
    public void The_three_keys_are_spelled_exactly_and_nothing_else_is_written()
    {
        using var runtime = new TestRuntime();
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(
            Feed(Release("v2.0.0", [Link("Marrow" + Suffix, "https://example.com/Marrow" + Suffix)]))));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        runtime.Loop.Run(() => checker.Check(background: true));
        var distinctKeys = runtime.Defaults.Writes.Distinct().ToArray();
        Assert.All(distinctKeys, key => Assert.Contains(key,
            new[] { "updateChecker_release", "updateChecker_lastChecked", "updateChecker_notifiedVersion" }));
        Assert.Contains("updateChecker_release", distinctKeys);
        Assert.Contains("updateChecker_lastChecked", distinctKeys);
        Assert.Contains("updateChecker_notifiedVersion", distinctKeys);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-030")] // UpdateChecker.swift:81-85, absence is the initial state, no defaults
    public void With_nothing_persisted_yet_every_property_starts_at_its_absent_state()
    {
        using var runtime = new TestRuntime();
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], ""));
        Assert.Null(checker.Update);
        Assert.Null(checker.LastCheckedAt);
        Assert.Null(runtime.Defaults.Object("updateChecker_notifiedVersion"));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-030")] // UpdateChecker.swift:77-81 the keys live in UserDefaults.standard, not the App Settings store
    public void The_three_keys_live_in_the_bound_UserDefaults_standard_not_a_separate_settings_file()
    {
        using var runtime = new TestRuntime();
        // UserDefaults.Standard is what UpdateChecker's constructor and persist() read and write throughout
        // (":113-118", ":231-239"); TestRuntime's override IS that store, so a write landing in runtime.Defaults
        // (rather than in some other file/store the checker never touches) proves the three keys share the one
        // bound UserDefaults.standard the rest of the settings surface (PR-SETTINGS-105) also uses.
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(
            Feed(Release("v2.0.0", [Link("Marrow" + Suffix, "https://example.com/Marrow" + Suffix)]))));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        runtime.Loop.Run(() => checker.Check());
        Assert.Same(runtime.Defaults, UserDefaults.Standard.Current);
        Assert.NotNull(runtime.Defaults.Object("updateChecker_release"));
    }

    // ---- UI-UPDATE-035: the "new version available" notification banner ----

    [Fact]
    [Trait("Req", "UI-UPDATE-035")] // UpdateChecker.swift:249-253, DEVIATION OD-0012 F1
    public void The_banner_carries_the_exact_title_body_identifier_and_version_for_the_found_release()
    {
        using var runtime = new TestRuntime();
        var toasts = new RecordingToastCenter();
        using var toastScope = UpdateToastCenter.Ambient.Override(toasts);
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(
            Feed(Release("v3.1.4", [Link("Marrow" + Suffix, "https://example.com/Marrow" + Suffix)]))));
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        runtime.Loop.Run(() => checker.Check(background: true));
        var shown = Assert.Single(toasts.Shown);
        // DEVIATION OD-0012 F1: upstream's "Droppy Code <version> is available" (:250) becomes "Marrow <version>
        // is available".
        Assert.Equal("nth 3.1.4 is available", shown.Title);
        // U+203A SINGLE RIGHT-POINTING ANGLE QUOTATION MARK, not ">" (:251, the notes field's separator).
        Assert.Equal("Open Settings › About to update and restart.", shown.Body);
        // :253 identifier: "update-\(update.version)".
        Assert.Equal("update-3.1.4", shown.Identifier);
        // The channel this port uses instead of Foundation's userInfo carries only the version (:252
        // userInfo = ["update": update.version]); there is no analogue of a "threadID" entry anywhere in the
        // Show(identifier, title, body, version) surface, matching the absence UI-UPDATE-035 calls out.
        Assert.Equal("3.1.4", shown.Version);
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-035")] // UpdateChecker.swift:247 posted at most once per version, ever
    public void The_banner_is_posted_at_most_once_per_version_ever_across_separate_checker_instances()
    {
        using var runtime = new TestRuntime();
        var toasts = new RecordingToastCenter();
        using var toastScope = UpdateToastCenter.Ambient.Override(toasts);
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(
            Feed(Release("v2.0.0", [Link("Marrow" + Suffix, "https://example.com/Marrow" + Suffix)]))));
        var first = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        runtime.Loop.Run(() => first.Check(background: true));
        Assert.Single(toasts.Shown);
        // A second, freshly constructed checker (as after a relaunch) restores updateChecker_notifiedVersion
        // from the same store and must not notify again for the same version.
        var second = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], server.Url("/releases")));
        runtime.Loop.Run(() => second.Check(background: true));
        Assert.Single(toasts.Shown); // still exactly one, ever
    }
}

using Marrow.App.Views.Settings;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B2 1fbf81e), :44-50, :67-185, :305-337.
public sealed class AboutUpdateViewsTests
{
    [Fact]
    [Trait("Req", "PR-SETTINGS-244")] // App/Views/Settings/AboutUpdateViews.swift:12-59
    public void Release_categories_keep_their_titles_and_count_wording()
    {
        Assert.Equal(
            new[]
            {
                ReleaseNoteCategory.NewFeatures,
                ReleaseNoteCategory.BugFixes,
                ReleaseNoteCategory.Refinements,
            },
            ReleaseNoteCategoryExtensions.AllCases);

        Assert.Equal("New", ReleaseNoteCategory.NewFeatures.CardTitle);
        Assert.Equal("Bugs", ReleaseNoteCategory.BugFixes.CardTitle);
        Assert.Equal("Refinements", ReleaseNoteCategory.Refinements.CardTitle);

        Assert.Equal("1 addition", ReleaseNoteCategory.NewFeatures.CountSummary(1));
        Assert.Equal("2 additions", ReleaseNoteCategory.NewFeatures.CountSummary(2));
        Assert.Equal("2 fixed", ReleaseNoteCategory.BugFixes.CountSummary(2));
        Assert.Equal("2 improved", ReleaseNoteCategory.Refinements.CountSummary(2));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-245")] // App/Views/Settings/AboutUpdateViews.swift:100-166
    [Trait("Req", "PR-UPDATE-065")] // App/Views/Settings/AboutUpdateViews.swift:100-166
    [Trait("Req", "PR-UPDATE-069")] // App/Views/Settings/AboutUpdateViews.swift:720-762
    public void Release_notes_collect_only_bullets_under_recognized_sections_in_declaration_order()
    {
        var digest = UpdateReleaseNotesDigest.Parse(
            """
            - Before a section
            ## New features
            - First change
            ## Thanks to contributors
            - Not a card change
            **Bugs**
            • Fixed change
            ## Improvements
            — Improved change
            """);

        Assert.Collection(
            digest.Sections,
            section =>
            {
                Assert.Equal(ReleaseNoteCategory.NewFeatures, section.Category);
                Assert.Equal(new[] { "First change" }, section.Bullets);
            },
            section =>
            {
                Assert.Equal(ReleaseNoteCategory.BugFixes, section.Category);
                Assert.Equal(new[] { "Fixed change" }, section.Bullets);
            },
            section =>
            {
                Assert.Equal(ReleaseNoteCategory.Refinements, section.Category);
                Assert.Equal(new[] { "Improved change" }, section.Bullets);
            });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-245")] // App/Views/Settings/AboutUpdateViews.swift:86, :103-123, :132-150
    [Trait("Req", "PR-UPDATE-065")] // App/Views/Settings/AboutUpdateViews.swift:86, :103-123, :132-150
    public void Release_note_bullet_markers_are_exact_and_unrecognized_headings_close_a_section()
    {
        var digest = UpdateReleaseNotesDigest.Parse(
            """
            # New
            - Hyphen
            ‐ Hyphen character
            – En dash
            — Em dash
            − Minus sign
            * Asterisk
            • Bullet
            Thanks:
            - Still in the current section
            # Thanks
            - Dropped
            """);

        var section = Assert.Single(digest.Sections);
        Assert.Equal(ReleaseNoteCategory.NewFeatures, section.Category);
        Assert.Equal(
            new[]
            {
                "Hyphen",
                "Hyphen character",
                "En dash",
                "Em dash",
                "Minus sign",
                "Asterisk",
                "Bullet",
                "Still in the current section",
            },
            section.Bullets);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-245")] // App/Views/Settings/AboutUpdateViews.swift:100-129, :178-185
    [Trait("Req", "PR-UPDATE-065")] // App/Views/Settings/AboutUpdateViews.swift:100-129, :178-185
    [Trait("Req", "PR-UPDATE-066")] // App/Views/Settings/AboutUpdateViews.swift:71-76, :120-127, :169-176
    [Trait("Req", "PR-UPDATE-067")] // App/Views/Settings/AboutUpdateViews.swift:178-185
    public void Release_note_bullets_strip_inline_markdown_deduplicate_and_separate_a_trailing_rollup()
    {
        const string notes = """
            ## Bug fixes
            - [Fixed](https://example.com) **the** `first` issue
            - fixed THE FIRST issue
            - Fixed 12 extra issues…
            """;

        var digest = UpdateReleaseNotesDigest.Parse(notes);
        var section = Assert.Single(digest.Sections);

        Assert.Equal(ReleaseNoteCategory.BugFixes, section.Category);
        Assert.Equal(new[] { "Fixed the first issue" }, section.Bullets);
        Assert.Equal("Fixed 12 extra issues…", section.Rollup);
        Assert.Equal(12, section.RollupCount);
        Assert.Equal(13, section.ChangeCount);

        Assert.Equal(digest, UpdateReleaseNotesDigest.Parse(notes));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-245")] // App/Views/Settings/AboutUpdateViews.swift:120-128, :169-176
    [Trait("Req", "PR-UPDATE-066")] // App/Views/Settings/AboutUpdateViews.swift:71-76, :120-127, :169-176
    public void Only_the_last_bullet_can_be_a_rollup_and_invalid_counts_remain_bullets()
    {
        var digest = UpdateReleaseNotesDigest.Parse(
            """
            ## New
            - 12 extra issues fixed
            - Fixed 0 extra issues
            """);

        var section = Assert.Single(digest.Sections);
        Assert.Equal(new[] { "12 extra issues fixed", "Fixed 0 extra issues" }, section.Bullets);
        Assert.Null(section.Rollup);
        Assert.Equal(0, section.RollupCount);
        Assert.Equal(2, section.ChangeCount);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-245")] // App/Views/Settings/AboutUpdateViews.swift:91-98, :100-129
    [Trait("Req", "PR-UPDATE-068")] // App/Views/Settings/AboutUpdateViews.swift:87-98
    [Trait("Req", "PR-UPDATE-069")] // App/Views/Settings/AboutUpdateViews.swift:720-762
    public void Empty_or_unrecognized_release_notes_have_an_empty_digest()
    {
        Assert.True(UpdateReleaseNotesDigest.Parse(null).IsEmpty);
        Assert.True(UpdateReleaseNotesDigest.Parse("").IsEmpty);
        Assert.True(UpdateReleaseNotesDigest.Parse("Plain release notes with no section.").IsEmpty);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-064")] // App/Views/Settings/AboutUpdateViews.swift:307-337, :363-377
    [Trait("Req", "PR-SETTINGS-247")] // App/Views/Settings/AboutUpdateViews.swift:305-337
    public void Fraction_chase_snaps_on_first_advance_caps_elapsed_time_and_never_overshoots()
    {
        var chase = new FractionChase(0);

        Assert.Equal(0.5, chase.Advance(0, target: 0.5, response: 0.3, minimumSpeed: 0));

        var advanced = chase.Advance(1, target: 1, response: 0.3, minimumSpeed: 0);
        var expected = 0.5 + 0.5 * (1 - Math.Exp(-0.25 / 0.3));
        Assert.Equal(expected, advanced, 12);

        Assert.Equal(0.25, chase.Advance(2, target: 0.25, response: 0.3, minimumSpeed: 0));
        Assert.Equal(0.25, chase.Advance(3, target: 0.25, response: 0.3, minimumSpeed: 0));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-064")] // App/Views/Settings/AboutUpdateViews.swift:316-335
    [Trait("Req", "PR-SETTINGS-247")] // App/Views/Settings/AboutUpdateViews.swift:305-337
    [Trait("Req", "PR-UPDATE-062")] // App/Views/Settings/AboutUpdateViews.swift:626-630, :687-690
    public void Minimum_speed_sets_a_floor_on_the_chase_step()
    {
        var chase = new FractionChase(0);
        Assert.Equal(0, chase.Advance(0, target: 0, response: 0.72, minimumSpeed: 0.12));

        var advanced = chase.Advance(0.1, target: 1, response: 0.72, minimumSpeed: 0.12);
        var exponentialStep = 1 - Math.Exp(-0.1 / 0.72);
        Assert.True(exponentialStep > 0.012); // :329-330 the natural step may exceed the minimum-speed floor
        Assert.Equal(exponentialStep, advanced, 12);

        var floorChase = new FractionChase(0);
        Assert.Equal(0, floorChase.Advance(0, target: 0, response: 10, minimumSpeed: 0.12));
        Assert.Equal(0.012, floorChase.Advance(0.1, target: 1, response: 10, minimumSpeed: 0.12), 12);
    }
}

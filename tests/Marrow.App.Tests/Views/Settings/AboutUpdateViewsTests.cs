using Marrow.App.Views.Settings;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc7926), :44-50, :67-185, :305-337.
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

    // The slider's fraction chase (AboutUpdateViews.swift:305-337) is gone: the install capsule springs to the fraction on the render thread (motion pack step 3 spring), proven in NthSitesBTests.

    private static AvailableUpdate Update(long? size = null, System.DateTimeOffset? releasedAt = null) => new()
    {
        Version = "1.7.0",
        Tag = "v1.7.0",
        DownloadURL = Marrow.Core.Port.SwiftURL.Create("https://example.com/download")!,
        Size = size,
        Notes = "",
        ReleasedAt = releasedAt,
        PageURL = Marrow.Core.Port.SwiftURL.Create("https://example.com/release")!,
    };

    private static string ReleaseDetail(AvailableUpdate update)
    {
        using var locale = Marrow.Core.Port.SwiftFormatters.CurrentLocale.Override("en_US");
        var method = typeof(AboutSoftwareUpdateSection)
            .GetMethod("ReleaseDetail", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string)method.Invoke(null, [update])!;
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-077")] // App/Views/Settings/AboutUpdateViews.swift:781-791
    [Trait("Decision", "ED-198")]
    public void Release_detail_uses_the_file_style_byte_count_not_the_KB_ByteCountFormatter_one()
    {
        // 1024 bytes -> "1 kB" under ByteCountFormatStyle(style: .file) (SwiftFormattersThirdTests' own oracle
        // fixture); the pre-B3 FileByteCount ByteCountFormatter rule gave "1 KB" (capital K) instead.
        Assert.Equal(
            "1 kB \U000000B7 Update & restart installs it in place and relaunches.", // U+00B7 MIDDLE DOT
            ReleaseDetail(Update(size: 1024L)));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-077")] // App/Views/Settings/AboutUpdateViews.swift:781-791
    public void Release_detail_omits_the_size_clause_when_the_update_has_no_size()
    {
        Assert.Equal(
            "Update & restart installs it in place and relaunches.",
            ReleaseDetail(Update()));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-077")] // App/Views/Settings/AboutUpdateViews.swift:781-791
    public void Release_detail_joins_date_size_and_the_closing_sentence_with_a_middle_dot()
    {
        var releasedAt = new System.DateTimeOffset(2026, 3, 1, 0, 0, 0, System.TimeSpan.Zero);
        var detail = ReleaseDetail(Update(size: 1024L, releasedAt: releasedAt));

        Assert.StartsWith("Released ", detail, System.StringComparison.Ordinal);
        Assert.Contains(" \U000000B7 1 kB \U000000B7 ", detail, System.StringComparison.Ordinal); // U+00B7 MIDDLE DOT
        Assert.EndsWith("Update & restart installs it in place and relaunches.", detail, System.StringComparison.Ordinal);
    }
}

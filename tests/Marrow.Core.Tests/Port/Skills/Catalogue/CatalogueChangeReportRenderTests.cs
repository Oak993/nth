using System.Globalization;
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-2: the report as the owner reads it (ONDEMAND-PLAN.md 3.7: "The catalogue builder writes a change report (versions, hashes, licence changes, sizes) for the
// owner"; 2.1: "a pin changes only with the owner's approval of its change report"). Markdown, line feeds, ends with one, the same text for the same two catalogues.
[Trait("Spec", "DL-2")]
public sealed class CatalogueChangeReportRenderTests
{
    private static string Render(CatalogueChangeReport report, ChangeReportRenderOptions? options = null) => report.ToMarkdown(options);

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_report_with_no_change_says_so_and_has_the_fixed_headings()
    {
        var text = Render(CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), ChangeReportFixtures.Previous()));
        Assert.Equal(
            "# Catalogue change report\n\n"
            + "Previous catalogue read on 2026-10-04 (3 items); new catalogue read on 2026-10-04 (3 items).\n\n"
            + "## Summary\n\n"
            + "- Items added: 0\n- Items removed: 0\n- Items changed: 0\n- Items unchanged: 3\n- Changes: none\n- Asking the terms again: none\n\n"
            + "## Needs the owner's attention\n\n- None.\n\n"
            + "## Changes by item\n\n- None.\n", text);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_summary_counts_items_and_changes_by_kind_and_names_the_ones_that_ask_the_terms_again()
    {
        var previous = ChangeReportFixtures.Mutate(ChangeReportFixtures.WithLicencedX3(ChangeReportFixtures.Previous(), "old"), "X1", i => i);
        var next = ChangeReportFixtures.Mutate(ChangeReportFixtures.WithLicencedX3(ChangeReportFixtures.Previous(), "new"), "X1", i => ChangeReportFixtures.ChangeArtifact(i with { Pin = "1.1.0" }, "tool.zip", a => a with { Sha256 = CatalogueFixtures.Sha("t2"), Size = 2048 }));
        next = CatalogueFixtures.Add(next, CatalogueFixtures.PinnedTool("X9", "other"));
        var text = Render(CatalogueChangeReport.Compare(previous, next));
        Assert.Contains("- Items added: 1 (X9)\n", text, StringComparison.Ordinal);
        Assert.Contains("- Items removed: 0\n", text, StringComparison.Ordinal);
        Assert.Contains("- Items changed: 2 (X1, X3)\n", text, StringComparison.Ordinal);
        Assert.Contains("- Items unchanged: 1\n", text, StringComparison.Ordinal);
        Assert.Contains("- Changes: item 1, version 1, hash 2, size 1, licence 1\n", text, StringComparison.Ordinal);
        Assert.Contains("- Asking the terms again: X3\n", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_attention_section_gives_each_flag_in_the_plain_words_with_its_reason()
    {
        var previous = ChangeReportFixtures.WithLicencedX3(ChangeReportFixtures.Previous(), "old terms");
        var next = ChangeReportFixtures.Mutate(ChangeReportFixtures.WithLicencedX3(ChangeReportFixtures.Previous(), "new terms"), "X1", i => i with { TermsClass = TermsClass.Proprietary });
        var text = Render(CatalogueChangeReport.Compare(previous, next));
        var section = text[(text.IndexOf("## Needs the owner's attention", StringComparison.Ordinal))..text.IndexOf("## Changes by item", StringComparison.Ordinal)];
        Assert.Equal(
            "## Needs the owner's attention\n\n"
            + "- X1: terms now required (was open, now proprietary)\n"
            + "- X1: licence text not recorded (the catalogue holds no hash of its licence file, so its terms cannot be accepted yet)\n"
            + $"- X3: asks the terms again (the licence text changed: {CatalogueFixtures.Sha("old terms")[..12]} -> {CatalogueFixtures.Sha("new terms")[..12]})\n\n", section);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void An_item_block_has_its_title_kind_download_size_and_one_line_per_change_in_category_order()
    {
        var next = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X1", i => ChangeReportFixtures.ChangeArtifact(i with { Pin = "1.1.0" }, "tool.zip", a => a with { Sha256 = CatalogueFixtures.Sha("t2"), Size = 4096 }));
        var text = Render(CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), next));
        var block = text[(text.IndexOf("### X1", StringComparison.Ordinal))..];
        Assert.Equal(
            "### X1 - Fixture tool (tool)\n\n"
            + "Download: 1,024 B -> 4,096 B (+3,072 B)\n\n"
            + "- version: pin: 1.0.0 -> 1.1.0\n"
            + $"- hash: artifacts[tool.zip].sha256: {CatalogueFixtures.Sha("tool")[..12]} -> {CatalogueFixtures.Sha("t2")[..12]}\n"
            + "- size: artifacts[tool.zip].size: 1,024 -> 4,096\n", block);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_download_size_that_shrinks_or_is_unknown_on_one_side_is_written_plainly_and_no_line_appears_when_neither_side_has_one()
    {
        var smaller = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X1", i => ChangeReportFixtures.ChangeArtifact(i, "tool.zip", a => a with { Size = 24 }));
        Assert.Contains("Download: 1,024 B -> 24 B (-1,000 B)\n", Render(CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), smaller)), StringComparison.Ordinal);
        var unknown = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X1", i => ChangeReportFixtures.ChangeArtifact(i, "tool.zip", a => a with { Size = null }));
        Assert.Contains("Download: 1,024 B -> unknown\n", Render(CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), unknown)), StringComparison.Ordinal);
        Assert.Contains("Download: unknown -> 1,024 B\n", Render(CatalogueChangeReport.Compare(unknown, ChangeReportFixtures.Previous())), StringComparison.Ordinal);
        var textOnly = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X1", i => i with { Title = "Other" });
        Assert.DoesNotContain("Download:", Render(CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), textOnly)), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void An_added_and_a_removed_item_have_their_own_blocks()
    {
        var previous = ChangeReportFixtures.Previous();
        var next = CatalogueFixtures.Add(CatalogueFixtures.Without(previous, "X2"), CatalogueFixtures.PinnedTool("X9", "other"));
        var text = Render(CatalogueChangeReport.Compare(previous, next));
        Assert.Contains("### X2 - Fixture set (pyset)\n\n- item: removed (was pyset)\n", text, StringComparison.Ordinal);
        Assert.Contains("### X9 - Fixture tool (tool)\n\n- item: added (tool)\n", text, StringComparison.Ordinal);
        // The download size is shown for an item that is in both catalogues; the data of an added item still carries it.
        var added = CatalogueChangeReport.Compare(previous, next).Items.Single(i => i.ItemId == "X9");
        Assert.Null(added.DownloadBytesBefore);
        Assert.Equal(1024L, added.DownloadBytesAfter);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_changed_file_is_listed_unless_the_owner_asks_for_a_shorter_report()
    {
        var files = Enumerable.Range(0, 40).Select(n => new CatalogueFile { Path = $"skills/s/f{n:D2}.md", Size = 1, Sha256 = CatalogueFixtures.Sha("a" + n) }).ToList();
        var previous = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X3", i => i with { Files = files });
        var next = ChangeReportFixtures.Mutate(previous, "X3", i => i with { Files = files.Select(f => f with { Sha256 = CatalogueFixtures.Sha("b" + f.Path) }).ToList() });
        var report = CatalogueChangeReport.Compare(previous, next);
        var full = Render(report);
        Assert.Equal(40, full.Split('\n').Count(l => l.StartsWith("- hash: files[", StringComparison.Ordinal)));
        Assert.DoesNotContain("not shown", full, StringComparison.Ordinal);
        var short5 = Render(report, new ChangeReportRenderOptions { MaxFilesPerItem = 5 });
        Assert.Equal(5, short5.Split('\n').Count(l => l.StartsWith("- hash: files[", StringComparison.Ordinal)));
        Assert.Contains("- hash: and 35 more file changes (not shown; the full list is in the report data)\n", short5, StringComparison.Ordinal);
        Assert.Equal(40, report.ChangesFor("X3").Count(c => c.Field.StartsWith("files[", StringComparison.Ordinal)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Render(report, new ChangeReportRenderOptions { MaxFilesPerItem = -1 }));
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_note_about_files_left_out_comes_after_the_last_file_shown_and_only_when_a_file_is_left_out()
    {
        var files = Enumerable.Range(0, 3).Select(n => new CatalogueFile { Path = $"skills/s/f{n}.md", Size = 1, Sha256 = CatalogueFixtures.Sha("a" + n) }).ToList();
        var previous = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X3", i => i with { Files = files });
        var next = ChangeReportFixtures.Mutate(previous, "X3", i => i with { Files = files.Select(f => f with { Sha256 = CatalogueFixtures.Sha("b" + f.Path) }).ToList() });
        var report = CatalogueChangeReport.Compare(previous, next);
        var lines = Render(report, new ChangeReportRenderOptions { MaxFilesPerItem = 2 }).Split('\n');
        var note = Array.FindIndex(lines, l => l.Contains("more file changes", StringComparison.Ordinal));
        Assert.Equal("- hash: and 1 more file changes (not shown; the full list is in the report data)", lines[note]);
        Assert.StartsWith("- hash: files[skills/s/f1.md]", lines[note - 1], StringComparison.Ordinal);
        Assert.Equal(2, lines.Count(l => l.StartsWith("- hash: files[", StringComparison.Ordinal)));
        foreach (var limit in new[] { 3, 4 })
            Assert.DoesNotContain("more file changes", Render(report, new ChangeReportRenderOptions { MaxFilesPerItem = limit }), StringComparison.Ordinal);
        var none = Render(report, new ChangeReportRenderOptions { MaxFilesPerItem = 0 });
        Assert.Contains("- hash: and 3 more file changes", none, StringComparison.Ordinal);
        Assert.DoesNotContain("- hash: files[", none, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_text_is_the_same_each_time_uses_line_feeds_and_ends_with_one_whatever_the_culture()
    {
        var next = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X1", i => ChangeReportFixtures.ChangeArtifact(i with { ApproxSizeMb = 1.5 }, "tool.zip", a => a with { Size = 1_234_567 }));
        var report = CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), next);
        var saved = CultureInfo.CurrentCulture;
        try
        {
            var invariant = Render(report);
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(invariant, Render(report));
            Assert.Equal(invariant, Render(CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), next)));
            Assert.DoesNotContain('\r', invariant);
            Assert.EndsWith("\n", invariant, StringComparison.Ordinal);
            Assert.False(invariant.EndsWith("\n\n", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_value_with_a_line_break_cannot_split_a_line_of_the_report()
    {
        var next = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X1", i => i with { LicenceNotes = "line one\nline two\r\n- fake: item" });
        var text = Render(CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), next));
        Assert.Contains("- text: licenceNotes: (none) -> line one line two - fake: item\n", text, StringComparison.Ordinal);
    }
}

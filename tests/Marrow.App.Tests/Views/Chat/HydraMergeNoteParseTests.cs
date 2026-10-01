using System.Linq;
using Marrow.App.Views.Chat;
using Xunit;
using static Marrow.App.Tests.Fixtures.HydraMergeCardProbe;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraMergePopover.swift (B3 52bc792), :25-75 HydraMergeNote.parse.
// Pure parsing: no window, no clock. The Swift regexes are matched whole (wholeMatch) against each trimmed line.
public sealed class HydraMergeNoteParseTests
{
    private static HydraMergeNote? TryParse(string body) => HydraMergeNote.Parse(body, Link());

    // ------------------------------------------------------------------ PR-HYDRAMERGE-091 the files line

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-091")] // App/Views/Chat/HydraMergePopover.swift:58-65, :70
    public void The_files_line_supplies_files_additions_deletions_target_and_branch()
    {
        var note = Parse(FilesLine(files: 12, additions: 340, deletions: 56, target: "main", branch: "hydra/fix-the-parser-a1b2c3"));

        Assert.Equal(12, note.Files);
        Assert.Equal(340, note.Additions);
        Assert.Equal(56, note.Deletions);
        Assert.Equal("main", note.Target);
        Assert.Equal("hydra/fix-the-parser-a1b2c3", note.Branch);
        Assert.Empty(note.Notes);
        Assert.Empty(note.Heads);
        Assert.Equal(7, note.Link.Number);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-091")] // :60 `files?`: the singular "1 file" matches too
    public void A_single_file_line_matches_the_singular_form()
    {
        var note = Parse("1 file (+1 " + Minus + "0) landed on `main` from `b`.");

        Assert.Equal(1, note.Files);
        Assert.Equal(1, note.Additions);
        Assert.Equal(0, note.Deletions);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-091")] // :60 the minus is U+2212 only; :70 no match means nil (the caller keeps the generic popover)
    public void An_ascii_hyphen_or_another_dash_in_place_of_the_minus_sign_makes_the_body_no_merge_note()
    {
        Assert.Null(TryParse("2 files (+3 -1) landed on `main` from `b`."));
        Assert.Null(TryParse("2 files (+3 \U00002013" + "1) landed on `main` from `b`.")); // U+2013 EN DASH
        Assert.Null(TryParse("2 files (+3 \U00002012" + "1) landed on `main` from `b`.")); // U+2012 FIGURE DASH
        Assert.NotNull(TryParse(FilesLine()));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-091")] // :70
    public void A_body_without_a_files_line_is_not_a_merge_note()
    {
        Assert.Null(TryParse(""));
        Assert.Null(TryParse("Checkout of `main` was left as it was.\nHeads\n" + HeadLine("Hank", 1, "Fix", "a.cs")));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-091")] // :60 wholeMatch: the trimmed line must match end to end
    public void The_files_line_must_match_the_whole_trimmed_line()
    {
        Assert.Null(TryParse(FilesLine() + " Extra words."));
        Assert.Null(TryParse("Note: " + FilesLine()));
        Assert.Null(TryParse("2 files (+3 " + Minus + "1) landed on `main` from `b`")); // no final period
        Assert.Null(TryParse("2 files (+3 " + Minus + "1) landed on `` from `b`.")); // empty target: [^`]+
        // Surrounding whitespace is trimmed first (:31), so it does not stop the match.
        Assert.NotNull(TryParse("   " + FilesLine() + "  \t"));
        Assert.NotNull(TryParse(FilesLine() + "\r\n"));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-091")] // :60 `match == nil`: only the first matching line supplies the fields
    public void Only_the_first_matching_files_line_supplies_the_fields_and_a_later_one_is_a_remark()
    {
        var note = Parse(
            FilesLine(files: 2, additions: 3, deletions: 1, target: "main", branch: "first") + "\n" +
            FilesLine(files: 9, additions: 9, deletions: 9, target: "dev", branch: "second"));

        Assert.Equal(2, note.Files);
        Assert.Equal(3, note.Additions);
        Assert.Equal(1, note.Deletions);
        Assert.Equal("main", note.Target);
        Assert.Equal("first", note.Branch);
        // The second line fails `match == nil`, so it falls through to the remark branch (:66-69), backticks stripped.
        Assert.Equal(["9 files (+9 " + Minus + "9) landed on dev from second."], note.Notes);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-091")] // :61-62 a numeric group that does not fit Int skips the line and matching continues
    public void A_files_line_whose_numbers_overflow_is_skipped_and_matching_continues()
    {
        var overflow = "99999999999999999999 files (+1 " + Minus + "1) landed on `x` from `y`.";
        var note = Parse(overflow + "\n" + FilesLine(files: 4, additions: 5, deletions: 6, target: "main", branch: "ok"));

        Assert.Equal(4, note.Files);
        Assert.Equal("ok", note.Branch);
        Assert.Empty(note.Notes); // the skipped line is neither the match nor a remark

        Assert.Null(TryParse(overflow)); // with nothing else to match, it is not a merge note
        Assert.Null(TryParse("1 file (+99999999999999999999 " + Minus + "1) landed on `x` from `y`."));
        Assert.Null(TryParse("1 file (+1 " + Minus + "99999999999999999999) landed on `x` from `y`."));
    }

    // ------------------------------------------------------------------ PR-HYDRAMERGE-092 the heads section

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-092")] // :44-51 no files, one file, many files; name, index and task from groups 1-3
    public void Head_lines_read_a_no_files_head_a_one_file_head_and_a_many_file_head()
    {
        var note = Parse(string.Join("\n",
            FilesLine(), "Heads",
            HeadLine("Hank", 1, "Fix the parser"),
            HeadLine("Hank 2", 11, "Tidy", "a/b.cs"),
            HeadLine("Dale", 3, "Wire it up", "x.cs", "y/z.cs", "w.md")));

        Assert.Equal(3, note.Heads.Count);

        var none = note.Heads[0];
        Assert.Equal("Hank", none.Name);
        Assert.Equal(1, none.Index);
        Assert.Equal("Fix the parser", none.Task);
        Assert.Empty(none.Files);

        var one = note.Heads[1];
        Assert.Equal("Hank 2", one.Name); // the non-greedy name group still takes a space-bearing name
        Assert.Equal(11, one.Index);
        Assert.Equal("Tidy", one.Task);
        Assert.Equal(["a/b.cs"], one.Files);

        var many = note.Heads[2];
        Assert.Equal("Dale", many.Name);
        Assert.Equal(3, many.Index);
        Assert.Equal(["x.cs", "y/z.cs", "w.md"], many.Files);

        Assert.Empty(note.Notes); // "Heads" and every head line are consumed, none is a remark
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-092")] // :17-23 Head.id is "<index>-<name>"
    public void A_head_is_identified_by_its_index_and_name()
    {
        var note = Parse(FilesLine() + "\nHeads\n" + HeadLine("Hank 2", 11, "Tidy"));

        Assert.Equal("11-Hank 2", note.Heads[0].Id);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-092")] // :50-51 files split on ", ", only when group 4 matched and group 5 is non-empty
    public void A_heads_file_list_is_split_on_comma_space_only_and_keeps_every_entry()
    {
        var note = Parse(FilesLine() + "\nHeads\n" +
            "- Hank (1): t " + EmDash + " 3 files: a.cs,b.cs, c d.cs, a.cs");

        Assert.Equal(["a.cs,b.cs", "c d.cs", "a.cs"], note.Heads[0].Files); // no trim, no de-duplication
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-092")] // :44 the separator is U+2014 EM DASH with single spaces, and the task stops at the first dash that lets the line match
    public void The_task_is_cut_at_the_first_em_dash_that_leaves_a_valid_tail()
    {
        var note = Parse(FilesLine() + "\nHeads\n" +
            "- Hank (1): Fix " + EmDash + " 1 file: a " + EmDash + " b\n" +
            "- Dale (2): Fix " + EmDash + " properly " + EmDash + " no files");

        Assert.Equal("Fix", note.Heads[0].Task);
        Assert.Equal(["a " + EmDash + " b"], note.Heads[0].Files); // the rest of the line is the file list
        Assert.Equal("Fix " + EmDash + " properly", note.Heads[1].Task); // the first dash leaves no valid tail, so the task runs on
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-092")] // :44 (.*?) is lazy: of several valid splits, the one with the shortest task wins
    public void The_task_group_is_lazy_so_the_first_valid_em_dash_split_wins()
    {
        var note = Parse(FilesLine() + "\nHeads\n" +
            "- Hank (1): A " + EmDash + " 1 file: x " + EmDash + " 2 files: y");

        Assert.Equal("A", note.Heads[0].Task);
        Assert.Equal(["x " + EmDash + " 2 files: y"], note.Heads[0].Files);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-092")] // :44 a head line with a hyphen or en dash for the separator does not match; it is dropped and the section continues
    public void A_head_line_that_does_not_match_is_dropped_and_the_section_goes_on()
    {
        var note = Parse(string.Join("\n",
            FilesLine(), "Heads",
            "- Hank (1): t - 1 file: a.cs", // ASCII hyphen
            "- Dale (2): t \U00002013 no files", // EN DASH
            "- no index here " + EmDash + " no files",
            "- Gail (4): t " + EmDash + " no files",
            "- Ed (x): t " + EmDash + " no files"));

        Assert.Equal(["Gail"], note.Heads.Select(head => head.Name));
        Assert.Empty(note.Notes); // the dropped "- " lines are not remarks
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-092")] // :45-50 an Int that overflows becomes index 0
    public void A_head_index_that_does_not_fit_an_int_becomes_zero()
    {
        var note = Parse(FilesLine() + "\nHeads\n- Hank (99999999999999999999): t " + EmDash + " no files");

        Assert.Equal(0, note.Heads[0].Index);
        Assert.Equal("0-Hank", note.Heads[0].Id);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-092")] // :56-57 a non-"- " line ends the section and is handled as a normal line
    public void A_line_without_a_dash_prefix_ends_the_heads_section_and_becomes_a_remark()
    {
        var note = Parse(string.Join("\n",
            FilesLine(), "Heads",
            HeadLine("Hank", 1, "Fix"),
            "Left `main` as it was.",
            HeadLine("Dale", 2, "Late"))); // the section is over: this is a plain remark now

        Assert.Equal(["Hank"], note.Heads.Select(head => head.Name));
        Assert.Equal(["Left main as it was.", HeadLine("Dale", 2, "Late")], note.Notes);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-092")] // :32-35 a blank line ends the section and is skipped
    public void A_blank_line_ends_the_heads_section_and_is_skipped()
    {
        var note = Parse(string.Join("\n",
            FilesLine(), "Heads",
            HeadLine("Hank", 1, "Fix"),
            "   ",
            HeadLine("Dale", 2, "Late")));

        Assert.Equal(["Hank"], note.Heads.Select(head => head.Name));
        Assert.Equal([HeadLine("Dale", 2, "Late")], note.Notes);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-092")] // :36-39 only the exact line "Heads" starts the section
    public void Only_the_exact_word_heads_starts_the_section()
    {
        var note = Parse(string.Join("\n",
            FilesLine(),
            "Heads:",
            "heads",
            HeadLine("Hank", 1, "Fix"))); // outside a section a head line is just a remark

        Assert.Empty(note.Heads);
        Assert.Equal(["Heads:", "heads", HeadLine("Hank", 1, "Fix")], note.Notes);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-092")] // :31 lines are trimmed; \r\n bodies parse like \n bodies
    public void Head_lines_are_trimmed_and_crlf_bodies_parse_like_lf_bodies()
    {
        var lines = new[] { FilesLine(), "Heads", "   " + HeadLine("Hank", 1, "Fix", "a.cs") + "  \t", HeadLine("Dale", 2, "Late") };
        var lf = Parse(string.Join("\n", lines));
        var crlf = Parse(string.Join("\r\n", lines));

        Assert.Equal(["Hank", "Dale"], lf.Heads.Select(head => head.Name));
        Assert.Equal(["a.cs"], lf.Heads[0].Files);
        Assert.Equal(lf.Heads, crlf.Heads);
        Assert.Equal(lf.Notes, crlf.Notes);
    }

    // ------------------------------------------------------------------ PR-HYDRAMERGE-093 remarks

    [Theory]
    [Trait("Req", "PR-HYDRAMERGE-093")] // :66-67 a line ending with the exact suffix "is up to date." is dropped
    [InlineData("The checkout is up to date.")]
    [InlineData("The project checkout is up to date.")]
    [InlineData("   The checkout is up to date.   ")] // trimmed first
    public void An_up_to_date_line_is_not_a_remark(string line)
    {
        var note = Parse(FilesLine() + "\n" + line);

        Assert.Empty(note.Notes);
    }

    [Theory]
    [Trait("Req", "PR-HYDRAMERGE-093")] // :67 the suffix must be exact, so near misses stay remarks
    [InlineData("The checkout is up to date")]
    [InlineData("The checkout is up to date. Really.")]
    [InlineData("The checkout is Up to date.")]
    public void A_line_that_only_resembles_the_up_to_date_line_stays_a_remark(string line)
    {
        var note = Parse(FilesLine() + "\n" + line);

        Assert.Equal([line], note.Notes);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-093")] // :68 every backtick is removed, and remarks keep their order
    public void Remarks_lose_every_backtick_and_keep_their_order()
    {
        var note = Parse(string.Join("\n",
            "Checkout of `main` was left as it was.",
            FilesLine(),
            "The project checkout is up to date.",
            "``double`` and `single` ticks",
            "Last remark."));

        Assert.Equal(
            ["Checkout of main was left as it was.", "double and single ticks", "Last remark."],
            note.Notes);
    }
}

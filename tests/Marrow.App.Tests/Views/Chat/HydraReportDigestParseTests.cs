using System;
using System.Linq;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

// App/Views/Chat/HydraReportsPopover.swift:44-196 (HydraReportDigest.parse and the readers it uses). Pure text in, digest out:
// no window, no clock, no process. The expected values below are derived from the Swift rules, and the producer/consumer
// tests feed the digest what Core/Models/Hydra.swift:1314-1418 (reportMessage, landingLine) actually writes.
public sealed class HydraReportDigestParseTests
{
    // The roster the lead writes headers from (Hydra.swift:231-257).
    private const string Ada = "Ada";
    private const string Hank = "Hank";
    private const string Minus = "\u2212";

    private static HydraReportDigest.Landing LandingOf(string line)
    {
        var digest = HydraReportDigest.Parse("## " + Ada + ": task\n" + line + "\n\nBody.");
        return Assert.Single(digest.Heads).Landing;
    }

    private static ValueList<HydraReportDigest.LandedFile> LandedFilesOf(string line) =>
        Assert.IsType<HydraReportDigest.Landing.Landed>(LandingOf(line)).Value;

    private static string LastBodyAfter(string closing) =>
        Assert.Single(HydraReportDigest.Parse("## " + Ada + ": task\nChanged nothing.\n\nThe report.\n\n" + closing).Heads).Body;

    [Fact]
    [Trait("Req", "PR-HYDRA-198")] // HydraReportsPopover.swift:44-76
    public void A_report_splits_into_an_intro_and_one_section_per_head_with_the_landing_line_consumed()
    {
        var text = "Hydra reports: Ada and Hank finished.\n\n"
            + "## Ada: Stream the parser (high)\n"
            + "Landed in your checkout: src/a.swift (+3 " + Minus + "1).\n"
            + "Ada's report.\n\n"
            + "## Hank: Fix the build (failed)\n"
            + "Changed nothing.\n"
            + "Hank's report.";

        var digest = HydraReportDigest.Parse(text);

        Assert.Equal("Hydra reports: Ada and Hank finished.", digest.Intro);
        Assert.Equal(2, digest.Heads.Count);
        Assert.Equal("Ada", digest.Heads[0].Name);
        Assert.Equal("Stream the parser", digest.Heads[0].Task);
        Assert.Equal("Ada's report.", digest.Heads[0].Body);
        var landed = Assert.IsType<HydraReportDigest.Landing.Landed>(digest.Heads[0].Landing);
        Assert.Equal("src/a.swift", Assert.Single(landed.Value).Path);
        Assert.Equal("Hank", digest.Heads[1].Name);
        Assert.Equal("Fix the build", digest.Heads[1].Task);
        Assert.Equal(HydraReportDigest.Outcome.Failed, digest.Heads[1].Outcome);
        Assert.IsType<HydraReportDigest.Landing.Nothing>(digest.Heads[1].Landing);
        Assert.Equal("Hank's report.", digest.Heads[1].Body);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-198")] // HydraReportsPopover.swift:55-60, :80-84
    public void Only_a_roster_named_hash_hash_line_with_a_colon_starts_a_head_and_other_headings_stay_in_the_body()
    {
        var text = "## " + Ada + ": Do it\n"
            + "Changed nothing.\n"
            + "My report.\n"
            + "## Summary\n"
            + "## Notes: for you\n"
            + "## Nobody: not on the roster\n"
            + "##" + Hank + ": no space after the hashes\n"
            + "## " + Hank + " no colon\n"
            + "Last line.";

        var digest = HydraReportDigest.Parse(text);

        var head = Assert.Single(digest.Heads);
        Assert.Equal("Ada", head.Name);
        Assert.Equal(
            "My report.\n## Summary\n## Notes: for you\n## Nobody: not on the roster\n##" + Hank
            + ": no space after the hashes\n## " + Hank + " no colon\nLast line.",
            head.Body);
        Assert.Null(digest.Intro);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-198")] // HydraReportsPopover.swift:54-55, :80-84
    public void A_head_header_may_be_indented_and_names_a_roster_head_in_any_case_or_later_round()
    {
        var text = "intro\n   ## ada: lower case\nChanged nothing.\nOne.\n\t## Hank 2: second round\nChanged nothing.\nTwo.";

        var digest = HydraReportDigest.Parse(text);

        Assert.Equal("intro", digest.Intro);
        Assert.Equal(new[] { "ada", "Hank 2" }, digest.Heads.Select(head => head.Name).ToArray());
        Assert.Equal(new[] { "lower case", "second round" }, digest.Heads.Select(head => head.Task).ToArray());
        Assert.Equal(new[] { "One.", "Two." }, digest.Heads.Select(head => head.Body).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-198")] // HydraReportsPopover.swift:64-66, :68-69
    public void A_report_with_no_head_headers_is_an_intro_only_and_blank_text_has_no_intro()
    {
        var plain = HydraReportDigest.Parse("  First line.\n## Not a head\nLast line.  ");
        Assert.Equal("First line.\n## Not a head\nLast line.", plain.Intro);
        Assert.Empty(plain.Heads);

        var blank = HydraReportDigest.Parse(" \n\n  \n");
        Assert.Null(blank.Intro);
        Assert.Empty(blank.Heads);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-198")] // HydraReportsPopover.swift:62-76
    public void Leading_blank_lines_are_dropped_and_only_the_first_remaining_line_can_be_a_landing()
    {
        var text = "## " + Ada + ": one\n\n   \n  Changed nothing.\nReport.\nChanged nothing.\n\n"
            + "## " + Hank + ": two\nNo landing here.\nChanged nothing.";

        var digest = HydraReportDigest.Parse(text);

        Assert.IsType<HydraReportDigest.Landing.Nothing>(digest.Heads[0].Landing);
        Assert.Equal("Report.\nChanged nothing.", digest.Heads[0].Body);
        Assert.IsType<HydraReportDigest.Landing.None>(digest.Heads[1].Landing);
        Assert.Equal("No landing here.\nChanged nothing.", digest.Heads[1].Body);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-198")] // HydraReportsPopover.swift:72, :191-196
    public void Only_the_last_heads_body_has_the_leads_closing_paragraph_stripped()
    {
        var text = "## " + Ada + ": one\nChanged nothing.\nFirst.\n\nThe user sent that work straight to the heads.\n\n"
            + "## " + Hank + ": two\nChanged nothing.\nSecond.\n\nThe user sent that work straight to the heads.";

        var digest = HydraReportDigest.Parse(text);

        Assert.Equal("First.\n\nThe user sent that work straight to the heads.", digest.Heads[0].Body);
        Assert.Equal("Second.", digest.Heads[1].Body);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-198")] // HydraReportsPopover.swift:44-76; Core/Models/Hydra.swift:1314-1397
    public void A_report_the_lead_builds_parses_back_into_its_heads_in_order()
    {
        var reports = new[]
        {
            new HydraReport
            {
                HeadIndex = 2, Task = "Wire the form", Origin = HydraHeadInfo.Origin.Delegated, Status = HydraHeadInfo.Status.Completed,
                Text = "Wired it.", CopyPath = "C:\\copies\\ada",
            },
            new HydraReport
            {
                HeadIndex = 0, Task = "Fix the build", Origin = HydraHeadInfo.Origin.Delegated, Status = HydraHeadInfo.Status.Failed,
                Text = "", CopyPath = "C:\\copies\\hank",
            },
        };

        var digest = HydraReportDigest.Parse(HydraPrompts.ReportMessage(reports));

        Assert.Equal("Hydra reports: Ada and Hank finished.", digest.Intro);
        Assert.Equal(new[] { "Ada", "Hank" }, digest.Heads.Select(head => head.Name).ToArray());
        Assert.Equal(new[] { "Wire the form", "Fix the build" }, digest.Heads.Select(head => head.Task).ToArray());
        Assert.Equal(HydraReportDigest.Outcome.Done, digest.Heads[0].Outcome);
        Assert.Equal(HydraReportDigest.Outcome.Failed, digest.Heads[1].Outcome);
        Assert.Equal("Wired it.", digest.Heads[0].Body);
        Assert.Equal("No report.", digest.Heads[1].Body);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-200")] // HydraReportsPopover.swift:121-127, :131-134
    public void Changed_nothing_is_the_empty_landing_and_is_matched_on_the_trimmed_line()
    {
        Assert.IsType<HydraReportDigest.Landing.Nothing>(LandingOf("Changed nothing."));
        Assert.IsType<HydraReportDigest.Landing.Nothing>(LandingOf("   Changed nothing.  "));
        Assert.IsType<HydraReportDigest.Landing.Nothing>(LandingOf("Changed nothing. And more words."));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-200")] // HydraReportsPopover.swift:135-142
    public void Nothing_landed_carries_the_copy_path_after_in_its_copy_at_with_one_trailing_period_removed()
    {
        var path = HydraReportDigest.Parse(
            "## " + Ada + ": t\nNothing landed; its unfinished work is in its copy at C:\\copies\\ada.\n\nBody.").Heads[0].Landing;
        Assert.Equal("C:\\copies\\ada", Assert.IsType<HydraReportDigest.Landing.Unfinished>(path).CopyPath);

        var noPeriod = LandingOf("Nothing landed; its unfinished work is in its copy at C:\\copies\\ada");
        Assert.Equal("C:\\copies\\ada", Assert.IsType<HydraReportDigest.Landing.Unfinished>(noPeriod).CopyPath);

        var doublePeriod = LandingOf("Nothing landed; in its copy at /tmp/ada..");
        Assert.Equal("/tmp/ada.", Assert.IsType<HydraReportDigest.Landing.Unfinished>(doublePeriod).CopyPath);

        var spaced = LandingOf("Nothing landed; in its copy at   /tmp/ada   ");
        Assert.Equal("/tmp/ada", Assert.IsType<HydraReportDigest.Landing.Unfinished>(spaced).CopyPath);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-200")] // HydraReportsPopover.swift:135-142
    public void Nothing_landed_without_a_copy_path_or_with_an_empty_one_has_no_path()
    {
        Assert.Null(Assert.IsType<HydraReportDigest.Landing.Unfinished>(LandingOf("Nothing landed")).CopyPath);
        Assert.Null(Assert.IsType<HydraReportDigest.Landing.Unfinished>(LandingOf("Nothing landed; its work is elsewhere.")).CopyPath);
        Assert.Null(Assert.IsType<HydraReportDigest.Landing.Unfinished>(LandingOf("Nothing landed in its copy at .")).CopyPath);
        Assert.Null(Assert.IsType<HydraReportDigest.Landing.Unfinished>(LandingOf("Nothing landed in its copy at ")).CopyPath);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-200")] // HydraReportsPopover.swift:143-145
    public void Landed_in_your_checkout_carries_the_files_parsed_from_the_remainder()
    {
        var files = LandedFilesOf("Landed in your checkout: a.swift (+1 " + Minus + "2), dir/b.swift (+30 " + Minus + "0).");

        Assert.Equal(new[] { "a.swift", "dir/b.swift" }, files.Select(file => file.Path).ToArray());
        Assert.Equal(new long[] { 1, 30 }, files.Select(file => file.Additions).ToArray());
        Assert.Equal(new long[] { 2, 0 }, files.Select(file => file.Deletions).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-200")] // HydraReportsPopover.swift:146-154
    public void Did_not_land_carries_no_error_when_none_follows_and_its_files_after_the_colon()
    {
        var landing = Assert.IsType<HydraReportDigest.Landing.NotLanded>(
            LandingOf("Did not land: a.swift (+1 " + Minus + "2), b.swift (+3 " + Minus + "4) with conflicts."));

        Assert.Null(landing.Error);
        Assert.Equal(new[] { "a.swift", "b.swift" }, landing.Value.Select(file => file.Path).ToArray());
        Assert.Equal(new[] { false, true }, landing.Value.Select(file => file.HasConflicts).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-200")] // HydraReportsPopover.swift:146-154
    public void Did_not_land_takes_an_error_from_the_parenthesis_group_right_after_it_then_the_colon_and_files()
    {
        var landing = Assert.IsType<HydraReportDigest.Landing.NotLanded>(
            LandingOf("Did not land (  git apply failed  ): a.swift (+1 " + Minus + "2)."));

        Assert.Equal("git apply failed", landing.Error);
        Assert.Equal("a.swift", Assert.Single(landing.Value).Path);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-200")] // HydraReportsPopover.swift:146-154
    public void An_error_string_with_a_closing_parenthesis_ends_at_the_first_one_and_an_empty_error_is_none()
    {
        var cut = Assert.IsType<HydraReportDigest.Landing.NotLanded>(
            LandingOf("Did not land (merge (3-way) failed): a.swift (+1 " + Minus + "2)."));
        // The error stops at the first ")": "merge (3-way". What is left, " failed): a.swift ...", does not start with a colon,
        // and the one item "failed): a.swift (+1 -2)" is not a file.
        Assert.Equal("merge (3-way", cut.Error);
        Assert.Empty(cut.Value);

        var empty = Assert.IsType<HydraReportDigest.Landing.NotLanded>(LandingOf("Did not land (): a.swift (+1 " + Minus + "2)."));
        Assert.Null(empty.Error);
        Assert.Equal("a.swift", Assert.Single(empty.Value).Path);

        var unclosed = Assert.IsType<HydraReportDigest.Landing.NotLanded>(LandingOf("Did not land (never closed"));
        Assert.Null(unclosed.Error);
        Assert.Empty(unclosed.Value);

        // The colon is optional: after "Did not land" with no error group, a leading ":" is dropped only when there is one.
        var bare = Assert.IsType<HydraReportDigest.Landing.NotLanded>(LandingOf("Did not land a.swift (+1 " + Minus + "2)"));
        Assert.Null(bare.Error);
        Assert.Equal("a.swift", Assert.Single(bare.Value).Path);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-200")] // HydraReportsPopover.swift:121-127
    public void A_first_line_without_one_of_the_four_prefixes_is_no_landing_and_stays_in_the_body()
    {
        foreach (var line in new[]
        {
            "Landed in your checkout", "landed in your checkout: a (+1 " + Minus + "1)", "Did not", "Changed nothing", "changed nothing.",
            "Nothing was landed", "The head landed in your checkout: a (+1 " + Minus + "1)",
        })
        {
            var head = Assert.Single(HydraReportDigest.Parse("## " + Ada + ": t\n" + line + "\nMore.").Heads);
            Assert.IsType<HydraReportDigest.Landing.None>(head.Landing);
            Assert.Equal(line + "\nMore.", head.Body);
        }
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-200")] // HydraReportsPopover.swift:121-155; Core/Models/Hydra.swift:1401-1418
    public void Every_landing_line_the_lead_can_write_parses_back_into_the_matching_landing()
    {
        HydraReport Report(HydraLanding? landing, HydraHeadInfo.Status status, string? copy) => new()
        {
            HeadIndex = 2, Task = "t", Origin = HydraHeadInfo.Origin.Delegated, Status = status, Text = "Report.",
            Landing = landing, CopyPath = copy,
        };
        HydraLanding.File File(string path, long add, long del) => new() { Path = path, Additions = add, Deletions = del };
        HydraReportDigest.Head Parsed(HydraReport report) =>
            Assert.Single(HydraReportDigest.Parse(HydraPrompts.ReportMessage([report])).Heads);

        Assert.IsType<HydraReportDigest.Landing.Nothing>(
            Parsed(Report(null, HydraHeadInfo.Status.Completed, "C:\\copy")).Landing);
        Assert.IsType<HydraReportDigest.Landing.Nothing>(
            Parsed(Report(new HydraLanding(), HydraHeadInfo.Status.Completed, null)).Landing);

        var unfinished = Assert.IsType<HydraReportDigest.Landing.Unfinished>(
            Parsed(Report(null, HydraHeadInfo.Status.Stopped, "C:\\copies\\ada")).Landing);
        Assert.Equal("C:\\copies\\ada", unfinished.CopyPath);

        var landed = Assert.IsType<HydraReportDigest.Landing.Landed>(Parsed(Report(
            new HydraLanding { Files = new ValueList<HydraLanding.File>([File("a.swift", 4, 5), File("b.swift", 6, 7)]), Conflicts = new ValueList<string>(["b.swift"]) },
            HydraHeadInfo.Status.Completed, null)).Landing);
        Assert.Equal(new[] { ("a.swift", 4L, 5L, false), ("b.swift", 6L, 7L, true) },
            landed.Value.Select(file => (file.Path, file.Additions, file.Deletions, file.HasConflicts)).ToArray());

        var failed = Assert.IsType<HydraReportDigest.Landing.NotLanded>(Parsed(Report(
            new HydraLanding { Files = new ValueList<HydraLanding.File>([File("a.swift", 1, 2)]), Error = "disk full" },
            HydraHeadInfo.Status.Completed, "C:\\copy")).Landing);
        Assert.Equal("disk full", failed.Error);
        Assert.Equal("a.swift", Assert.Single(failed.Value).Path);

        var patched = Assert.IsType<HydraReportDigest.Landing.NotLanded>(Parsed(Report(
            new HydraLanding { Files = new ValueList<HydraLanding.File>([File("a.swift", 1, 2)]), PatchPath = "C:\\copy\\a.patch" },
            HydraHeadInfo.Status.Completed, "C:\\copy")).Landing);
        Assert.Null(patched.Error);
        Assert.Equal("a.swift", Assert.Single(patched.Value).Path);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-201")] // HydraReportsPopover.swift:171-181
    public void A_file_item_accepts_the_minus_sign_or_an_ascii_hyphen_and_an_optional_conflict_flag()
    {
        var files = LandedFilesOf(
            "Landed in your checkout: m.swift (+12 " + Minus + "3), h.swift (+4 -5), c.swift (+6 " + Minus + "7) with conflicts, d.swift (+8 -9) with conflicts");

        Assert.Equal(
            new[] { ("m.swift", 12L, 3L, false), ("h.swift", 4L, 5L, false), ("c.swift", 6L, 7L, true), ("d.swift", 8L, 9L, true) },
            files.Select(file => (file.Path, file.Additions, file.Deletions, file.HasConflicts)).ToArray());
        Assert.Equal("m.swift", files[0].Id);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-201")] // HydraReportsPopover.swift:173, :178-181
    public void An_item_must_match_the_whole_pattern_and_the_ones_that_do_not_are_dropped_silently()
    {
        var files = LandedFilesOf(
            "Landed in your checkout: ok.swift (+1 " + Minus + "1), "
            + "has space.swift (+2 " + Minus + "2), "
            + "junk, "
            + "nodel.swift (+3), "
            + "plus.swift (1 " + Minus + "4), "
            + "tail.swift (+5 " + Minus + "5) extra, "
            + "flag.swift (+6 " + Minus + "6) with conflict, "
            + "(+7 " + Minus + "7), "
            + "also-ok.swift (+8 " + Minus + "8)");

        Assert.Equal(new[] { "ok.swift", "also-ok.swift" }, files.Select(file => file.Path).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-201")] // HydraReportsPopover.swift:157-169
    public void Items_are_split_on_comma_space_only_and_one_trailing_period_is_removed()
    {
        var files = LandedFilesOf("Landed in your checkout: a.swift (+1 " + Minus + "1),b.swift (+2 " + Minus + "2), c.swift (+3 " + Minus + "3).");
        Assert.Equal(new[] { "c.swift" }, files.Select(file => file.Path).ToArray());

        var twoPeriods = LandedFilesOf("Landed in your checkout: a.swift (+1 " + Minus + "1)..");
        Assert.Empty(twoPeriods);

        var none = LandedFilesOf("Landed in your checkout:");
        Assert.Empty(none);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-201")] // HydraReportsPopover.swift:163-166
    public void The_and_n_more_tail_is_stripped_from_the_end_of_the_list()
    {
        var files = LandedFilesOf("Landed in your checkout: a.swift (+1 " + Minus + "1), b.swift (+2 " + Minus + "2), and 12 more.");

        Assert.Equal(new[] { "a.swift", "b.swift" }, files.Select(file => file.Path).ToArray());

        // The clause is never itself a file item, so dropping it from the list is unobservable on its own: an item "and 3 more"
        // would be discarded by the per-item match anyway (reported as an equivalent mutant).
        var lone = LandedFilesOf("Landed in your checkout: only.swift (+1 " + Minus + "1), and 3 more.");
        Assert.Equal(new[] { "only.swift" }, lone.Select(file => file.Path).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-201")] // HydraReportsPopover.swift:163-166
    public void The_build_output_clause_is_stripped_for_one_file_or_several_and_before_the_and_n_more_clause()
    {
        var one = LandedFilesOf("Landed in your checkout: a.swift (+1 " + Minus + "1); 1 build-output file was left out.");
        Assert.Equal(new[] { "a.swift" }, one.Select(file => file.Path).ToArray());

        var several = LandedFilesOf("Landed in your checkout: a.swift (+1 " + Minus + "1); 4 build-output files were left out.");
        Assert.Equal(new[] { "a.swift" }, several.Select(file => file.Path).ToArray());

        var both = LandedFilesOf(
            "Landed in your checkout: a.swift (+1 " + Minus + "1), b.swift (+2 " + Minus + "2), and 9 more; 2 build-output files were left out.");
        Assert.Equal(new[] { "a.swift", "b.swift" }, both.Select(file => file.Path).ToArray());

        // The clause is only a tail: inside the list it is just an item that is not a file.
        var inside = LandedFilesOf("Landed in your checkout: a.swift (+1 " + Minus + "1); 1 build-output file was left out, b.swift (+2 " + Minus + "2).");
        Assert.Equal(new[] { "b.swift" }, inside.Select(file => file.Path).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-201")] // HydraReportsPopover.swift:171-181
    public void A_count_too_large_for_a_signed_64_bit_integer_drops_the_item()
    {
        var files = LandedFilesOf(
            "Landed in your checkout: big.swift (+99999999999999999999 " + Minus + "1), max.swift (+9223372036854775807 " + Minus + "0)");

        var file = Assert.Single(files);
        Assert.Equal("max.swift", file.Path);
        Assert.Equal(long.MaxValue, file.Additions);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-201")] // HydraReportsPopover.swift:157-169; Core/Models/Hydra.swift:1401-1418
    public void The_file_list_the_lead_writes_with_conflicts_a_cut_off_and_omitted_build_output_parses_back()
    {
        var files = Enumerable.Range(1, 27)
            .Select(i => new HydraLanding.File { Path = "src/f" + i + ".swift", Additions = i, Deletions = 100 - i })
            .ToArray();
        var report = new HydraReport
        {
            HeadIndex = 2, Task = "t", Origin = HydraHeadInfo.Origin.Delegated, Status = HydraHeadInfo.Status.Completed, Text = "Report.",
            Landing = new HydraLanding
            {
                Files = new ValueList<HydraLanding.File>(files), Conflicts = new ValueList<string>(["src/f2.swift"]), DroppedBuildOutputFiles = 3,
            },
        };

        var landing = Assert.Single(HydraReportDigest.Parse(HydraPrompts.ReportMessage([report])).Heads).Landing;

        var parsed = Assert.IsType<HydraReportDigest.Landing.Landed>(landing).Value;
        Assert.Equal(25, parsed.Count);
        Assert.Equal(files.Take(25).Select(file => file.Path).ToArray(), parsed.Select(file => file.Path).ToArray());
        Assert.Equal(files.Take(25).Select(file => (file.Additions, file.Deletions)).ToArray(), parsed.Select(file => (file.Additions, file.Deletions)).ToArray());
        Assert.Equal(new[] { "src/f2.swift" }, parsed.Where(file => file.HasConflicts).Select(file => file.Path).ToArray());
    }

    [Theory]
    [Trait("Req", "PR-HYDRA-202")] // HydraReportsPopover.swift:185-196
    [InlineData("The changes listed above are in your checkout already; nothing needs applying.")]
    [InlineData("The heads worked in your checkout, so their changes are there already.")]
    [InlineData("A file listed with conflicts was merged three-way and keeps conflict markers.")]
    [InlineData("Do not check any of this with git status or git diff.")]
    [InlineData("Do not wait for Hank and do not take over its tasks.")]
    [InlineData("A head that failed leaves its part to you.")]
    [InlineData("A head the user stopped leaves its part alone unless the user asks for it.")]
    [InlineData("The user queued that work for the heads while you were busy.")]
    [InlineData("The user sent that work straight to the heads instead of to you.")]
    public void The_last_paragraph_of_the_last_heads_body_is_dropped_when_it_starts_with_one_of_the_nine_prefixes(string closing)
    {
        Assert.Equal("The report.", LastBodyAfter(closing));
        Assert.Equal("The report.", LastBodyAfter("   " + closing + "   "));
    }

    [Theory]
    [Trait("Req", "PR-HYDRA-202")] // HydraReportsPopover.swift:185-196
    [InlineData("Ada and Hank both changed src/a.swift: read it as it is now before you build on it.")]
    [InlineData("Ada's changes would not apply on their own; the patch is at C:\\p.patch.")]
    [InlineData("The changes listed")]
    [InlineData("Do not wait")]
    [InlineData("A head that")]
    [InlineData("See: The user queued that work for the heads.")]
    [InlineData("the user sent that work straight to the heads.")]
    public void A_last_paragraph_that_starts_with_anything_else_stays_in_the_body(string closing)
    {
        Assert.Equal("The report.\n\n" + closing, LastBodyAfter(closing));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-202")] // HydraReportsPopover.swift:191-196
    public void Only_the_paragraph_after_the_last_blank_line_is_examined_and_a_body_with_no_blank_line_is_shown_whole()
    {
        var middle = LastBodyAfter("Do not wait for Hank.\n\nA closing thought of the head's own.");
        Assert.Equal("The report.\n\nDo not wait for Hank.\n\nA closing thought of the head's own.", middle);

        var single = HydraReportDigest.Parse("## " + Ada + ": t\nChanged nothing.\nDo not wait for Hank.").Heads[0].Body;
        Assert.Equal("Do not wait for Hank.", single);

        // What is left is trimmed: a spare blank line or trailing spaces before the dropped paragraph do not stay.
        var spare = HydraReportDigest.Parse("## " + Ada + ": t\nChanged nothing.\nThe report.  \n\n\nThe user queued that work.").Heads[0].Body;
        Assert.Equal("The report.", spare);

        // Dropping the paragraph leaves what came before it, trimmed, including earlier paragraphs.
        var two = LastBodyAfter("First part.\n\nThe user queued that work.");
        Assert.Equal("The report.\n\nFirst part.", two);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-202")] // HydraReportsPopover.swift:185-196; Core/Models/Hydra.swift:1341-1388
    public void The_closing_paragraph_the_lead_builds_is_dropped_when_it_opens_with_a_listed_sentence()
    {
        var report = new HydraReport
        {
            HeadIndex = 2, Task = "Wire the form", Origin = HydraHeadInfo.Origin.Delegated, Status = HydraHeadInfo.Status.Failed,
            Text = "It broke.", CopyPath = "C:\\copies\\ada",
        };

        var digest = HydraReportDigest.Parse(HydraPrompts.ReportMessage([report], stillWorking: ["Hank"]));

        // The closing opens "A head that failed ...", one of the nine, so the whole one-paragraph closing leaves.
        Assert.Equal("It broke.", Assert.Single(digest.Heads).Body);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-202")] // HydraReportsPopover.swift:185-196; Core/Models/Hydra.swift:1353-1361, :1385
    public void A_closing_paragraph_that_opens_with_the_shared_file_sentence_is_shown_in_the_last_heads_body()
    {
        HydraReport Report(long index, string path) => new()
        {
            HeadIndex = index, Task = "t", Origin = HydraHeadInfo.Origin.Delegated, Status = HydraHeadInfo.Status.Completed,
            Text = "Report of " + index + ".", CopyPath = "C:\\copy",
            Landing = new HydraLanding
            {
                Files = new ValueList<HydraLanding.File>([new HydraLanding.File { Path = path, Additions = 1, Deletions = 1 }]),
                Error = "disk full",
            },
        };

        var digest = HydraReportDigest.Parse(HydraPrompts.ReportMessage([Report(2, "src/a.swift"), Report(0, "src/a.swift")]));

        // Neither "Ada and Hank both changed src/a.swift: ..." nor the style rule that rides in the same paragraph is a listed
        // prefix, so the lead's instructions are rendered inside the last head's report (upstream behaviour, reproduced).
        Assert.Equal("Report of 2.", digest.Heads[0].Body);
        var last = digest.Heads[1].Body;
        Assert.StartsWith("Report of 0.\n\nAda and Hank both changed src/a.swift: read it as it is now before you build on it.", last, StringComparison.Ordinal);
        Assert.Contains("Do not check any of this with git status or git diff", last, StringComparison.Ordinal);
    }
}

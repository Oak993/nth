using Marrow.Core.Port;
using Marrow.Services.Git;

namespace Marrow.Services.Tests.Git;

// Source: DroppyCode/Services/Git/RevertPreview.swift (B2 1fbf81e), :1-42.
// Every expected value below is transcribed from that file and from the Swift standard library semantics named
// beside it (split, Int(String), reduce with a trapping +), never from a C# implementation.
//
// The thrown error: every guard throws CocoaError(.fileReadCorruptFile) (:33, :36), ported as
// Marrow.Core.Port.CocoaError (ED-100): Domain "NSCocoaErrorDomain", code 259. AssertCorruptFile pins that identity on
// every malformed shape, that it is a thrown Swift error (not a trap, R-ERR-1), and that try? turns it into nil so no
// partial preview escapes. Its description is Foundation's text, UNVERIFIED as oracle item O-25.
public sealed class RevertPreviewTests
{
    private static RevertPreview.File Row(string path, long additions, long deletions, bool isBinary = false) =>
        new(path, additions, deletions, isBinary);

    private static CocoaError AssertCorruptFile(string numstat)
    {
        var thrown = Assert.ThrowsAny<Exception>(() => new RevertPreview(numstat));
        Assert.True(SwiftTry.IsThrownError(thrown), $"a thrown Swift error, not a trap: {thrown.GetType().Name}");
        // CocoaError(.fileReadCorruptFile), RevertPreview.swift:33, :36; ED-100 identity.
        var error = Assert.IsType<CocoaError>(thrown);
        Assert.Equal(CocoaError.ErrorCode.FileReadCorruptFile, error.ErrorCodeValue);
        Assert.Equal("NSCocoaErrorDomain", error.Domain);
        Assert.Equal(259L, error.Code);
        Assert.Null(SwiftTry.Optional(() => new RevertPreview(numstat)));
        return error;
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:29-40
    public void A_text_record_gives_its_counts_its_path_and_is_not_binary_or_kept()
    {
        var preview = new RevertPreview("12\t3\tSources/App/main.swift\0");
        var file = Assert.Single(preview.Files);
        Assert.Equal("Sources/App/main.swift", file.Path);
        Assert.Equal(12L, file.Additions);
        Assert.Equal(3L, file.Deletions);
        Assert.False(file.IsBinary);
        Assert.False(file.IsKept);   // :12 var isKept = false, and :38 never passes isKept
        Assert.Equal("Sources/App/main.swift", file.Id); // :14 var id: String { path }
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:34-35, :38
    public void A_binary_record_has_both_counts_dash_and_gets_zero_and_zero()
    {
        var preview = new RevertPreview("-\t-\tAssets/icon.png\0");
        var file = Assert.Single(preview.Files);
        Assert.Equal("Assets/icon.png", file.Path);
        Assert.Equal(0L, file.Additions);
        Assert.Equal(0L, file.Deletions);
        Assert.True(file.IsBinary);
        Assert.False(file.IsKept);
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:30-31, :40
    public void Empty_input_gives_an_empty_file_list_and_no_error()
    {
        Assert.Empty(new RevertPreview("").Files);
        // split(separator:) drops empty subsequences by default, so NULs alone are no records.
        Assert.Empty(new RevertPreview("\0").Files);
        Assert.Empty(new RevertPreview("\0\0\0").Files);
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:31, :38
    public void Records_keep_their_order_and_empty_pieces_between_NULs_are_skipped()
    {
        var preview = new RevertPreview("\0" + "1\t2\ta.txt\0\0" + "-\t-\tb.bin\0" + "0\t7\tc.txt");
        Assert.Equal(new[] { Row("a.txt", 1, 2), Row("b.bin", 0, 0, isBinary: true), Row("c.txt", 0, 7) }, preview.Files);
        Assert.Equal(new[] { "a.txt", "b.bin", "c.txt" }, preview.Files.Select(f => f.Id));
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:32, :38
    public void The_tab_split_stops_after_two_splits_so_the_path_keeps_its_tabs()
    {
        var preview = new RevertPreview("4\t5\tdir\twith\ttabs.txt\0");
        Assert.Equal("dir\twith\ttabs.txt", Assert.Single(preview.Files).Path);
        // After two splits the remainder is the third field even when it is only a tab: it is not empty.
        var tabOnly = new RevertPreview("1\t0\t\t\0");
        Assert.Equal("\t", Assert.Single(tabOnly.Files).Path);
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:32-36 (omittingEmptySubsequences: false)
    public void Empty_fields_between_tabs_are_kept_so_a_doubled_or_leading_tab_throws()
    {
        // Kept empties: ["1", "", "2\tfile.txt"] and ["", "1", "2\tfile.txt"]; an empty count is not an Int.
        AssertCorruptFile("1\t\t2\tfile.txt\0");
        AssertCorruptFile("\t1\t2\tfile.txt\0");
        AssertCorruptFile("-\t\t-\tfile.txt\0");
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:31-32, :38; port_impacts.md 231 (G12)
    public void A_path_keeps_carriage_returns_newlines_and_non_ASCII_text_byte_for_byte()
    {
        var preview = new RevertPreview("1\t1\tname\r\0" + "2\t2\tline\r\nbreak\0" + "3\t3\tcaf\U000000E9 \U0001F600.md\0"); // U+00E9 LATIN SMALL LETTER E WITH ACUTE, U+1F600 GRINNING FACE
        Assert.Equal(new[] { "name\r", "line\r\nbreak", "caf\U000000E9 \U0001F600.md" }, preview.Files.Select(f => f.Path)); // U+00E9, U+1F600
        Assert.Equal(new byte[] { 0x6E, 0x61, 0x6D, 0x65, 0x0D }, System.Text.Encoding.UTF8.GetBytes(preview.Files[0].Path));
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:32-33
    public void A_record_without_exactly_three_fields_throws()
    {
        AssertCorruptFile("12\t3\0");
        AssertCorruptFile("12\0");
        AssertCorruptFile("Sources/App/main.swift\0");
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:33
    public void A_record_whose_path_field_is_empty_throws()
    {
        AssertCorruptFile("12\t3\t\0");
        AssertCorruptFile("-\t-\t\0");
        // The rename shape git prints under -z (counts, an empty path field, then the two paths) is refused.
        AssertCorruptFile("1\t1\t\0old.txt\0new.txt\0");
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:34-36
    public void A_count_that_is_not_a_Swift_Int_throws()
    {
        AssertCorruptFile("x\t3\tfile.txt\0");
        AssertCorruptFile("12\ty\tfile.txt\0");
        AssertCorruptFile("\t\tfile.txt\0");             // empty counts
        AssertCorruptFile(" 1\t2\tfile.txt\0");          // Int(" 1") is nil: no whitespace
        AssertCorruptFile("1 \t2\tfile.txt\0");
        AssertCorruptFile("1_000\t2\tfile.txt\0");       // no underscores
        AssertCorruptFile("1.0\t2\tfile.txt\0");
        AssertCorruptFile("0x10\t2\tfile.txt\0");
        AssertCorruptFile("+\t2\tfile.txt\0");           // a sign with no digits
        AssertCorruptFile("\U00000663\t2\tfile.txt\0"); // U+0663 ARABIC-INDIC DIGIT THREE is not an ASCII digit
        AssertCorruptFile("9223372036854775808\t0\tfile.txt\0"); // Int.max + 1 overflows to nil
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:34-36
    public void Only_both_counts_dash_is_binary_and_a_single_dash_throws()
    {
        AssertCorruptFile("-\t4\tfile.txt\0");
        AssertCorruptFile("4\t-\tfile.txt\0");
        AssertCorruptFile("--\t-\tfile.txt\0");
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:35-36
    public void A_negative_count_throws()
    {
        AssertCorruptFile("-1\t0\tfile.txt\0");
        AssertCorruptFile("0\t-5\tfile.txt\0");
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:35, :38 (Int(String) accepts one leading sign; Int is 64-bit)
    public void Counts_parse_as_Swift_Int_with_a_sign_and_the_full_64_bit_range()
    {
        var preview = new RevertPreview("+7\t-0\tsigned.txt\0" + "9223372036854775807\t0\tbig.txt\0");
        Assert.Equal(new[] { Row("signed.txt", 7, 0), Row("big.txt", long.MaxValue, 0) }, preview.Files);
        Assert.Equal(typeof(long), typeof(RevertPreview.File).GetProperty("Additions")!.PropertyType);
        Assert.Equal(typeof(long), typeof(RevertPreview.File).GetProperty("Deletions")!.PropertyType);
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:30-40 (files is assigned only after the loop)
    public void One_malformed_record_fails_the_whole_parse_even_after_good_ones()
    {
        AssertCorruptFile("1\t2\ta.txt\0" + "-\t-\tb.bin\0" + "bad\0");
        AssertCorruptFile("1\t2\ta.txt\0" + "3\t-4\tc.txt\0");
    }

    [Fact]
    [Trait("Req", "PR-GIT-099")] // RevertPreview.swift:33, :36 (both guards throw CocoaError(.fileReadCorruptFile))
    public void Every_malformed_shape_throws_the_same_error()
    {
        var errors = new[]
        {
            AssertCorruptFile("12\t3\0"),
            AssertCorruptFile("12\t3\t\0"),
            AssertCorruptFile("x\t3\tfile.txt\0"),
            AssertCorruptFile("-1\t3\tfile.txt\0"),
            AssertCorruptFile("-\t3\tfile.txt\0"),
        };
        foreach (var error in errors)
        {
            Assert.Equal(errors[0].Domain, error.Domain);
            Assert.Equal(errors[0].Code, error.Code);
            Assert.Equal(errors[0].Message, error.Message);
        }
    }

    // R-TEST-6: the expected text is Foundation's, not RevertPreview.swift's (:33 and :36 only throw
    // CocoaError(.fileReadCorruptFile)), and it is UNVERIFIED as oracle O-25 (ED-100). This test therefore
    // carries no Req trait and cannot verify PR-GIT-099 until O-25 is settled.
    [Fact]
    [Trait("Decision", "ED-100")]
    [Trait("Rule", "R-ERR-2")]
    [Trait("Oracle", "O-25")]
    public void The_corrupt_file_error_describes_itself_with_Foundations_text()
    {
        var error = AssertCorruptFile("12\t3\0");
        // U+2019 RIGHT SINGLE QUOTATION MARK twice, as Foundation writes it (ED-100, UNVERIFIED as O-25).
        const string expected = "The file couldn\U00002019t be opened because it isn\U00002019t in the correct format."; // U+2019 RIGHT SINGLE QUOTATION MARK
        Assert.Equal(expected, SwiftErrors.LocalizedDescription(error));
        Assert.Equal(expected, error.Message);
        Assert.Equal(2, SwiftErrors.LocalizedDescription(error).Count(c => c == '\u2019')); // U+2019 RIGHT SINGLE QUOTATION MARK
    }

    [Fact]
    [Trait("Req", "PR-GIT-100")] // RevertPreview.swift:24-26
    public void Restorable_excludes_kept_files_and_the_totals_sum_only_the_restorable_ones()
    {
        var kept = Row("theirs.txt", 10, 20) with { IsKept = true };
        var preview = new RevertPreview(new[] { Row("a.txt", 3, 1), kept, Row("b.bin", 0, 0, isBinary: true), Row("c.txt", 5, 0) });
        Assert.Equal(4, preview.Files.Count);
        Assert.Equal(new[] { Row("a.txt", 3, 1), Row("b.bin", 0, 0, isBinary: true), Row("c.txt", 5, 0) }, preview.Restorable);
        Assert.Equal(8L, preview.Additions);
        Assert.Equal(1L, preview.Deletions);
    }

    [Fact]
    [Trait("Req", "PR-GIT-100")] // RevertPreview.swift:24-26
    public void A_preview_whose_files_are_all_kept_lists_them_and_totals_zero()
    {
        var preview = new RevertPreview(new[] { Row("x.txt", 4, 9) with { IsKept = true }, Row("y.txt", 6, 1) with { IsKept = true } });
        Assert.Equal(new[] { "x.txt", "y.txt" }, preview.Files.Select(f => f.Path));
        Assert.Empty(preview.Restorable);
        Assert.Equal(0L, preview.Additions);
        Assert.Equal(0L, preview.Deletions);
    }

    [Fact]
    [Trait("Req", "PR-GIT-100")] // RevertPreview.swift:19-26
    public void With_nothing_kept_every_file_is_restorable_and_an_empty_preview_totals_zero()
    {
        var preview = new RevertPreview("2\t3\ta.txt\0-\t-\tb.bin\0" + "40\t1\tc.txt\0");
        Assert.Equal(preview.Files, preview.Restorable);
        Assert.Equal(42L, preview.Additions);
        Assert.Equal(4L, preview.Deletions);
        var empty = new RevertPreview(Array.Empty<RevertPreview.File>());
        Assert.Empty(empty.Files);
        Assert.Empty(empty.Restorable);
        Assert.Equal(0L, empty.Additions);
        Assert.Equal(0L, empty.Deletions);
    }

    [Fact]
    [Trait("Req", "PR-GIT-100")] // RevertPreview.swift:17, :24-26 (computed from files on every read)
    public void Marking_a_file_kept_on_a_copy_changes_that_copys_totals_and_not_the_original()
    {
        var preview = new RevertPreview(new[] { Row("a.txt", 3, 1), Row("b.txt", 5, 2) });
        var changed = preview with { Files = preview.Files.SetItem(1, preview.Files[1] with { IsKept = true }) };
        Assert.Equal(new[] { Row("a.txt", 3, 1) }, changed.Restorable);
        Assert.Equal(3L, changed.Additions);
        Assert.Equal(1L, changed.Deletions);
        Assert.Equal(8L, preview.Additions);
        Assert.Equal(3L, preview.Deletions);
        Assert.False(preview.Files[1].IsKept);
    }

    [Fact]
    [Trait("Req", "PR-GIT-100")] // RevertPreview.swift:25-26 (reduce with Swift's + traps on overflow)
    [Trait("Rule", "R-NUM-4")]
    public void The_totals_trap_on_overflow_as_Swift_addition_does()
    {
        var preview = new RevertPreview(new[] { Row("a.txt", long.MaxValue, long.MaxValue), Row("b.txt", 1, 1) });
        Assert.Throws<OverflowException>(() => preview.Additions);
        Assert.Throws<OverflowException>(() => preview.Deletions);
        // A kept file is not summed, so the same rows with one kept do not overflow.
        var keptOne = preview with { Files = preview.Files.SetItem(1, preview.Files[1] with { IsKept = true }) };
        Assert.Equal(long.MaxValue, keptOne.Additions);
        Assert.Equal(long.MaxValue, keptOne.Deletions);
    }
}

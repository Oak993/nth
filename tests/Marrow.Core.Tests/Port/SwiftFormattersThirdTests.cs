using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// Foundation unit F.ICUFormatters (IMPLEMENTING.md 4.3; R-FMT-2), decisions_draft.md E2:
// ByteCountFormatStyle(style: .file) for the three B3 sites that use it. See SwiftFormattersTests.cs for the rest of
// F.ICUFormatters' surface note; this file adds only SwiftFormatters.ByteCountStyleFile(long, string), which that
// file doesn't cover. SwiftFormatters.FileByteCount (ByteCountFormatter, ED-133) stays as it is: it has its own
// remaining Swift twins outside this lane's three sites, and its own tests in SwiftFormattersTests.cs.
//
// Swift call sites (B3 52bc7926097cd622d08d6294e0d8b08a48c7516e), verified directly against the pinned tree: all
// three use ".byteCount(style: .file)" or "ByteCountFormatStyle(style: .file)" with every other parameter left at
// its default (allowedUnits .all, spellsOutZero true, includesActualByteCount false), and none sets a locale:
//   UI/Common/AttachmentPreview.swift:95-98    static func fileSize(_ path: String) -> String? { ...
//                                               size.int64Value.formatted(.byteCount(style: .file)) }
//   App/Views/Composer/DownloadsPopover.swift:68  size: size > 0 ? Int64(size).formatted(.byteCount(style: .file)) : nil
//   App/Views/Settings/AboutUpdateViews.swift:787  parts.append(ByteCountFormatStyle(style: .file).format(size))
// (B3 has no ByteCountFormatter call left; decisions_draft.md E2's grep claim is confirmed.)
//
// Evidence (E:/Droppy/work/b3/evidence_6_1/): sf-swift-6.2-RELEASE-ByteCountFormatStyle.swift (Attributed._format
// :151-242: the zero branch :153-180 is independent of style; the unit is chosen by maxDecimalSizes with a strict
// "&lt;" comparison :194-207, so each threshold value itself belongs to the next unit up, not the one it names;
// precision :212-220; only the byte unit is unit-width-full-name, every other unit takes ICU's default (short)
// width :222, which is where "kB" — not FileByteCount's "KB" — comes from); sf-swift-6.2-RELEASE-ByteTests.swift
// (zeroSpelledOutKb :22-33, real, en_US "Zero kB"; attributed() :123-187, real, .file style: 0 -> "Zero kB", 1 ->
// "1 byte", 1,100,000 -> "1.1 MB"; actualByteCount() :115-117, real, .file: 1,024 -> "1 kB"). The decimal singular-unit
// test (singularUnitsDecimal, guarded by "#if FIXED_86386684") does not run, so 1 MB/1 GB/1 TB/1 PB have no upstream
// test behind them; the values here are derived from the source above instead and cross-checked against its worked
// grouping/rounding examples.
//
// A correction against decisions_draft.md's own E2 table: the maxDecimalSizes comparison is strict "&lt;" (:201), so
// 999_499 (the kB threshold constant) already selects MB, and 999_949_999 (the MB threshold constant) already
// selects GB — the draft's rows for those two exact byte counts ("999 kB" and "999.9 MB") describe the wrong
// bucket; the boundary tests below use the source's own semantics instead (999_498/999_499 and
// 999_949_998/999_949_999 pairs), and "999.9 MB" is demonstrated at 999_949_998, one below the real MB/GB boundary.
//
// None of this has run against macOS Foundation.framework (ED-086), so every test here is Oracle O-66 (R-TEST-6).
public sealed class SwiftFormattersThirdTests
{
    private const string EnUS = "en_US";

    [Fact]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-66")]
    public void Zero_is_spelled_out_with_the_kilobyte_unit()
    {
        // ByteTests.swift zeroSpelledOutKb (:22-33, en_US) and attributed() (:126-131): the zero branch does not
        // consult style (:153), so this is the same result the swift-foundation test gets for .memory.
        Assert.Equal("Zero kB", SwiftFormatters.ByteCountStyleFile(0L, EnUS));
    }

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-66")]
    [InlineData(1L, "1 byte")]       // ByteTests.swift attributed() :134-138, real, style .file
    [InlineData(998L, "998 bytes")]  // stays byte: 998 < 999
    public void The_byte_unit_uses_its_full_name_and_ICUs_plural_rule(long bytes, string expected) =>
        Assert.Equal(expected, SwiftFormatters.ByteCountStyleFile(bytes, EnUS));

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-66")]
    [InlineData(999L, "1 kB")]        // byte/kB boundary: 999 is not &lt; 999, so it is already kB
    [InlineData(1000L, "1 kB")]
    [InlineData(1024L, "1 kB")]       // ByteTests.swift actualByteCount() :115-117, real, style .file
    [InlineData(1500L, "2 kB")]       // half-even: 1.5 rounds to the nearest even integer, 2
    [InlineData(2500L, "2 kB")]       // half-even: 2.5 rounds to 2, not 3
    [InlineData(999_498L, "999 kB")]  // the last byte count that still rounds/stays under the kB threshold
    public void Kilobytes_have_no_fraction_digits_and_round_half_even(long bytes, string expected) =>
        Assert.Equal(expected, SwiftFormatters.ByteCountStyleFile(bytes, EnUS));

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-66")]
    [InlineData(999_499L, "1 MB")]      // kB/MB boundary: the kB threshold constant itself already selects MB
    [InlineData(1_100_000L, "1.1 MB")]  // ByteTests.swift attributed() :158-165, real, style .file
    [InlineData(1_250_000L, "1.2 MB")]  // half-even: 1.25 rounds to 1.2 (2 is even), not 1.3
    [InlineData(1_350_000L, "1.4 MB")]  // half-even: 1.35 rounds to 1.4 (4 is even), not 1.3
    [InlineData(999_949_998L, "999.9 MB")] // the last byte count that still stays MB (one below the MB/GB boundary)
    public void Megabytes_show_up_to_one_fraction_digit_trimmed_and_round_half_even(long bytes, string expected) =>
        Assert.Equal(expected, SwiftFormatters.ByteCountStyleFile(bytes, EnUS));

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-66")]
    [InlineData(999_949_999L, "1 GB")]     // MB/GB boundary: the MB threshold constant itself already selects GB
    [InlineData(999_950_000L, "1 GB")]
    [InlineData(1_234_567_890L, "1.23 GB")] // half-even: 1.23456789 rounds to 1.23, not 1.24
    [InlineData(999_994_999_998L, "999.99 GB")] // the last byte count that still stays GB (one below the GB/TB boundary)
    public void Gigabytes_show_up_to_two_fraction_digits_trimmed_and_round_half_even(long bytes, string expected) =>
        Assert.Equal(expected, SwiftFormatters.ByteCountStyleFile(bytes, EnUS));

    [Theory]
    [Trait("Rule", "R-FMT-2")]
    [Trait("Oracle", "O-66")]
    [InlineData(999_994_999_999L, "1 TB")]           // GB/TB boundary: the GB threshold constant selects TB
    [InlineData(999_994_999_999_998L, "999.99 TB")]  // the last byte count that still stays TB
    [InlineData(999_994_999_999_999L, "1 PB")]       // TB/PB boundary: the TB threshold constant selects PB, the
                                                      // practical ceiling (ByteTests.swift testEveryAllowedUnit
                                                      // :190-208: even allowedUnits eb/zb/ybOrHigher format as PB)
    [InlineData(10_000_000_000_000_000L, "10 PB")]
    public void Terabytes_and_petabytes_follow_the_same_precision_and_round_half_even(long bytes, string expected) =>
        Assert.Equal(expected, SwiftFormatters.ByteCountStyleFile(bytes, EnUS));
}

using System.Text;
using Nth.Build;

namespace Marrow.Core.Tests.Packaging;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 9 (build check "no user-visible Marrow in the setup's strings"), unit U7 (ED-NEW-inst-u7-6).
// A native-AOT exe holds its string literals as UTF-16 and its metadata as UTF-8, so the scan looks for the word as both, in any case,
// and does not count the first part of a dotted name (Marrow.Core.Support), which is a namespace a user never sees.
public sealed class BinaryTextScanTests
{
    private static string Binary(params (int At, byte[] Bytes)[] pieces)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nth-scan-").FullName, "binary.bin");
        var length = pieces.Length == 0 ? 0 : pieces.Max(p => p.At + p.Bytes.Length) + 64;
        var data = new byte[length];
        new Random(7).NextBytes(data);
        for (var i = 0; i < data.Length; i++) data[i] |= 0x80; // no accidental ASCII letters
        foreach (var (at, bytes) in pieces) Array.Copy(bytes, 0, data, at, bytes.Length);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] Wide(string text) => Encoding.Unicode.GetBytes(text);

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_word_is_found_as_ascii_and_as_utf16_in_any_case()
    {
        var path = Binary((100, Ascii("Marrow Code")), (400, Wide("Marrow")), (700, Ascii("MARROW")), (900, Wide("marrow")));

        var found = BinaryTextScan.FindWord(path, "Marrow");

        Assert.Equal(4, found.Count);
        Assert.Contains(found, f => f.StartsWith("ascii:", StringComparison.Ordinal) && f.Contains("Marrow Code", StringComparison.Ordinal));
        Assert.Contains(found, f => f.StartsWith("utf16:", StringComparison.Ordinal) && f.Contains("Marrow", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("@64", StringComparison.Ordinal)); // 100 = 0x64, the offset is reported in hex
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_first_part_of_a_dotted_name_is_a_namespace_and_is_not_counted_in_either_encoding()
    {
        var path = Binary((50, Ascii("Marrow.Core.Support.InstallerGate")), (300, Wide("Marrow.Setup")), (600, Ascii("marrow.app")));

        Assert.Empty(BinaryTextScan.FindWord(path, "Marrow"));
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void A_compound_name_and_a_dot_before_a_non_letter_are_counted()
    {
        var path = Binary((50, Ascii("MarrowSetup-3f9a")), (300, Wide("MarrowCode.Marrow")), (600, Ascii("Marrow.")), (700, Ascii("Marrow.1")));

        var found = BinaryTextScan.FindWord(path, "Marrow");

        Assert.Equal(5, found.Count); // MarrowSetup, both Marrow of MarrowCode.Marrow (the second ends its name), Marrow. and Marrow.1
    }

    [Theory]
    [Trait("Decision", "OD-0135")]
    [InlineData(1_048_576 - 3)]
    [InlineData(1_048_576 - 6)]
    [InlineData(1_048_576)]
    [InlineData(2_097_152 - 10)]
    [InlineData(1_048_576 + 64 - 5)]
    public void A_match_across_a_block_boundary_is_found_once(int at)
    {
        var path = Binary((at, Ascii("Marrow Code")), (at + 5_000_000, Wide("Marrow")));

        var found = BinaryTextScan.FindWord(path, "Marrow");

        Assert.Equal(2, found.Count);
        Assert.Contains(found, f => f.StartsWith("ascii:", StringComparison.Ordinal));
        Assert.Contains(found, f => f.StartsWith("utf16:", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void Each_encoding_can_be_read_on_its_own_because_aot_metadata_is_utf8_and_only_utf16_is_what_a_user_sees()
    {
        var path = Binary((100, Ascii("Marrow")), (400, Wide("Marrow Setup")), (700, Ascii("MarrowSetup-1")));

        Assert.Equal(3, BinaryTextScan.FindWord(path, "Marrow").Count);
        Assert.Equal(["ascii:", "ascii:"], BinaryTextScan.FindWord(path, "Marrow", ascii: true, utf16: false).Select(f => f[..6]).ToArray());
        Assert.Equal(["utf16:"], BinaryTextScan.FindWord(path, "Marrow", ascii: false, utf16: true).Select(f => f[..6]).ToArray());
        Assert.Empty(BinaryTextScan.FindWord(path, "Marrow", ascii: false, utf16: false));
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void A_file_without_the_word_and_an_empty_file_give_nothing()
    {
        Assert.Empty(BinaryTextScan.FindWord(Binary((10, Ascii("nth Setup")), (200, Wide("nth"))), "Marrow"));
        Assert.Empty(BinaryTextScan.FindWord(Binary(), "Marrow"));
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// R-STR rules (IMPLEMENTING.md 3.10). Every non-ASCII character is written as a \U escape (R-STR-9).
// Source: draft A (proto-A SwiftStringTests) and draft B (the three newline splitters).
public sealed class SwiftStringTests
{
    private const string Family = "\U0001F468\U0000200D\U0001F469\U0000200D\U0001F467"; // one Character, 8 UTF-16 units
    private const string Flags = "\U0001F1FA\U0001F1F8\U0001F1EB\U0001F1F7";            // two Characters
    private const string Decomposed = "e\U00000301";                                    // one Character
    private const string Composed = "\U000000E9";

    [Fact]
    [Trait("Rule", "R-STR-1")]
    [Trait("Decision", "ED-073")]
    public void Count_is_Characters_and_CRLF_is_one_Character()
    {
        Assert.Equal(3, SwiftString.Count(Flags + Decomposed));
        Assert.Equal(10, (Flags + Decomposed).Length);
        Assert.Equal(1, SwiftString.Count("\r\n"));
        Assert.Equal(1, SwiftString.Count(Family));
        Assert.Equal(18, SwiftString.Utf8Count(Family));
    }

    [Fact]
    [Trait("Rule", "R-STR-1")]
    public void Slicing_keeps_whole_Characters_and_a_negative_count_traps()
    {
        Assert.Equal(Decomposed, SwiftString.Prefix(Decomposed + "llo", 1));
        Assert.Equal("e", (Decomposed + "llo")[..1]); // the pitfall: half a Character
        Assert.Equal(Family, SwiftString.DropLast(Family + "x"));
        Assert.Equal("x", SwiftString.DropFirst(Family + "x"));
        Assert.Equal(Family + "x", SwiftString.Suffix(Family + "x", 2));
        Assert.Equal("", SwiftString.DropLast("ab", 5));
        Assert.Equal("ab", SwiftString.Prefix("ab", 5));
        Assert.Throws<SwiftTrapException>(() => SwiftString.Prefix("ab", -1));
    }

    [Fact]
    [Trait("Rule", "R-STR-2")]
    public void Equality_is_canonical_equivalence_also_for_set_and_dictionary_keys()
    {
        Assert.True(SwiftString.Equal(Composed, Decomposed));
        Assert.False(string.Equals(Composed, Decomposed, StringComparison.Ordinal));
        var keys = new HashSet<string>(SwiftStringComparer.Instance) { Composed };
        Assert.Contains(Decomposed, keys);
        Assert.DoesNotContain(Decomposed, new HashSet<string>(StringComparer.Ordinal) { Composed });
        Assert.True(SwiftString.Equal("\U0000212A", "K")); // KELVIN SIGN is canonically K
    }

    [Fact]
    [Trait("Rule", "R-STR-2")]
    [Trait("Rule", "R-COLL-1")]
    public void Ordering_is_by_scalar_value_not_UTF16_unit_and_default_OrderBy_is_cultural()
    {
        Assert.True(SwiftString.Compare("\U0000FFFF", "\U00010000") < 0);
        Assert.True(string.CompareOrdinal("\U0000FFFF", "\U00010000") > 0);
        Assert.Equal(0, SwiftString.Compare(Composed, Decomposed));
        string[] names = ["b", "\U00010000", "\U0000FFFF", "a", "B"];
        Assert.Equal(["B", "a", "b", "\U0000FFFF", "\U00010000"], names.OrderBy(n => n, SwiftStringComparer.Instance));
        Assert.Equal("a", names.OrderBy(n => n, StringComparer.InvariantCulture).First()); // the pitfall: "a" sorts before "B"
    }

    [Fact]
    [Trait("Rule", "R-STR-2")]
    public void HasPrefix_and_hasSuffix_compare_whole_Characters_canonically()
    {
        Assert.False(SwiftString.HasPrefix(Decomposed + "x", "e"));
        Assert.StartsWith("e", Decomposed + "x", StringComparison.Ordinal); // the pitfall
        Assert.True(SwiftString.HasPrefix(Composed + "x", Decomposed));
        Assert.False(SwiftString.HasPrefix(Family, "\U0001F468"));
        Assert.True(SwiftString.HasSuffix("x" + Decomposed, Composed));
        Assert.False(SwiftString.HasSuffix("x" + Decomposed, "\U00000301"));
        Assert.True(SwiftString.HasPrefix("abc", ""));
    }

    [Fact]
    [Trait("Rule", "R-STR-2")]
    [Trait("Oracle", "O-06")]
    public void A_prefix_or_suffix_that_splits_CRLF_does_not_match_even_on_ASCII()
    {
        Assert.False(SwiftString.HasPrefix("\r\nx", "\r"));
        Assert.True(SwiftString.HasPrefix("\r\nx", "\r\n"));
        Assert.False(SwiftString.HasSuffix("a\r\n", "\n"));
        Assert.True(SwiftString.HasSuffix("a\r\n", "\r\n"));
    }

    [Fact]
    [Trait("Rule", "R-STR-3")]
    public void The_three_line_splitters_disagree_on_CRLF()
    {
        const string text = "a\r\nb\n\nc\n";
        Assert.Equal(["a\r\nb", "c"], SwiftString.Split(text, "\n"));                  // split(separator: "\n")
        Assert.Equal(["a", "", "b", "", "c", ""], SwiftString.ComponentsSeparatedByNewlines(text)); // .newlines
        Assert.Equal(["a\r", "b", "", "c", ""], text.Split('\n'));                       // the .NET pitfall
    }

    [Fact]
    [Trait("Rule", "R-STR-3")]
    public void Split_follows_the_stdlib_for_maxSplits_and_empty_pieces()
    {
        Assert.Equal(["a", "b,c"], SwiftString.Split("a,b,c", ",", maxSplits: 1));
        Assert.Equal(["", "a", "", ""], SwiftString.Split(",a,,", ",", omittingEmptySubsequences: false));
        Assert.Equal(["a", ",b,,c"], SwiftString.Split(",,a,,b,,c", ",", maxSplits: 1));
        Assert.Empty(SwiftString.Split("", ","));
        Assert.Equal([""], SwiftString.Split("", ",", omittingEmptySubsequences: false));
    }

    [Fact]
    [Trait("Rule", "R-STR-4")]
    [Trait("Decision", "ED-073")]
    public void Case_mapping_is_full_and_locale_independent()
    {
        Assert.Equal("i\U00000307", SwiftString.Lowercased("\U00000130"));
        Assert.Equal("\U00000130", "\U00000130".ToLowerInvariant()); // the pitfall, observed on .NET 10
        Assert.Equal("SS", SwiftString.Uppercased("\U000000DF"));
        Assert.Equal("\U000000DF", "\U000000DF".ToUpperInvariant());
        Assert.Equal("FI", SwiftString.Uppercased("\U0000FB01"));
        Assert.Equal("ABC-XYZ", SwiftString.Uppercased("abc-xyz"));
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("i", SwiftString.Lowercased("I"));
            Assert.Equal("\U00000131", "I".ToLower(CultureInfo.CurrentCulture)); // the culture pitfall
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    [Trait("Rule", "R-STR-4")]
    [Trait("Oracle", "O-07")]
    public void Lowercase_maps_each_scalar_alone_so_no_final_sigma_applies()
    {
        // ED-009 named JavaScript's toLowerCase, which applies Final_Sigma; Swift's per-scalar mapping does not.
        Assert.Equal("\U000003C3\U000003B1\U000003C3", SwiftString.Lowercased("\U000003A3\U00000391\U000003A3"));
    }

    [Fact]
    [Trait("Rule", "R-STR-6")]
    [Trait("Oracle", "O-08")]
    public void LocalizedCaseInsensitiveContains_rejects_an_empty_needle_and_stays_accent_sensitive()
    {
        var english = CultureInfo.GetCultureInfo("en-US");
        Assert.False(SwiftString.LocalizedCaseInsensitiveContains("abc", ""));
        Assert.Equal(0, english.CompareInfo.IndexOf("abc", "", CompareOptions.IgnoreCase)); // the pitfall
        Assert.True(SwiftString.LocalizedCaseInsensitiveContains("Hello World", "WORLD", english));
        Assert.False(SwiftString.LocalizedCaseInsensitiveContains("caf" + Composed, "cafe", english));
    }

    [Fact]
    [Trait("Rule", "R-STR-5")]
    public void Character_properties_follow_Unicode_not_dotnet_categories()
    {
        Assert.True(SwiftCharacter.IsNumber("\U000000BD"));
        Assert.True(SwiftCharacter.IsNumber("\U00004E00"));
        Assert.False(char.IsNumber('\U00004E00'));
        Assert.True(SwiftCharacter.IsLetter("\U000024B6"));
        Assert.False(char.IsLetter('\U000024B6'));
        Assert.False(SwiftCharacter.IsLetter("1"));
        Assert.True(SwiftCharacter.IsWhitespace("\r\n"));
        Assert.False(SwiftCharacter.IsWhitespace("\U0000200B"));
        Assert.True(SwiftCharacter.IsNumber("7"));
    }

    [Fact]
    [Trait("Rule", "R-STR-7")]
    public void Trimming_uses_Foundation_sets_not_String_Trim()
    {
        Assert.Equal("x", FoundationText.Trim("\U0000200B x \U0000200B"));
        Assert.Equal("\U0000200B x \U0000200B", "\U0000200B x \U0000200B".Trim()); // the pitfall: U+200B is not .NET whitespace
        Assert.Equal("x", FoundationText.Trim("\n x\r\n", includeNewlines: true));
        Assert.Equal("\n x\r\n", FoundationText.Trim("\n x\r\n"));
    }

    [Fact]
    [Trait("Rule", "R-STR-8")]
    [Trait("Oracle", "O-15")]
    public void UTF8_decoding_has_a_failing_and_a_repairing_form()
    {
        byte[] invalid = [0x61, 0xFF, 0x62];
        Assert.Null(SwiftText.DecodeUtf8OrNil(invalid));
        Assert.Equal("a\U0000FFFDb", SwiftText.DecodeUtf8Repairing(invalid));
        Assert.Equal("\U0000FEFFa", SwiftText.DecodeUtf8OrNil([0xEF, 0xBB, 0xBF, 0x61])); // the BOM is kept (O-15)
    }

    [Fact]
    [Trait("Rule", "R-RE-2")]
    public void Dotnet_regex_can_end_inside_a_Character()
    {
        var text = "caf" + Decomposed;
        var match = Regex.Match(text, "[a-z]+", RegexOptions.CultureInvariant);
        Assert.Equal("cafe", match.Value);
        Assert.False(SwiftString.IsCharacterBoundary(text, match.Index + match.Length));
    }

    [Fact]
    [Trait("Rule", "R-RE-1")]
    public void Dotnet_dollar_matches_before_a_final_newline_so_the_port_writes_z()
    {
        Assert.Matches("abc$", "abc\n");
        Assert.DoesNotMatch(@"abc\z", "abc\n");
    }
}

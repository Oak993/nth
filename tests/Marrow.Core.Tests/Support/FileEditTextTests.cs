using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class FileEditTextTests
{
    [Fact]
    [Trait("Req", "PR-STORE-113")]
    public void Convenience_constructor_passes_the_path_and_all_three_computed_stats_through()
    {
        var edit = new FileEdit("folder/a.txt", "keep\nold\n", "keep\nnew\nmore\n");
        Assert.Equal("folder/a.txt", edit.Path);
        Assert.Equal("@@ -1,3 +1,4 @@\n keep\n-old\n+new\n+more\n ", edit.Diff);
        Assert.Equal(2, edit.Additions);
        Assert.Equal(1, edit.Deletions);
    }

    [Fact] // Text.swift:121-124 passes path straight into self.init(path:) without normalizing it.
    [Trait("Req", "PR-STORE-113")]
    public void Convenience_constructor_never_normalizes_the_path()
    {
        var edit = new FileEdit(" Folder/A.txt ", "old", "new");
        Assert.Equal(" Folder/A.txt ", edit.Path);
    }

    [Fact]
    [Trait("Req", "PR-STORE-113")]
    public void Over_cap_edit_keeps_the_counts_while_omitting_diff_from_storage()
    {
        var edit = new FileEdit("large", new string('a', 200001), "new");
        Assert.Null(edit.Diff);
        Assert.Equal(1, edit.Additions);
        Assert.Equal(1, edit.Deletions);
        Assert.Equal("{\"additions\":1,\"deletions\":1,\"path\":\"large\"}", Encoding.UTF8.GetString(Coding.Encode(edit)));
        Assert.Equal(edit, Coding.Decode<FileEdit>(Coding.Encode(edit)));
    }

    [Fact] // Carrier fields from Timeline.swift:202-207.
    [Trait("Rule", "R-CODE-3")] // The carrier Codable, not the PR-STORE-113 initializer.
    [Trait("Rule", "R-CODE-1")]
    public void Memberwise_defaults_and_exact_storage_keys_survive_a_round_trip()
    {
        Assert.Equal(new FileEdit("x", null, 0, 0), new FileEdit("x"));
        var edit = new FileEdit("x/y", "", long.MaxValue, -1);
        Assert.Equal("{\"additions\":9223372036854775807,\"deletions\":-1,\"diff\":\"\",\"path\":\"x\\/y\"}",
            Encoding.UTF8.GetString(Coding.Encode(edit)));
        Assert.Equal(edit, Coding.Decode<FileEdit>(Coding.Encode(edit)));
        Assert.Equal("null", Encoding.UTF8.GetString(Coding.Encode<FileEdit?>(null)));
    }

    [Theory] // Swift synthesized Codable requires nonoptional carrier fields.
    [Trait("Rule", "R-CODE-1")] // The carrier Codable, not the PR-STORE-113 initializer.
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"path\":\"x\",\"additions\":1}")]
    [InlineData("{\"path\":\"x\",\"deletions\":1}")]
    [InlineData("{\"path\":null,\"additions\":1,\"deletions\":1}")]
    [InlineData("{\"path\":\"x\",\"additions\":\"1\",\"deletions\":1}")]
    [InlineData("{\"path\":\"x\",\"additions\":1,\"deletions\":1,\"diff\":false}")]
    public void Invalid_persisted_edit_is_rejected_without_inventing_defaults(string json) =>
        Assert.Throws<JsonException>(() => Coding.Decode<FileEdit>(Encoding.UTF8.GetBytes(json)));

    [Fact]
    [Trait("Rule", "R-CODE-1")] // The carrier Codable, not the PR-STORE-113 initializer.
    public void Explicit_null_diff_and_integral_JSON_number_follow_the_storage_decoder()
    {
        var edit = Coding.Decode<FileEdit>("{\"path\":\"x\",\"diff\":null,\"additions\":1.0,\"deletions\":2}"u8);
        Assert.Equal(new FileEdit("x", null, 1, 2), edit);
    }

    [Theory]
    [Trait("Req", "PR-STORE-114")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("text")]
    public void Nil_if_empty_preserves_every_nonempty_string_by_identity(string text) =>
        Assert.Same(text, text.NilIfEmpty());

    [Fact]
    [Trait("Req", "PR-STORE-114")]
    public void Nil_if_empty_returns_null_for_only_the_empty_string() => Assert.Null("".NilIfEmpty());

    [Theory] // Text.swift:130-132: trimmingCharacters(in: .whitespacesAndNewlines), leaving inner text untouched.
    [Trait("Req", "PR-STORE-167")]
    [InlineData("  \t\r\nhello world\n\t ", "hello world")]
    [InlineData("kept  inner  spaces", "kept  inner  spaces")]
    [InlineData("   \r\n\t", "")]
    [InlineData("", "")]
    public void Trimmed_removes_leading_and_trailing_whitespace_and_newlines_only(string text, string expected) =>
        Assert.Equal(expected, text.Trimmed());


    // The Foundation set is an explicit table, not .NET's char.IsWhiteSpace: U+200B ZWSP is a member,
    // U+2060 WORD JOINER is not, and every .whitespaces / .newlines table member trims from both edges.
    // Built from code points at runtime (not \u escapes in the literal) to keep every character unambiguous.
    [Fact]
    [Trait("Req", "PR-STORE-167")]
    public void Trimmed_follows_the_Foundation_whitespacesAndNewlines_table_exactly()
    {
        foreach (var codePoint in new[] { 0x00A0, 0x200B, 0x3000, 0x2028, 0x2029, 0x0085 }) // .whitespaces / .newlines members
            Assert.Equal("x", Wrap(codePoint).Trimmed());
        Assert.Equal(Wrap(0x2060), Wrap(0x2060).Trimmed()); // WORD JOINER is in neither table

        static string Wrap(int codePoint) => new(new[] { (char)codePoint, 'x', (char)codePoint });
    }
}

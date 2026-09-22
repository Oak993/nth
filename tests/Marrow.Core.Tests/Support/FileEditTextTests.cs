using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class FileEditTextTests
{
    [Fact] // PR-STORE-113
    public void Convenience_constructor_passes_the_path_and_all_three_computed_stats_through()
    {
        var edit = new FileEdit("folder/a.txt", "keep\nold\n", "keep\nnew\nmore\n");
        Assert.Equal("folder/a.txt", edit.Path);
        Assert.Equal("@@ -1,3 +1,4 @@\n keep\n-old\n+new\n+more\n ", edit.Diff);
        Assert.Equal(2, edit.Additions);
        Assert.Equal(1, edit.Deletions);
    }

    [Fact] // PR-STORE-113
    public void Over_cap_edit_keeps_the_counts_while_omitting_diff_from_storage()
    {
        var edit = new FileEdit("large", new string('a', 200001), "new");
        Assert.Null(edit.Diff);
        Assert.Equal(1, edit.Additions);
        Assert.Equal(1, edit.Deletions);
        Assert.Equal("{\"additions\":1,\"deletions\":1,\"path\":\"large\"}", Encoding.UTF8.GetString(Coding.Encode(edit)));
        Assert.Equal(edit, Coding.Decode<FileEdit>(Coding.Encode(edit)));
    }

    [Fact] // PR-STORE-113; carrier fields from Timeline.swift:170-175.
    public void Memberwise_defaults_and_exact_storage_keys_survive_a_round_trip()
    {
        Assert.Equal(new FileEdit("x", null, 0, 0), new FileEdit("x"));
        var edit = new FileEdit("x/y", "", long.MaxValue, -1);
        Assert.Equal("{\"additions\":9223372036854775807,\"deletions\":-1,\"diff\":\"\",\"path\":\"x\\/y\"}",
            Encoding.UTF8.GetString(Coding.Encode(edit)));
        Assert.Equal(edit, Coding.Decode<FileEdit>(Coding.Encode(edit)));
        Assert.Equal("null", Encoding.UTF8.GetString(Coding.Encode<FileEdit?>(null)));
    }

    [Theory] // PR-STORE-113; Swift synthesized Codable requires nonoptional carrier fields.
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"path\":\"x\",\"additions\":1}")]
    [InlineData("{\"path\":\"x\",\"deletions\":1}")]
    [InlineData("{\"path\":null,\"additions\":1,\"deletions\":1}")]
    [InlineData("{\"path\":\"x\",\"additions\":\"1\",\"deletions\":1}")]
    [InlineData("{\"path\":\"x\",\"additions\":1,\"deletions\":1,\"diff\":false}")]
    public void Invalid_persisted_edit_is_rejected_without_inventing_defaults(string json) =>
        Assert.Throws<JsonException>(() => Coding.Decode<FileEdit>(Encoding.UTF8.GetBytes(json)));

    [Fact] // PR-STORE-113
    public void Explicit_null_diff_and_integral_JSON_number_follow_the_storage_decoder()
    {
        var edit = Coding.Decode<FileEdit>("{\"path\":\"x\",\"diff\":null,\"additions\":1.0,\"deletions\":2}"u8);
        Assert.Equal(new FileEdit("x", null, 1, 2), edit);
    }

    [Theory] // PR-STORE-114
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("text")]
    public void Nil_if_empty_preserves_every_nonempty_string_by_identity(string text) =>
        Assert.Same(text, text.NilIfEmpty());

    [Fact] // PR-STORE-114
    public void Nil_if_empty_returns_null_for_only_the_empty_string() => Assert.Null("".NilIfEmpty());
}

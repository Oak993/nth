using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using ChangedFile = Marrow.Core.Models.FileChangeSummary.File;

namespace Marrow.Core.Tests.Models;

// Source: DroppyCode/Core/Models/FileChangeSummary.swift (B2 1fbf81e), :1-15.
//
// Tests for the stored shape of a turn's change summary (PR-RUNTIME-264).
//
// Where the expectations come from, never from the C# under test:
// - The Swift type is a plain struct with synthesized Codable and Hashable (:4-15): a nested File with
//   `var path: String`, `var additions: Int`, `var deletions: Int`, `var isBinary: Bool` (:5-10), then
//   `let files: [File]`, `let additions: Int`, `let deletions: Int` (:12-14). No CodingKeys, no custom init(from:),
//   no default values, so every key is the property name, every field is required on decode (R-CODE-1), and the
//   memberwise initializer stores the totals it is given (nothing in this file computes them; the init(files:) that
//   sums them is FileChangeSummary+Diff.swift, PR-GIT-092, another unit).
// - The persisted bytes: the summary lives in ThreadDocument.items (the record's persistence field), which
//   Storage.swift:381-382 writes with JSONEncoder.storage and reads with JSONDecoder.storage (Coding.swift:19-34):
//   sorted keys, compact, "/" escaped as "\/", non-ASCII written as raw UTF-8, Int as an integer literal (R-CODE-3,
//   R-CODE-4, R-CODE-5). Ordinal key order of the fixed keys: additions < deletions < files, and
//   additions < deletions < isBinary < path.
// - The port API these tests assume (R-NAME-1, R-NAME-2, R-VAL-1, R-VAL-2, R-CODE-3): the sealed record
//   FileChangeSummary(ValueList<File> Files, long Additions, long Deletions) with the nested sealed record
//   File(string Path, long Additions, long Deletions, bool IsBinary), persisted through a System.Text.Json converter
//   so SwiftJson.Encode(value, Coding.StorageOptions, SwiftJsonStyle.Storage) and Coding.Decode<T> reach it.
//
// Scope: these tests prove the part of PR-RUNTIME-264 this file owns: the type, its exact keys, its strict decoding
// and its value equality. The rest of the record (TurnSummary.changes in Timeline.swift:259-267; finishTurn copying
// files.count and the totals, ThreadRuntime.swift:2384-2395; the nil changes of install's synthetic end marker,
// ThreadRuntime.swift:609; storeLegacyChanges filling only a nil, ThreadRuntime.swift:3751-3757) belongs to those
// files' units and is tested there: none of those types exists in the C# tree yet.
public sealed class FileChangeSummaryTests
{
    // A two-file summary, one of them binary, as the record's verification method asks.
    private static FileChangeSummary TwoFileSummary() => new(
        new ValueList<ChangedFile>([
            new ChangedFile("Sources/App.swift", 3, 1, false),
            new ChangedFile("Assets/icon.png", 0, 0, true),
        ]),
        3,
        1);

    // The bytes JSONEncoder.storage writes for TwoFileSummary(): sorted keys, "/" escaped, compact.
    private const string TwoFileStorageJson =
        "{\"additions\":3,\"deletions\":1,\"files\":[" +
        "{\"additions\":3,\"deletions\":1,\"isBinary\":false,\"path\":\"Sources\\/App.swift\"}," +
        "{\"additions\":0,\"deletions\":0,\"isBinary\":true,\"path\":\"Assets\\/icon.png\"}]}";

    private static string Encode(FileChangeSummary summary) =>
        Encoding.UTF8.GetString(SwiftJson.Encode(summary, Coding.StorageOptions, SwiftJsonStyle.Storage));

    private static FileChangeSummary Decode(string json) =>
        Coding.Decode<FileChangeSummary>(Encoding.UTF8.GetBytes(json))!;

    private static string DecodingErrorKind(string json) =>
        Assert.Throws<DecodingError>(() => Decode(json)).Kind;

    [Fact]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:4-15; Services/Store/Storage.swift:381-382
    public void A_two_file_summary_with_a_binary_file_is_stored_with_exactly_the_synthesized_keys()
    {
        var summary = TwoFileSummary();
        var json = Encode(summary);
        Assert.Equal(TwoFileStorageJson, json);

        // The exact key sets, independently of order: three on the summary, four on each file, nothing else.
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(new[] { "additions", "deletions", "files" }, root.EnumerateObject().Select(p => p.Name).ToArray());
        foreach (var file in root.GetProperty("files").EnumerateArray())
            Assert.Equal(new[] { "additions", "deletions", "isBinary", "path" }, file.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:4-15; Core/Support/Coding.swift:28-34
    public void The_stored_summary_reads_back_equal_file_by_file_and_in_order()
    {
        var reloaded = Decode(TwoFileStorageJson);
        Assert.Equal(TwoFileSummary(), reloaded);
        Assert.Equal(2, reloaded.Files.Count);
        Assert.Equal("Sources/App.swift", reloaded.Files[0].Path);
        Assert.Equal(3, reloaded.Files[0].Additions);
        Assert.Equal(1, reloaded.Files[0].Deletions);
        Assert.False(reloaded.Files[0].IsBinary);
        Assert.Equal("Assets/icon.png", reloaded.Files[1].Path);
        Assert.Equal(0, reloaded.Files[1].Additions);
        Assert.Equal(0, reloaded.Files[1].Deletions);
        Assert.True(reloaded.Files[1].IsBinary);
        Assert.Equal(3, reloaded.Additions);
        Assert.Equal(1, reloaded.Deletions);
        Assert.Equal(TwoFileStorageJson, Encode(reloaded));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:12-14
    public void The_totals_are_stored_as_given_and_never_recomputed_from_the_files()
    {
        // `let additions: Int` and `let deletions: Int` are stored properties set by the memberwise initializer.
        var summary = new FileChangeSummary(new ValueList<ChangedFile>([new ChangedFile("a.txt", 1, 2, false)]), 40, 7);
        Assert.Equal(40, summary.Additions);
        Assert.Equal(7, summary.Deletions);
        var json = Encode(summary);
        Assert.Equal("{\"additions\":40,\"deletions\":7,\"files\":[{\"additions\":1,\"deletions\":2,\"isBinary\":false,\"path\":\"a.txt\"}]}", json);
        var reloaded = Decode(json);
        Assert.Equal(40, reloaded.Additions);
        Assert.Equal(7, reloaded.Deletions);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:12-14
    public void An_empty_summary_is_stored_with_an_empty_files_array()
    {
        var empty = new FileChangeSummary(ValueList<ChangedFile>.Empty, 0, 0);
        Assert.Equal("{\"additions\":0,\"deletions\":0,\"files\":[]}", Encode(empty));
        Assert.Equal(empty, Decode("{\"files\":[],\"additions\":0,\"deletions\":0}"));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:6; Core/Support/Coding.swift:19-26
    public void A_path_keeps_non_ASCII_as_raw_UTF8_and_escapes_only_the_slash()
    {
        // U+00E9, U+2019, U+2014, U+2026 and U+1F600 are written raw; "/" is "\/" (no .withoutEscapingSlashes).
        const string path = "Docs/café ’notes—v2… \U0001F600.md";
        var summary = new FileChangeSummary(new ValueList<ChangedFile>([new ChangedFile(path, 2, 0, false)]), 2, 0);
        var bytes = SwiftJson.Encode(summary, Coding.StorageOptions, SwiftJsonStyle.Storage);
        var expected = Encoding.UTF8.GetBytes(
            "{\"additions\":2,\"deletions\":0,\"files\":[{\"additions\":2,\"deletions\":0,\"isBinary\":false," +
            "\"path\":\"Docs\\/café ’notes—v2… \U0001F600.md\"}]}");
        Assert.Equal(expected, bytes);
        // The byte sequences themselves: U+2019 is E2 80 99, U+2014 is E2 80 94, U+2026 is E2 80 A6.
        Assert.Contains("café ’notes—v2…", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        ReadOnlySpan<byte> rightQuote = [0xE2, 0x80, 0x99];
        ReadOnlySpan<byte> emDash = [0xE2, 0x80, 0x94];
        ReadOnlySpan<byte> ellipsis = [0xE2, 0x80, 0xA6];
        ReadOnlySpan<byte> grin = [0xF0, 0x9F, 0x98, 0x80];
        Assert.True(bytes.AsSpan().IndexOf(rightQuote) >= 0);
        Assert.True(bytes.AsSpan().IndexOf(emDash) >= 0);
        Assert.True(bytes.AsSpan().IndexOf(ellipsis) >= 0);
        Assert.True(bytes.AsSpan().IndexOf(grin) >= 0);
        Assert.Equal(path, Decode(Encoding.UTF8.GetString(bytes)).Files[0].Path);
    }

    [Theory]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:12-14: synthesized, every key required
    [InlineData("{\"additions\":3,\"deletions\":1}")]
    [InlineData("{\"files\":[],\"deletions\":1}")]
    [InlineData("{\"files\":[],\"additions\":3}")]
    [InlineData("{\"Files\":[],\"additions\":3,\"deletions\":1}")]
    [InlineData("{}")]
    public void A_summary_missing_a_key_is_keyNotFound(string json) =>
        Assert.Equal("keyNotFound", DecodingErrorKind(json));

    [Theory]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:6-9: synthesized, every key required
    [InlineData("{\"additions\":1,\"deletions\":0,\"isBinary\":false}")]
    [InlineData("{\"path\":\"a\",\"deletions\":0,\"isBinary\":false}")]
    [InlineData("{\"path\":\"a\",\"additions\":1,\"isBinary\":false}")]
    [InlineData("{\"path\":\"a\",\"additions\":1,\"deletions\":0}")]
    [InlineData("{\"path\":\"a\",\"additions\":1,\"deletions\":0,\"binary\":false}")]
    public void A_file_missing_a_key_is_keyNotFound(string file) =>
        Assert.Equal("keyNotFound", DecodingErrorKind("{\"files\":[" + file + "],\"additions\":1,\"deletions\":0}"));

    [Theory]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:6-14: non-optional, so null is valueNotFound
    [InlineData("{\"files\":null,\"additions\":0,\"deletions\":0}")]
    [InlineData("{\"files\":[],\"additions\":null,\"deletions\":0}")]
    [InlineData("{\"files\":[],\"additions\":0,\"deletions\":null}")]
    [InlineData("{\"files\":[null],\"additions\":0,\"deletions\":0}")]
    [InlineData("{\"files\":[{\"path\":null,\"additions\":0,\"deletions\":0,\"isBinary\":false}],\"additions\":0,\"deletions\":0}")]
    [InlineData("{\"files\":[{\"path\":\"a\",\"additions\":null,\"deletions\":0,\"isBinary\":false}],\"additions\":0,\"deletions\":0}")]
    [InlineData("{\"files\":[{\"path\":\"a\",\"additions\":0,\"deletions\":null,\"isBinary\":false}],\"additions\":0,\"deletions\":0}")]
    [InlineData("{\"files\":[{\"path\":\"a\",\"additions\":0,\"deletions\":0,\"isBinary\":null}],\"additions\":0,\"deletions\":0}")]
    public void A_null_value_is_valueNotFound(string json) =>
        Assert.Equal("valueNotFound", DecodingErrorKind(json));

    [Theory]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:6-14: JSONDecoder's type rules (R-CODE-1)
    [InlineData("[]")]
    [InlineData("{\"files\":{},\"additions\":0,\"deletions\":0}")]
    [InlineData("{\"files\":\"a\",\"additions\":0,\"deletions\":0}")]
    [InlineData("{\"files\":[],\"additions\":\"3\",\"deletions\":0}")]
    [InlineData("{\"files\":[],\"additions\":0,\"deletions\":true}")]
    [InlineData("{\"files\":[\"a\"],\"additions\":0,\"deletions\":0}")]
    [InlineData("{\"files\":[{\"path\":1,\"additions\":0,\"deletions\":0,\"isBinary\":false}],\"additions\":0,\"deletions\":0}")]
    [InlineData("{\"files\":[{\"path\":\"a\",\"additions\":\"0\",\"deletions\":0,\"isBinary\":false}],\"additions\":0,\"deletions\":0}")]
    [InlineData("{\"files\":[{\"path\":\"a\",\"additions\":0,\"deletions\":0,\"isBinary\":0}],\"additions\":0,\"deletions\":0}")]
    [InlineData("{\"files\":[{\"path\":\"a\",\"additions\":0,\"deletions\":0,\"isBinary\":\"true\"}],\"additions\":0,\"deletions\":0}")]
    public void A_wrong_type_is_typeMismatch(string json) =>
        Assert.Equal("typeMismatch", DecodingErrorKind(json));

    [Theory]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:7-8,13-14: Int rejects a fraction
    [InlineData("{\"files\":[],\"additions\":1.5,\"deletions\":0}")]
    [InlineData("{\"files\":[{\"path\":\"a\",\"additions\":0,\"deletions\":2.25,\"isBinary\":false}],\"additions\":0,\"deletions\":0}")]
    public void A_fractional_count_is_dataCorrupted(string json) =>
        Assert.Equal("dataCorrupted", DecodingErrorKind(json));

    [Fact]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:4-15: synthesized decoding ignores unknown keys
    public void Unknown_keys_are_ignored_and_key_order_does_not_matter_and_integral_numbers_read_as_Int()
    {
        const string json =
            "{\"deletions\":1e0,\"note\":\"x\",\"files\":[" +
            "{\"isBinary\":false,\"diff\":\"@@\",\"path\":\"Sources/App.swift\",\"deletions\":1,\"additions\":3.0}," +
            "{\"path\":\"Assets/icon.png\",\"isBinary\":true,\"additions\":0,\"deletions\":0}],\"additions\":3}";
        Assert.Equal(TwoFileSummary(), Decode(json));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:4-5: synthesized Hashable
    public void Summaries_with_the_same_contents_are_equal_and_hash_alike()
    {
        var first = TwoFileSummary();
        var second = TwoFileSummary();
        Assert.NotSame(first, second);
        Assert.NotSame(first.Files, second.Files);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(first.Files[1], new ChangedFile("Assets/icon.png", 0, 0, true));
        Assert.Equal(first.Files[1].GetHashCode(), new ChangedFile("Assets/icon.png", 0, 0, true).GetHashCode());
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:4-15: every stored property takes part in ==
    public void Summaries_that_differ_in_any_field_or_in_file_order_are_not_equal()
    {
        var baseline = TwoFileSummary();
        var a = new ChangedFile("Sources/App.swift", 3, 1, false);
        var b = new ChangedFile("Assets/icon.png", 0, 0, true);

        Assert.NotEqual(baseline, new FileChangeSummary(new ValueList<ChangedFile>([b, a]), 3, 1));
        Assert.NotEqual(baseline, new FileChangeSummary(new ValueList<ChangedFile>([a]), 3, 1));
        Assert.NotEqual(baseline, new FileChangeSummary(new ValueList<ChangedFile>([a, b]), 4, 1));
        Assert.NotEqual(baseline, new FileChangeSummary(new ValueList<ChangedFile>([a, b]), 3, 2));
        Assert.NotEqual(baseline, new FileChangeSummary(new ValueList<ChangedFile>([a, b with { IsBinary = false }]), 3, 1));
        Assert.NotEqual(baseline, new FileChangeSummary(new ValueList<ChangedFile>([a with { Path = "Sources/app.swift" }, b]), 3, 1));
        Assert.NotEqual(baseline, new FileChangeSummary(new ValueList<ChangedFile>([a with { Additions = 2 }, b]), 3, 1));
        Assert.NotEqual(baseline, new FileChangeSummary(new ValueList<ChangedFile>([a with { Deletions = 0 }, b]), 3, 1));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-264")] // Core/Models/FileChangeSummary.swift:5-10: File is a value type with var fields
    public void Changing_a_copy_of_a_file_leaves_the_original_and_its_summary_unchanged()
    {
        var summary = TwoFileSummary();
        var original = summary.Files[0];
        var copy = original with { Additions = 99, IsBinary = true };
        Assert.Equal(3, original.Additions);
        Assert.False(original.IsBinary);
        Assert.Equal(99, copy.Additions);
        Assert.True(copy.IsBinary);
        Assert.Equal(TwoFileSummary(), summary);
        Assert.Equal(TwoFileStorageJson, Encode(summary));
    }
}

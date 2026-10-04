using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-1 (first-release voice, OD-0092 and its amendment, OD-0099 Q-V-14): the model catalogue and its embedded manifest.
// Expected values are read from voice-SPEC.md V-9.1 / V-9.2 / V-9.6, which carry them (R-TEST-4), not from the port.
public sealed class ModelCatalogTests
{
    private const string Commit = "5359861c739e955e79d9a303bcbc70fb988958b1";

    private static List<string[]> SpecRows() => VoiceSpecFile.TableRows("| Id (file stem)", "File name = ").Skip(1).ToList();

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T1")]
    [Trait("Decision", "ED-267")] // OD-0092, voice-SPEC V-3.3, V-9.1
    public void The_manifest_has_the_29_files_with_the_ids_bytes_and_sha256_of_V_9_1_in_order()
    {
        var rows = SpecRows();
        Assert.Equal(29, rows.Count);
        Assert.Equal(29, ModelCatalog.Files.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var file = ModelCatalog.Files[i];
            Assert.Equal(rows[i][0], file.Id);
            Assert.Equal(long.Parse(rows[i][4], System.Globalization.CultureInfo.InvariantCulture), file.Bytes);
            Assert.Equal(rows[i][5], file.Sha256);
            Assert.Equal(64, file.Sha256.Length);
        }
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T1")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-3.3: the Hugging Face commit is pinned; the file name is ggml-<id>.bin
    public void The_hugging_face_commit_is_pinned_and_every_url_is_built_from_it()
    {
        Assert.Equal(Commit, ModelCatalog.HuggingFaceCommit);
        foreach (var file in ModelCatalog.Files)
        {
            Assert.Equal("ggml-" + file.Id + ".bin", file.FileName);
            Assert.Equal("https://huggingface.co/ggerganov/whisper.cpp/resolve/" + Commit + "/ggml-" + file.Id + ".bin", file.Url);
        }
        Assert.Equal("https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-large-v3-turbo-q8_0.bin",
            ModelCatalog.Find("large-v3-turbo-q8_0")!.Url);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T1")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-9.1: ids are unique; large-v1, large-v2 and their quantizations are not offered
    public void Ids_are_unique_and_the_unoffered_large_models_are_absent()
    {
        Assert.Equal(29, ModelCatalog.Files.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(29, ModelCatalog.Files.Select(f => f.Sha256).Distinct(StringComparer.Ordinal).Count());
        Assert.Null(ModelCatalog.Find("large-v1"));
        Assert.Null(ModelCatalog.Find("large-v2"));
        Assert.Null(ModelCatalog.Find("large-v2-q5_0"));
        Assert.Null(ModelCatalog.Find("nope"));
        Assert.Null(ModelCatalog.Find(null));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T12")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-9.1 display names, version names, English-only column
    public void Each_file_has_the_model_name_version_and_english_flag_of_its_row()
    {
        foreach (var row in SpecRows())
        {
            var file = ModelCatalog.Find(row[0])!;
            Assert.Equal(row[1], file.Model);
            Assert.Equal(row[2], file.Version.ToString());
            Assert.Equal(row[3] == "yes", file.EnglishOnly);
        }
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T12")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-9.1 (q5 Compact, q8 Balanced, f16 Full) and the model list order of V-16.3.3 (6 rows)
    public void The_six_models_come_in_the_designs_order_with_speed_and_accuracy_dots_and_english_only_availability()
    {
        var expected = new (string Name, int Speed, int Accuracy, bool EnglishOnly)[]
        {
            ("Tiny", 5, 1, true), ("Base", 5, 2, true), ("Small", 4, 3, true), ("Medium", 2, 4, true),
            ("Large v3 Turbo", 4, 5, false), ("Large v3", 1, 5, false),
        };
        Assert.Equal(expected, ModelCatalog.Models.Select(m => (m.Name, m.Speed, m.Accuracy, m.HasEnglishOnly)).ToArray());
        foreach (var model in ModelCatalog.Models)
        {
            Assert.Equal(model.HasEnglishOnly, ModelCatalog.FilesOf(model.Name).Any(f => f.EnglishOnly));
            Assert.Equal(model.Name, ModelCatalog.ModelOf(ModelCatalog.FilesOf(model.Name)[0].Id).Name);
        }
        Assert.Equal(29, ModelCatalog.Models.Sum(m => ModelCatalog.FilesOf(m.Name).Count));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T12")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-9.1 notes, first match wins; OD-0099 / ED-267 (4) Q-V-14: the "can also translate" notes are dropped
    public void Model_notes_are_the_designs_with_the_translate_notes_dropped_for_Medium_and_Large_v3()
    {
        const string english = "English only is a little more accurate for English and knows no other language.";
        foreach (var name in new[] { "Tiny", "Base", "Small" }) Assert.Equal(english, ModelCatalog.Model(name).Note);
        Assert.Equal("About as accurate as Large v3, far quicker; it does not translate.", ModelCatalog.Model("Large v3 Turbo").Note);
        Assert.Null(ModelCatalog.Model("Medium").Note);
        Assert.Null(ModelCatalog.Model("Large v3").Note);
        Assert.DoesNotContain(ModelCatalog.Models, m => m.Note is not null && m.Note.Contains("can also translate", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T12")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-9.2: MB = bytes / 1,000,000; GB with fmt(.,2) when MB >= 1000, else round(MB) MB
    [InlineData("tiny-q5_1", "32 MB")]
    [InlineData("tiny-q8_0", "44 MB")]
    [InlineData("large-v3-turbo-q5_0", "574 MB")]
    [InlineData("large-v3-turbo-q8_0", "874 MB")]
    [InlineData("medium", "1.53 GB")]
    [InlineData("large-v3-turbo", "1.62 GB")]
    [InlineData("large-v3", "3.1 GB")]
    public void Sizes_are_shown_as_the_designs_examples(string id, string shown) =>
        Assert.Equal(shown, ModelCatalog.SizeText(ModelCatalog.Find(id)!.Bytes));

    [Theory]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T12")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-9.2 and SPEC 1 CZ-12.10: JavaScript Math.round (half up), shortest digits, no trailing zeros
    [InlineData(0L, "0 MB")]
    [InlineData(499_999L, "0 MB")]
    [InlineData(500_000L, "1 MB")]
    [InlineData(999_499_999L, "999 MB")]
    [InlineData(999_500_000L, "1000 MB")]
    [InlineData(1_000_000_000L, "1 GB")]
    [InlineData(1_004_999_999L, "1 GB")]
    [InlineData(1_005_000_000L, "1 GB")] // 1.005 * 100 is 100.49999999999999 in binary floating point, so JavaScript rounds it down
    [InlineData(1_016_000_000L, "1.02 GB")]
    [InlineData(1_125_000_000L, "1.13 GB")]
    [InlineData(1_500_000_000L, "1.5 GB")]
    [InlineData(3_095_033_483L, "3.1 GB")]
    public void Size_rounding_follows_JavaScript_half_up_and_drops_trailing_zeros(long bytes, string shown) =>
        Assert.Equal(shown, ModelCatalog.SizeText(bytes));

    [Theory]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T13")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-9.6 and its examples
    [InlineData("large-v3-turbo-q8_0", "Large v3 Turbo, balanced 874 MB")]
    [InlineData("small.en-q5_1", "Small (English only), compact 190 MB")]
    [InlineData("large-v3", "Large v3, full 3.1 GB")]
    [InlineData("tiny", "Tiny, full 78 MB")]
    [InlineData("medium.en-q8_0", "Medium (English only), balanced 823 MB")]
    public void Model_names_are_model_english_only_version_and_size(string id, string name) =>
        Assert.Equal(name, ModelCatalog.ModelName(id));

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T13")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-9.6: <Model> + (en ? ' (English only)') + ', ' + <version lower case> + ' ' + <size>, for all 29
    public void Every_model_name_is_built_by_the_same_rule()
    {
        foreach (var row in SpecRows())
        {
            var file = ModelCatalog.Find(row[0])!;
            var mb = file.Bytes / 1_000_000.0;
            var size = mb >= 1000 ? (Math.Floor(mb / 1000 * 100 + 0.5) / 100).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " GB"
                : Math.Floor(mb + 0.5).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " MB";
            var expected = row[1] + (row[3] == "yes" ? " (English only)" : "") + ", " + row[2].ToLowerInvariant() + " " + size;
            Assert.Equal(expected, ModelCatalog.ModelName(row[0]));
        }
        Assert.Null(ModelCatalog.ModelName("nope"));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T13")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.3.3: the version button reads {Version}, {size} + ", English only" (the row's pieces)
    public void The_name_without_version_and_size_is_the_model_with_its_english_only_mark()
    {
        Assert.Equal("Base (English only)", ModelCatalog.ShortName("base.en-q8_0"));
        Assert.Equal("Large v3 Turbo", ModelCatalog.ShortName("large-v3-turbo-q5_0"));
        Assert.Null(ModelCatalog.ShortName("nope"));
    }
}

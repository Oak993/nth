using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-14 (first-release voice, voice-SPEC V-16.3.3, V-16.3.4, V-9.2, V-T94, V-T96; OD-0099 Q-V-14): what the Speech model list says about a model and its variants.
// The expectations are written out from V-16.3.3 / V-9.1 / V-9.2, not read back from the code.
public sealed class SpeechModelListTests
{
    [Theory]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3: the version button reads `{Version}, {size}` + `, English only`
    [InlineData("large-v3-turbo-q8_0", "Balanced, 874 MB")]
    [InlineData("large-v3-turbo-q5_0", "Compact, 574 MB")]
    [InlineData("tiny.en-q5_1", "Compact, 32 MB, English only")]
    [InlineData("medium", "Full, 1.53 GB")]
    [InlineData("medium.en-q8_0", "Balanced, 823 MB, English only")]
    [InlineData("large-v3", "Full, 3.1 GB")]
    public void The_version_button_reads_version_size_and_English_only(string id, string expected) =>
        Assert.Equal(expected, SpeechModelList.Meta(ModelCatalog.Find(id)!));

    [Theory]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3: a segment of the version control reads `{Version} · {size}` (U+00B7)
    [InlineData("small-q5_1", "Compact · 190 MB")]
    [InlineData("small-q8_0", "Balanced · 264 MB")]
    [InlineData("small", "Full · 488 MB")]
    [InlineData("large-v3-q5_0", "Compact · 1.08 GB")]
    public void A_segment_reads_version_dot_size(string id, string expected) =>
        Assert.Equal(expected, SpeechModelList.SegmentText(ModelCatalog.Find(id)!));

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3: the English only switch does not change what a segment says, so one version control serves both forms
    public void The_English_only_form_of_a_version_shows_the_same_segment_text()
    {
        foreach (var model in ModelCatalog.Models.Where(m => m.HasEnglishOnly))
            foreach (var version in SpeechModelList.VersionsOf(model.Name))
            {
                var multi = SpeechModelList.File(model.Name, new ModelRowChoice(version, false));
                var english = SpeechModelList.File(model.Name, new ModelRowChoice(version, true));
                Assert.False(multi.EnglishOnly);
                Assert.True(english.EnglishOnly);
                Assert.Equal(SpeechModelList.SegmentText(multi), SpeechModelList.SegmentText(english));
            }
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-9.1: Tiny to Large v3 Turbo have Compact, Balanced and Full; Large v3 has Compact and Full only
    public void Each_model_offers_the_versions_the_manifest_has()
    {
        var all = new[] { ModelVersion.Compact, ModelVersion.Balanced, ModelVersion.Full };
        foreach (var name in new[] { "Tiny", "Base", "Small", "Medium", "Large v3 Turbo" }) Assert.Equal(all, SpeechModelList.VersionsOf(name));
        Assert.Equal([ModelVersion.Compact, ModelVersion.Full], SpeechModelList.VersionsOf("Large v3"));
        foreach (var model in ModelCatalog.Models)
            foreach (var version in SpeechModelList.VersionsOf(model.Name))
                Assert.Equal(model.Name, SpeechModelList.File(model.Name, new ModelRowChoice(version, false)).Model);
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3: English only exists for Tiny to Medium; a model without it ignores the flag
    public void A_model_without_an_English_only_form_ignores_the_flag()
    {
        var file = SpeechModelList.File("Large v3 Turbo", new ModelRowChoice(ModelVersion.Balanced, true));
        Assert.Equal("large-v3-turbo-q8_0", file.Id);
        Assert.Equal("small.en-q8_0", SpeechModelList.File("Small", new ModelRowChoice(ModelVersion.Balanced, true)).Id);
        Assert.Equal("medium-q5_0", SpeechModelList.File("Medium", new ModelRowChoice(ModelVersion.Compact, false)).Id);
        Assert.Equal("large-v3-q5_0", SpeechModelList.File("Large v3", new ModelRowChoice(ModelVersion.Compact, false)).Id);
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3: the meters are accessible as `{name}: speed {n} of 5, accuracy {m} of 5`
    public void The_meters_are_named_with_both_scores()
    {
        Assert.Equal("Large v3 Turbo: speed 4 of 5, accuracy 5 of 5", SpeechModelList.MeterName(ModelCatalog.Model("Large v3 Turbo")));
        Assert.Equal("Tiny: speed 5 of 5, accuracy 1 of 5", SpeechModelList.MeterName(ModelCatalog.Model("Tiny")));
        Assert.Equal("Large v3: speed 1 of 5, accuracy 5 of 5", SpeechModelList.MeterName(ModelCatalog.Model("Large v3")));
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3 (ED): the version shown in each row is the in-use variant, else the recommended variant, else Compact
    public void A_row_shows_the_in_use_variant_else_the_recommended_one_else_Compact()
    {
        Assert.Equal(new ModelRowChoice(ModelVersion.Balanced, false), SpeechModelList.InitialChoice("Large v3 Turbo", "large-v3-turbo-q8_0", "large-v3-turbo-q5_0"));
        Assert.Equal(new ModelRowChoice(ModelVersion.Compact, false), SpeechModelList.InitialChoice("Large v3 Turbo", "small.en-q8_0", "large-v3-turbo-q5_0"));
        Assert.Equal(new ModelRowChoice(ModelVersion.Balanced, true), SpeechModelList.InitialChoice("Small", "small.en-q8_0", "small-q5_1"));
        Assert.Equal(new ModelRowChoice(ModelVersion.Balanced, true), SpeechModelList.InitialChoice("Small", null, "small.en-q8_0"));
        Assert.Equal(new ModelRowChoice(ModelVersion.Compact, false), SpeechModelList.InitialChoice("Base", "large-v3-turbo-q8_0", "large-v3-turbo-q5_0"));
        Assert.Equal(new ModelRowChoice(ModelVersion.Compact, false), SpeechModelList.InitialChoice("Medium", null, null));
        Assert.Equal(new ModelRowChoice(ModelVersion.Full, false), SpeechModelList.InitialChoice("Large v3", "large-v3", null));
        Assert.Equal(new ModelRowChoice(ModelVersion.Compact, false), SpeechModelList.InitialChoice("Tiny", "not-a-model", "also-not"));
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T96")]
    [Trait("Decision", "ED-267")] // V-16.3.4 and V-9.2: `Installed: {size}, in Marrow Code’s models folder` with the sizes formatted as V-9.2 does
    public void The_footer_totals_the_installed_bytes_with_the_sizes_of_V_9_2()
    {
        Assert.Equal("Installed: 0 MB, in nth’s models folder", SpeechModelList.Footer(0));
        Assert.Equal("Installed: 574 MB, in nth’s models folder", SpeechModelList.Footer(574041195));
        Assert.Equal("Installed: 1.45 GB, in nth’s models folder", SpeechModelList.Footer(574041195 + 874188075));
        Assert.Equal("Installed: 3.1 GB, in nth’s models folder", SpeechModelList.Footer(3095033483));
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T90")]
    [Trait("Decision", "ED-267")] // V-16.3.3: the card's caption and subtitle
    public void The_caption_and_subtitle_are_the_designs()
    {
        Assert.Equal("Whisper", SpeechModelList.Caption);
        Assert.Equal("OpenAI, 99 languages. Speed as on a graphics card; the processor alone is slower", SpeechModelList.Subtitle);
    }
}

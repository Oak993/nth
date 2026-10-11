using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-2: writing catalogue.json. The pin run (the lead's, by hand) produces a new catalogue.json that SK-1's loader and gate must accept, so the writer is the
// loader's exact inverse (strict schema, camelCase, enums as words, a required key with no value written as an explicit null, an optional one left out when empty),
// canonical (one catalogue, one text) and independent of the machine's culture. The pin state is derived from the values (SK-1's rule: "pinned" only when nothing is
// missing), never typed by hand.
public sealed class SkillsCatalogueWriterTests
{
    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_embedded_catalogue_written_and_read_back_has_no_change_in_any_field_and_the_text_is_stable()
    {
        var text = SkillsCatalogueWriter.Serialize(CatalogueFixtures.Real);
        var back = SkillsCatalogueLoader.Parse(text);
        var report = CatalogueChangeReport.Compare(CatalogueFixtures.Real, back);
        Assert.Empty(report.Items);
        Assert.Equal(CatalogueFixtures.Real.Items.Count, back.Items.Count);
        Assert.Equal(text, SkillsCatalogueWriter.Serialize(back));
        Assert.Empty(CatalogueGate.Check(back).Violations);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_fully_pinned_fixture_round_trips_with_every_field()
    {
        // ED-NEW-dl-11: the release gate also wants each runnable item's installer parameters and licence pin, so the full round trip uses the fixture that carries them.
        var text = SkillsCatalogueWriter.Serialize(CatalogueFixtures.Installable());
        var back = SkillsCatalogueLoader.Parse(text);
        Assert.Empty(CatalogueChangeReport.Compare(CatalogueFixtures.Installable(), back).Items);
        Assert.Equal(text, SkillsCatalogueWriter.Serialize(back));
        Assert.Empty(CatalogueGate.Check(back, new CatalogueGateOptions { RequireAllPinned = true }).Violations);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void One_catalogue_has_one_text_whatever_the_order_of_building_it_up_and_ends_with_a_line_feed()
    {
        var text = SkillsCatalogueWriter.Serialize(CatalogueFixtures.Pinned());
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.False(text.EndsWith("\n\n", StringComparison.Ordinal));
        Assert.DoesNotContain('\r', text);
        Assert.StartsWith("{\n  \"schema\": 1,\n  \"readOn\": \"2026-10-04\",\n  \"items\": [\n    {\n      \"id\": \"X1\",\n      \"kind\": \"tool\",\n", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Required_keys_with_no_value_are_written_as_null_and_empty_optional_ones_are_left_out()
    {
        var bare = CatalogueFixtures.Pinned() with
        {
            Items =
            [
                CatalogueFixtures.PinnedTool() with { Pin = null, LicenceName = null, LicenceUrl = null, LicenceFile = null, ApproxSizeMb = null, PinState = PinState.Unpinned,
                    Artifacts = [new CatalogueArtifact { Name = "tool.zip", Url = null, Size = null, Sha256 = null, Sha256Hint = null }] },
                CatalogueFixtures.PinnedSkillset() with { PinHint = "0123456", LicenceNotes = "a note", Skills = [new CatalogueSkill { Folder = "alpha-skill", Name = null, Path = null, Needs = [] }, new CatalogueSkill { Folder = "beta", Name = "beta", Path = "skills/beta", Needs = [], TermsClass = TermsClass.None, Pin = CatalogueFixtures.FixturePin }] },
            ],
        };
        var root = JsonNode.Parse(SkillsCatalogueWriter.Serialize(bare))!;
        var tool = root["items"]![0]!.AsObject();
        foreach (var key in new[] { "pin", "licenceName", "licenceUrl", "licenceFile", "approxSizeMb" })
        {
            Assert.True(tool.ContainsKey(key), key);
            Assert.Null(tool[key]);
        }
        foreach (var key in new[] { "skills", "folders", "packages", "skillsListed", "skillCount", "pinHint", "licenceNotes" }) Assert.False(tool.ContainsKey(key), key);
        foreach (var key in new[] { "needs", "files", "provides", "artifacts" }) Assert.True(tool.ContainsKey(key), key);
        var artifact = tool["artifacts"]![0]!.AsObject();
        Assert.False(artifact.ContainsKey("sha256Hint"));
        foreach (var key in new[] { "url", "size", "sha256" })
        {
            Assert.True(artifact.ContainsKey(key), key);
            Assert.Null(artifact[key]);
        }
        var skills = root["items"]![1]!.AsObject();
        Assert.Equal("0123456", (string)skills["pinHint"]!);
        Assert.Equal("a note", (string)skills["licenceNotes"]!);
        Assert.True((bool)skills["skillsListed"]!);
        Assert.Equal(2, (int)skills["skillCount"]!);
        var first = skills["skills"]![0]!.AsObject();
        Assert.True(first.ContainsKey("name"));
        Assert.Null(first["name"]);
        Assert.True(first.ContainsKey("path"));
        Assert.False(first.ContainsKey("termsClass"));
        Assert.False(first.ContainsKey("pin"));
        var second = skills["skills"]![1]!.AsObject();
        Assert.Equal("none", (string)second["termsClass"]!);
        Assert.Equal(CatalogueFixtures.FixturePin, (string)second["pin"]!);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Enums_are_written_as_their_camel_case_words_and_numbers_as_invariant_numbers_and_text_is_not_escaped_needlessly()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var catalogue = CatalogueFixtures.Pinned() with
            {
                Items =
                [
                    CatalogueFixtures.PinnedTool() with { ApproxSizeMb = 8.1, Provides = [new CatalogueProvide { Kind = ProvideKind.Message, Name = "Executable doesn't exist at" }],
                        Artifacts = [new CatalogueArtifact { Name = "tool.zip", Url = "https://github.com/example/t.zip", Size = 12601679, Sha256 = CatalogueFixtures.Sha("t"), Sha256Hint = null }] },
                    CatalogueFixtures.PinnedPyset() with { Provides = [new CatalogueProvide { Kind = ProvideKind.PyModule, Name = "m" }] },
                    CatalogueFixtures.PinnedSkillset() with { TermsClass = TermsClass.Proprietary, CountState = CountState.Added, PinState = PinState.Unpinned },
                ],
            };
            var text = SkillsCatalogueWriter.Serialize(catalogue);
            Assert.Contains("\"kind\": \"tool\"", text, StringComparison.Ordinal);
            Assert.Contains("\"kind\": \"pyset\"", text, StringComparison.Ordinal);
            Assert.Contains("\"kind\": \"skillset\"", text, StringComparison.Ordinal);
            Assert.Contains("\"termsClass\": \"proprietary\"", text, StringComparison.Ordinal);
            Assert.Contains("\"countState\": \"added\"", text, StringComparison.Ordinal);
            Assert.Contains("\"pinState\": \"unpinned\"", text, StringComparison.Ordinal);
            Assert.Contains("\"kind\": \"pyModule\"", text, StringComparison.Ordinal);
            Assert.Contains("\"kind\": \"message\"", text, StringComparison.Ordinal);
            Assert.Contains("\"approxSizeMb\": 8.1,", text, StringComparison.Ordinal);
            Assert.Contains("\"size\": 12601679,", text, StringComparison.Ordinal);
            Assert.Contains("\"name\": \"Executable doesn't exist at\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\\u0027", text, StringComparison.Ordinal);
            Assert.Equal(text, SkillsCatalogueWriter.Serialize(catalogue));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Text_with_quotes_backslashes_line_breaks_and_non_ASCII_letters_survives_the_round_trip()
    {
        var tricky = "Quote \" backslash \\ line\nbreak tab\t umlaut ü emoji 😀 slash / < > &";
        var catalogue = ChangeReportFixtures.Mutate(CatalogueFixtures.Pinned(), "X1", i => i with { Summary = "One line.", Title = tricky, LicenceNotes = tricky });
        var back = SkillsCatalogueLoader.Parse(SkillsCatalogueWriter.Serialize(catalogue));
        Assert.Equal(tricky, CatalogueFixtures.Item(back, "X1").Title);
        Assert.Equal(tricky, CatalogueFixtures.Item(back, "X1").LicenceNotes);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_pin_state_is_derived_from_the_values_pinned_only_when_nothing_is_missing()
    {
        var pinned = SkillsCatalogueWriter.WithDerivedPinStates(CatalogueFixtures.Pinned());
        Assert.All(pinned.Items, i => Assert.Equal(PinState.Pinned, i.PinState));
        var wrong = CatalogueFixtures.Change(CatalogueFixtures.Pinned(), "X1", i => i with { PinState = PinState.Unpinned });
        Assert.Equal(PinState.Pinned, CatalogueFixtures.Item(SkillsCatalogueWriter.WithDerivedPinStates(wrong), "X1").PinState);
        var lacking = CatalogueFixtures.Change(CatalogueFixtures.Pinned(), "X1", i => ChangeReportFixtures.ChangeArtifact(i, "tool.zip", a => a with { Sha256 = null }));
        Assert.Equal(PinState.Unpinned, CatalogueFixtures.Item(SkillsCatalogueWriter.WithDerivedPinStates(lacking), "X1").PinState);
        Assert.Empty(CatalogueGate.Check(SkillsCatalogueWriter.WithDerivedPinStates(lacking)).Violations);
        Assert.Empty(CatalogueGate.Check(SkillsCatalogueWriter.WithDerivedPinStates(CatalogueFixtures.Real)).Violations);
        Assert.Empty(CatalogueChangeReport.Compare(CatalogueFixtures.Real, SkillsCatalogueWriter.WithDerivedPinStates(CatalogueFixtures.Real)).Items);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_null_catalogue_is_refused_and_a_catalogue_the_gate_rejects_is_still_written_so_it_can_be_read()
    {
        Assert.Throws<ArgumentNullException>(() => SkillsCatalogueWriter.Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => SkillsCatalogueWriter.WithDerivedPinStates(null!));
        var bad = CatalogueFixtures.Change(CatalogueFixtures.Pinned(), "X1", i => i with { Needs = ["T99"] });
        var back = SkillsCatalogueLoader.Parse(SkillsCatalogueWriter.Serialize(bad));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(back), CatalogueRules.NeedsExist, "X1"));
    }
}

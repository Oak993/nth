using System.Text.Json.Nodes;
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-1 (DL-1 of porting/first-release/skills-ONDEMAND-PLAN.md section 7, BUILD-PLAN.md 2.5): the catalogue schema and the embedded file.
// Expected values come from ONDEMAND-PLAN.md 2.3 (the per-item fields) and BUILD-PLAN.md 2.5 SK-1 (kind, termsClass, licence file path);
// the plan says an unknown value stays unknown, so a key is always present and an explicit null marks "not read yet".
[Trait("Spec", "DL-1")]
public sealed class SkillsCatalogueSchemaTests
{
    private static readonly string[] ItemKeys =
    [
        "id", "kind", "title", "summary", "vendor", "countState", "pinState", "pin", "sourceUrl", "licenceName", "licenceUrl",
        "licenceFile", "termsClass", "approxSizeMb", "needs", "provides", "artifacts", "files",
    ];

    private static JsonNode Edit(Action<JsonObject> change)
    {
        var root = JsonNode.Parse(CatalogueFixtures.RawJson())!;
        change(root["items"]!.AsArray()[0]!.AsObject());
        return root;
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_catalogue_is_an_embedded_resource_of_Marrow_Core_and_parses()
    {
        using var stream = typeof(SkillsCatalogue).Assembly.GetManifestResourceStream(SkillsCatalogueLoader.ResourceName);
        Assert.NotNull(stream);
        Assert.Equal("Marrow.Core.SkillsCatalogue.json", SkillsCatalogueLoader.ResourceName);
        Assert.Equal(SkillsCatalogueCounts.SchemaVersion, CatalogueFixtures.Real.Schema);
        Assert.Equal("2026-10-04", CatalogueFixtures.Real.ReadOn);
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_item_carries_every_key_ONDEMAND_2_3_names_even_when_the_value_is_not_known_yet()
    {
        var items = JsonNode.Parse(CatalogueFixtures.RawJson())!["items"]!.AsArray();
        Assert.NotEmpty(items);
        foreach (var item in items)
        {
            var obj = item!.AsObject();
            foreach (var key in ItemKeys) Assert.True(obj.ContainsKey(key), $"{obj["id"]}: key {key} is missing");
        }
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_five_kinds_are_the_ones_ONDEMAND_2_3_and_BUILD_PLAN_2_5_name_and_every_kind_is_used()
    {
        Assert.Equal(["nodeset", "plugin", "pyset", "skillset", "tool"],
            Enum.GetNames<ItemKind>().Select(n => n.ToLowerInvariant()).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal(["Nodeset", "Plugin", "Pyset", "Skillset", "Tool"],
            CatalogueFixtures.Real.Items.Select(i => i.Kind.ToString()).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal(["none", "open", "proprietary"], Enum.GetNames<TermsClass>().Select(n => n.ToLowerInvariant()).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_json_spells_kinds_and_terms_classes_as_the_plan_does()
    {
        var items = JsonNode.Parse(CatalogueFixtures.RawJson())!["items"]!.AsArray().Select(i => i!.AsObject()).ToList();
        Assert.All(items, i => Assert.Contains((string)i["kind"]!, new[] { "skillset", "plugin", "tool", "pyset", "nodeset" }));
        Assert.All(items, i => Assert.Contains((string)i["termsClass"]!, new[] { "open", "proprietary", "none" }));
        Assert.All(items, i => Assert.Contains((string)i["pinState"]!, new[] { "pinned", "unpinned" }));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void An_unknown_member_is_refused_so_a_typo_cannot_hide_a_value() =>
        Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse(Edit(o => o["licenseName"] = "MIT").ToJsonString()));

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_missing_required_member_is_refused_not_defaulted() =>
        Assert.All(ItemKeys, key =>
            Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse(Edit(o => o.Remove(key)).ToJsonString())));

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_null_where_the_schema_allows_none_is_refused()
    {
        foreach (var key in new[] { "id", "kind", "title", "summary", "vendor", "termsClass", "needs", "provides", "artifacts", "files" })
            Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse(Edit(o => o[key] = null).ToJsonString()));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void An_enum_must_be_one_of_its_own_words_never_a_number_or_a_stranger()
    {
        Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse(Edit(o => o["kind"] = 0).ToJsonString()));
        Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse(Edit(o => o["kind"] = "bundle").ToJsonString()));
        Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse(Edit(o => o["termsClass"] = "Open").ToJsonString()));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_duplicate_key_and_malformed_text_are_refused()
    {
        var raw = CatalogueFixtures.RawJson();
        Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse(raw.Replace("\"schema\": 1,", "\"schema\": 1, \"schema\": 1,", StringComparison.Ordinal)));
        Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse(""));
        Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse("[]"));
        Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse("null"));
        Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse("{"));
        Assert.Throws<CatalogueFormatException>(() => SkillsCatalogueLoader.Parse(raw[..(raw.Length / 2)]));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_loader_returns_the_same_values_each_time()
    {
        var first = SkillsCatalogueLoader.LoadEmbedded();
        var second = SkillsCatalogueLoader.LoadEmbedded();
        Assert.Equal(first.Items.Select(i => i.Id), second.Items.Select(i => i.Id));
        Assert.Equal(first.Items.Count, CatalogueFixtures.Real.Items.Count);
    }
}

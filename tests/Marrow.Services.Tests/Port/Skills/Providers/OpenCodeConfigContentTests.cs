using System.Text.Json;
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// PORT-ONLY: SK-11d / WU-9 "the variable itself".
public sealed class OpenCodeConfigContentTests
{
    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void Only_a_skills_key_is_added_and_the_base_text_stays_byte_for_byte()
    {
        const string baseJson = "{\"$schema\":\"https://opencode.ai/config.json\",\"mcp\":{\"a\":{\"type\":\"local\",\"command\":[\"x\"]}}}";
        var result = OpenCodeConfigContent.WithSkillPaths(baseJson, ["u1", "~\\x"], "C:\\Data dir\\views\\opencode\\chat-1");
        Assert.Equal(baseJson[..^1] + ",\"skills\":{\"paths\":[\"u1\",\"~\\\\x\",\"C:\\\\Data dir\\\\views\\\\opencode\\\\chat-1\"]}}", result);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void An_empty_base_gives_just_the_skills_key()
    {
        Assert.Equal("{\"skills\":{\"paths\":[\"m\"]}}", OpenCodeConfigContent.WithSkillPaths("{}", [], "m"));
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void An_inherited_base_is_preserved_key_for_key_and_its_other_skills_keys_survive()
    {
        var result = OpenCodeConfigContent.WithSkillPaths("{\"model\":\"m\",\"skills\":{\"urls\":[\"https://e/\"],\"paths\":[\"old\"]},\"n\":1}", ["old"], "m");
        using var d = JsonDocument.Parse(result);
        Assert.Equal("m", d.RootElement.GetProperty("model").GetString());
        Assert.Equal(1, d.RootElement.GetProperty("n").GetInt32());
        Assert.Equal("https://e/", d.RootElement.GetProperty("skills").GetProperty("urls")[0].GetString());
        Assert.Equal(["old", "m"], OpenCodeFixture.ReadPaths(result));
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void A_non_object_base_is_refused()
    {
        Assert.Throws<OpenCodeConfigUnreadableException>(() => OpenCodeConfigContent.WithSkillPaths("[1]", [], "m"));
    }
}

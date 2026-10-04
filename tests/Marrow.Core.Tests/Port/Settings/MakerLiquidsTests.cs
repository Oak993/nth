using System.Security.Cryptography;
using System.Text.Json;
using Marrow.Core.Port.Settings;
namespace Marrow.Core.Tests.Port.Settings;

public sealed class MakerLiquidsTests
{
    [Fact]
    [Trait("Feature", "CZ-1")]
    [Trait("Spec", "CZ-T29")]
    [Trait("Decision", "ED-251")]
    public void Thirty_makers_have_the_pinned_source_order_names_models_and_both_high_palettes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!.FullName, "tests/fixtures/customization/expected.json")));
        var bytes = File.ReadAllBytes(Path.Combine(dir.FullName, "porting/first-release/design-src/orb/liquid-palettes.json"));
        Assert.Equal("a600baefbcf22aca861392456ff4d9c05088c37627fe30e95cd5142d5c441f15", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(MakerLiquids.SourceSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(30, MakerLiquids.All.Count);
        Assert.Equal(new[] { "Claude", "OpenAI", "Gemini", "DeepSeek", "Grok", "NVIDIA", "Meta", "Mistral", "Qwen", "Kimi", "Cursor", "OpenCode", "OpenCodeGo", "Zai", "Devin", "Copilot", "CommandCode", "Pi", "MiniMax", "Tencent", "Cohere", "Microsoft", "Perplexity", "NousResearch", "AWS", "AI21", "Inflection", "LiquidAI", "Baidu", "Arcee" }, MakerLiquids.All.Select(m => m["id"]));
        var expected = fixture.RootElement.GetProperty("makers").EnumerateArray().ToArray();
        for (var i = 0; i < 30; i++)
            foreach (var property in expected[i].EnumerateObject()) Assert.Equal(property.Value.GetString(), MakerLiquids.All[i][property.Name]);
        Assert.Equal("OpenCode Go", MakerLiquids.For("OpenCodeGo")["name"]);
        Assert.Equal("Command Code", MakerLiquids.For("CommandCode")["name"]);
        Assert.Equal("Nous Research", MakerLiquids.For("NousResearch")["name"]);
        Assert.Equal("Liquid AI", MakerLiquids.For("LiquidAI")["name"]);
        Assert.Equal("Z.ai", MakerLiquids.For("Zai")["name"]);
        Assert.DoesNotContain(MakerLiquids.All, m => m["model"].StartsWith('('));
    }
}

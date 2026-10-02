using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

// SettingsView.swift:719-727 "Get a <name> key"; OD-0072: the article follows the first sound of the name.
public sealed class ProviderKeyLinkTests
{
    [Theory]
    [Trait("Req", "UI-PROVREG-040")]
    [InlineData(ProviderKind.Codex, "Get a Codex key")]
    [InlineData(ProviderKind.Claude, "Get a Claude key")]
    [InlineData(ProviderKind.Cursor, "Get a Cursor key")]
    [InlineData(ProviderKind.Opencode, "Get an OpenCode Zen key")]
    [InlineData(ProviderKind.OpencodeGo, "Get an OpenCode Go key")]
    [InlineData(ProviderKind.Grok, "Get a Grok key")]
    [InlineData(ProviderKind.Deepseek, "Get a DeepSeek key")]
    [InlineData(ProviderKind.Meta, "Get a Meta key")]
    [InlineData(ProviderKind.Zai, "Get a Z.ai key")]
    [InlineData(ProviderKind.Devin, "Get a Devin key")]
    [InlineData(ProviderKind.Antigravity, "Get an Antigravity key")]
    [InlineData(ProviderKind.Copilot, "Get a Copilot key")]
    [InlineData(ProviderKind.Commandcode, "Get a Command Code key")]
    [InlineData(ProviderKind.Pi, "Get a Pi key")]
    [InlineData(ProviderKind.OpenRouter, "Get an OpenRouter key")]
    public void Every_provider_kind_gets_the_article_its_name_sounds_like(ProviderKind kind, string expected) =>
        Assert.Equal(expected, kind.GetKeyLinkText);

    [Theory]
    [Trait("Req", "UI-PROVREG-040")]
    [InlineData("Apple", "an")]
    [InlineData("Echo", "an")]
    [InlineData("Ollama", "an")]
    [InlineData("Umbrella", "an")]
    [InlineData("Unicorn", "a")]
    [InlineData("User", "a")]
    [InlineData("Utility", "a")]
    [InlineData("Banana", "a")]
    [InlineData("xAI", "an")]
    [InlineData("XAI", "an")]
    [InlineData("MSN", "an")]
    [InlineData("HP", "an")]
    [InlineData("FBI", "an")]
    [InlineData("SDK", "an")]
    [InlineData("NASA", "a")]
    [InlineData("GPU", "a")]
    [InlineData("Hour", "an")]
    [InlineData("EU", "an")]
    [InlineData("Europe", "a")]
    public void The_article_follows_the_first_sound(string name, string article) =>
        Assert.Equal(article, ProviderKeyLink.Article(name));
}

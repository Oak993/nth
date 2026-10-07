using System.Text.Json;
using Marrow.Core.Port.Local;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.Core.Tests.Port.Local;

// T15-review-rulings.md #5 / MAUI-054: identity is a reply, never a configured address or model name.
[Trait("Feature", "MA-3"), Trait("Rule", "R-TEST-10")]
public sealed class LocalServerIdentityTests
{
    [Theory]
    [InlineData("Ollama is running", LocalServerKind.Ollama)]
    [InlineData("  Ollama is running\n", LocalServerKind.Ollama)]
    [InlineData("Ollama is not running", LocalServerKind.Unknown)]
    [InlineData("LM Studio", LocalServerKind.Unknown)]
    [InlineData("", LocalServerKind.Unknown)]
    [InlineData("{\"version\":\"0.12.0\"}", LocalServerKind.Unknown)]
    public void Root_requires_the_actual_Ollama_identity_reply(string reply, LocalServerKind expected) =>
        Assert.Equal(expected, LocalServerIdentity.FromRootReply(reply));

    [Theory]
    [InlineData("{\"object\":\"list\",\"data\":[{\"id\":\"sample\",\"object\":\"model\",\"type\":\"llm\",\"publisher\":\"any-publisher\",\"arch\":\"llama\",\"compatibility_type\":\"gguf\",\"quantization\":\"Q4_K_M\",\"state\":\"not-loaded\",\"max_context_length\":131072}]}", LocalServerKind.LMStudio)]
    [InlineData("{\"object\":\"list\",\"data\":[{\"id\":\"sample\",\"object\":\"model\",\"type\":\"vlm\",\"publisher\":\"mlx-community\",\"arch\":\"qwen2_vl\",\"compatibility_type\":\"mlx\",\"quantization\":\"4bit\",\"state\":\"loaded\",\"max_context_length\":32768}]}", LocalServerKind.LMStudio)]
    [InlineData("{\"object\":\"list\",\"data\":[{\"id\":\"Ollama\",\"owned_by\":\"lmstudio-community\"}]}", LocalServerKind.Unknown)]
    [InlineData("{\"data\":[{\"id\":\"test\",\"loaded_context_length\":8192}]}", LocalServerKind.Unknown)]
    [InlineData("{\"data\":[]}", LocalServerKind.Unknown)]
    [InlineData("[]", LocalServerKind.Unknown)]
    [InlineData("{\"object\":\"list\",\"data\":[null,4,\"LM Studio\"]}", LocalServerKind.Unknown)]
    public void Studio_endpoint_requires_its_documented_reply_shape(string reply, LocalServerKind expected)
    {
        using var json = JsonDocument.Parse(reply);
        Assert.Equal(expected, LocalServerIdentity.FromStudioModelsReply(json.RootElement));
    }

    [Theory]
    [InlineData(LocalServerKind.Unknown, "Found a server on this PC \u00b7 1 model added")]
    [InlineData(LocalServerKind.Ollama, "Found Ollama on this PC \u00b7 1 model added")]
    [InlineData(LocalServerKind.LMStudio, "Found LM Studio on this PC \u00b7 1 model added")]
    public async Task Settings_uses_the_discovered_kind_and_keeps_the_generic_fallback(LocalServerKind kind, string expected)
    {
        var rig = new AccountsRig();
        rig.Actions.Local = new(LocalConnectOutcome.Found, 1, "this PC", kind);
        Assert.Equal(expected, await rig.Model.ConnectLocalAsync("http://localhost:11434"));
        Assert.Equal("Found a server on another-pc \u00b7 2 models added", MultiAccountWords.LocalFoundOn("another-pc", 2));
    }
}

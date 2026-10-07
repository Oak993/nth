using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Local;
using Marrow.Services.Port.Local;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Settings.MultiAccount;

// MAUI-054: exercise the production discovery -> account action -> result bridge, with in-process replies only.
[Trait("Feature", "MA-8"), Trait("Rule", "R-TEST-10")]
public sealed class LocalConnectIdentityBridgeTests
{
    [Theory]
    [InlineData("Ollama is running", LocalServerKind.Ollama)]
    [InlineData("studio-native", LocalServerKind.LMStudio)]
    [InlineData("generic server", LocalServerKind.Unknown)]
    [InlineData("{\"version\":\"1.0\"}", LocalServerKind.Unknown)]
    public void Account_actions_copy_the_detected_kind_without_reclassifying_the_address(string rootReply, LocalServerKind expected)
    {
        using var http = new InProcessHttpHandler
        {
            Answer = r => r.Url.AbsolutePath switch
            {
                "/v1/models" => InProcessHttpHandler.Reply(200, """{"data":[{"id":"test"}]}"""),
                "/api/v0/models" when rootReply == "studio-native" => InProcessHttpHandler.Reply(200, """{"object":"list","data":[{"id":"test","object":"model","type":"llm","publisher":"any","arch":"llama","compatibility_type":"gguf","quantization":"Q4_K_M","state":"loaded","max_context_length":8192}]}"""),
                "/" => InProcessHttpHandler.Reply(200, rootReply, "text/plain"),
                _ => InProcessHttpHandler.Reply(404, "{}"),
            },
        };
        using var discovery = new LocalDiscovery([], httpHandler: http);
        using var harness = new AppModelHarness();
        var app = harness.MakeModel();
        harness.PrepareBootstrap(app);
        harness.Main.OnMain(() => app.Providers.InitializeLocal(discovery));
        var result = harness.Main.Loop.Run(() => new AppAccountActions(app).ConnectLocalAsync("https://offline-identity.invalid:11434"), AppModelHarness.Bound);
        Assert.Equal(expected, result.Kind);
        Assert.Equal(1, result.Models);
        Assert.Equal("offline-identity.invalid", result.Where);
        Assert.Contains(app.Settings.Accounts.Accounts, a => a.Defaults.Model?.StartsWith("local/", StringComparison.Ordinal) == true);
    }
}

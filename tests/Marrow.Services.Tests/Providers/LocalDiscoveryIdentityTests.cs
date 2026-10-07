using Marrow.Core.Port.Local;
using Marrow.Services.Port.Local;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

[Trait("Feature", "MA-3"), Trait("Rule", "R-TEST-10")]
public sealed class LocalDiscoveryIdentityTests
{
    private const string List = """{"data":[{"id":"test","context_length":8192,"supports_tool_calling":true}]}""";
    // Published native API reply: https://lmstudio.ai/docs/developer/rest/endpoints# get-api-v0-models.
    private const string Studio = """{"object":"list","data":[{"id":"test","object":"model","type":"llm","publisher":"any-publisher","arch":"llama","compatibility_type":"gguf","quantization":"Q4_K_M","state":"loaded","max_context_length":131072}]}""";

    [Theory]
    [InlineData(11434, "generic", LocalServerKind.Unknown)]
    [InlineData(1234, "generic", LocalServerKind.Unknown)]
    [InlineData(8080, "generic", LocalServerKind.Unknown)]
    [InlineData(9100, "ollama", LocalServerKind.Ollama)]
    [InlineData(1234, "ollama", LocalServerKind.Ollama)]
    [InlineData(11434, "studio", LocalServerKind.LMStudio)]
    [InlineData(9100, "studio", LocalServerKind.LMStudio)]
    [InlineData(11434, "version-only", LocalServerKind.Unknown)]
    [InlineData(1234, "malformed", LocalServerKind.Unknown)]
    [InlineData(11434, "error", LocalServerKind.Unknown)]
    [InlineData(1234, "model-name", LocalServerKind.Unknown)]
    [InlineData(11434, "tags-only", LocalServerKind.Unknown)]
    [InlineData(11434, "redirect", LocalServerKind.Unknown)]
    [InlineData(11434, "identity-timeout", LocalServerKind.Unknown)]
    public async Task Server_reply_alone_determines_identity_on_any_port(int port, string shape, LocalServerKind expected)
    {
        using var http = Handler(shape);
        using var discovery = new LocalDiscovery([new Uri("https://offline-identity.invalid:" + port)], httpHandler: http);
        var entry = Assert.Single(await discovery.Discover());
        Assert.Equal(expected, entry.Kind);
        Assert.Single(entry.Models);
        Assert.All(http.Received, r => Assert.Null(r.Header("Authorization")));
        Assert.All(http.Received, r => Assert.True(r.Method == "GET" || (r.Method == "POST" && r.Url.AbsolutePath == "/api/show")));
        Assert.DoesNotContain(http.Received, r => r.Url.AbsolutePath.Contains("load", StringComparison.Ordinal) || r.Url.AbsolutePath.Contains("generate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rediscovery_never_keeps_an_identity_the_server_no_longer_reports()
    {
        using var http = Handler("ollama");
        using var discovery = new LocalDiscovery([new Uri("https://offline-identity.invalid:11434")], httpHandler: http);
        Assert.Equal(LocalServerKind.Ollama, Assert.Single(await discovery.Discover()).Kind);
        http.Answer = r => InProcessHttpHandler.Reply(r.Url.AbsolutePath == "/v1/models" ? 200 : 404, r.Url.AbsolutePath == "/v1/models" ? List : "{}");
        Assert.Equal(LocalServerKind.Unknown, Assert.Single(await discovery.Discover()).Kind);
    }

    private static InProcessHttpHandler Handler(string shape) => new()
    {
        Answer = r => r.Url.AbsolutePath switch
        {
            "/" when shape == "identity-timeout" => throw new OperationCanceledException(),
            "/api/tags" when shape == "tags-only" => InProcessHttpHandler.Reply(200, """{"models":[{"name":"test"}]}"""),
            "/v1/models" => InProcessHttpHandler.Reply(200, shape == "model-name" ? """{"data":[{"id":"Ollama","owned_by":"lmstudio-community"}]}""" : List),
            "/" when shape == "ollama" => InProcessHttpHandler.Reply(200, "Ollama is running", "text/plain"),
            "/" when shape == "redirect" => InProcessHttpHandler.Reply(302, "Ollama is running", "text/plain", ("Location", "https://other-identity.invalid/")),
            "/" when shape == "error" => InProcessHttpHandler.Reply(500, "Ollama is running", "text/plain"),
            "/" when shape == "version-only" => InProcessHttpHandler.Reply(200, """{"version":"0.12.0"}"""),
            "/api/v0/models" when shape == "studio" => InProcessHttpHandler.Reply(200, Studio),
            "/api/v0/models" when shape == "malformed" => InProcessHttpHandler.Reply(200, "LM Studio {"),
            _ => InProcessHttpHandler.Reply(404, "{}"),
        },
    };
}

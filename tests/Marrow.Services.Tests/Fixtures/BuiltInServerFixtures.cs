using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// Dextra AS-4 (ED-NEW-dextra-as4-7): the per-provider fixtures for nth's built-in MCP servers, in the shape Tactus's
/// provider tests use (pc-control 3.4), so nth-dextra and nth-pc check every provider against the same inputs: a
/// session's configuration, a connected server in each provider's form, and the advisor's entry.
/// </summary>
public static class BuiltInServerFixtures
{
    public static readonly MCPResolvedServer Advisor = new("nth-advisor", "nth-advisor")
    {
        Command = "C:/nth/nth.exe", Args = new ValueList<string>(["--marrow-advisor-mcp", "pipe", "token"]),
    };

    /// <summary>A connected server as Codex's overrides carry it.</summary>
    public static JSONValue CodexConnected() =>
        JSONValue.FromDictionaryLiteral(("github", JSONValue.FromDictionaryLiteral(("url", "https://example.test/mcp"))));

    /// <summary>A connected server as ACP's session/new carries it.</summary>
    public static JSONValue AcpConnected() => JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("name", "github")));

    /// <summary>A connected server for the Settings files (claude.json, the Gemini settings).</summary>
    public static MCPResolvedServer Connected { get; } = new("github", "github") { Url = "https://example.test/mcp" };

    /// <summary>Every provider whose session can carry nth's built-in servers.</summary>
    public static IReadOnlyList<ProviderKind> Providers { get; } =
    [
        ProviderKind.Claude, ProviderKind.Codex, ProviderKind.Copilot, ProviderKind.Cursor, ProviderKind.Antigravity,
        ProviderKind.Pi, ProviderKind.Commandcode, ProviderKind.Deepseek, ProviderKind.Local,
    ];

    public static SessionConfiguration Config(ProviderKind provider, Guid? thread = null) => new()
    {
        Provider = provider,
        WorkingDirectory = SwiftURL.Create("marrow-native-path:C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
        Model = "fixture-model",
        IgnisThreadID = thread,
    };
}

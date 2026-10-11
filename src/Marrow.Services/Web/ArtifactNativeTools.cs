// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.2 "as native tools (API providers)"; ED-NEW-web-w12-1). The artifact
// tools in the API providers' own tool loop, beside use_skill and ask_advisor (NativeSkillTools offers and runs them).
// Offered only to a chat's session while the artifact host is on. No Swift counterpart.
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;

namespace Marrow.Services.Web;

public sealed class ArtifactNativeTools(UiRegistration registration)
{
    /// <summary>The tools for a session, or null when it serves no chat or artifacts are off.</summary>
    public static ArtifactNativeTools? For(SessionConfiguration configuration) =>
        UiMcpBridge.For(configuration) is { } registration ? new ArtifactNativeTools(registration) : null;

    public UiRegistration Registration { get; } = registration;

    public IReadOnlyList<JSONValue> Definitions => [.. ArtifactTools.Names.Select(ArtifactTools.FunctionDefinition)];

    public bool Owns(string toolName) => ArtifactTools.IsName(toolName);

    /// <summary>The timeline row's title for a call, known before it runs.</summary>
    public static string Title(string toolName, JSONValue arguments) => ArtifactTools.RowTitle(toolName, Node(arguments));

    /// <summary>Runs one artifact tool; a refusal is a failed result, never a throw.</summary>
    public async Task<NativeToolResult> Execute(string toolName, JSONValue arguments)
    {
        var (thread, provider, model, host) = Registration;
        var (author, prompt) = await host.Caller(thread, provider, model).ConfigureAwait(true);
        var result = ArtifactTools.Run(host.Store!, thread, toolName, Node(arguments), author, prompt);
        return new NativeToolResult(Title(toolName, arguments), result.Text, result.Failed, []);
    }

    private static JsonNode? Node(JSONValue arguments)
    {
        try { return JsonNode.Parse(arguments.CompactString); }
        catch (JsonException) { return null; }
    }
}

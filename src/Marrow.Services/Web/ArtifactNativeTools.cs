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

    /// <summary>The tools offered, in order: the four artifact tools, then the read-only live-view reference (W17). The live-answer tools of W24 join them.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. ArtifactTools.Names, UiReferenceTool.Name];

    public IReadOnlyList<JSONValue> Definitions => [.. ArtifactTools.Names.Select(ArtifactTools.FunctionDefinition), UiReferenceTool.FunctionDefinition()];

    public bool Owns(string toolName) => Names.Contains(toolName);

    /// <summary>
    /// The teaching block for the system prompt (W17, ED-NEW-web-w17-1): when to use text, a live view or an artifact, then the view
    /// language and worked examples, matching the tools offered. A small local model gets the lite subset
    /// (<paramref name="profile"/>), and fenced blocks in place of tools when it cannot call them.
    /// </summary>
    public string TeachingBlock(Marrow.Core.NthUi.NthUiProfile profile = Marrow.Core.NthUi.NthUiProfile.Full, bool toolsUsable = true) =>
        UiTeaching.SystemBlock(Names, profile, toolsUsable);

    /// <summary>The timeline row's title for a call, known before it runs.</summary>
    public static string Title(string toolName, JSONValue arguments) =>
        toolName == UiReferenceTool.Name ? UiReferenceTool.RowTitle(Node(arguments)) : ArtifactTools.RowTitle(toolName, Node(arguments));

    /// <summary>Runs one artifact tool; a refusal is a failed result, never a throw.</summary>
    public async Task<NativeToolResult> Execute(string toolName, JSONValue arguments)
    {
        if (toolName == UiReferenceTool.Name)
        {
            var reference = UiReferenceTool.Run(Node(arguments));
            return new NativeToolResult(Title(toolName, arguments), reference.Text, reference.Failed, []);
        }
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

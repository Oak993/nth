// PORT-ONLY: web surfaces W11, the web tools for API providers (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "as native
// tools for API providers (WebNativeTools, like NativeSkillTools)"; ED-NEW-web-w11-1). No Swift counterpart. The same engine
// and gates as the nth-pc server; the definitions are the OpenAI-style functions the API sessions send, offered while Visus
// model access is on, with the model instructions in the system prompt. A tool message is text; an OpenAI-compatible session
// sends a screenshot as a picture in the round's screenshot message when its model takes images (as Tactus's native tools do),
// and a model that does not is told to read the page instead.
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Web;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Web;

public sealed class WebNativeTools(Func<WebAgentHub> hub, string thread, string? model)
{
    private PcRegistration? registration;
    /// <summary>The chat's web tools, or null when Visus is not bound or the session serves no chat.</summary>
    public static WebNativeTools? For(SessionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var shared = WebAgentHub.Shared.Current;
        if (shared.Host is null || configuration.IgnisThreadID is not { } thread) return null;
        return new WebNativeTools(() => WebAgentHub.Shared.Current, thread.ToString("D"), configuration.Model)
        {
            // ED-NEW-mission-pc-1: retain this launch's seal and host even if Tactus is off (Visus is independent).
            registration = configuration.Sealed is not null ? new PcRegistration(thread.ToString("D"), configuration.Provider, configuration.Model, configuration.Provider.SupportsImages)
            {
                Head = configuration.IsHydraHead ? "a Hydra head" : "a mission chat",
                MissionId = configuration.Sealed.MissionId,
                MissionHost = Marrow.Services.Assistant.AssistantMcpBridge.Host.Current,
            } : null,
        };
    }

    public string Thread { get; } = thread;

    private bool On => hub().Offered;

    /// <summary>The function definitions to send now: all the web tools while Visus model access is on, none otherwise.</summary>
    public IReadOnlyList<JSONValue> Definitions => On ? [.. WebToolSchemas.All.Select(spec => JSONValue.Parse(spec.FunctionDefinition().ToJsonString())!)] : [];

    /// <summary>The instructions for the system prompt while the tools are offered; null otherwise.</summary>
    public string? Instructions => On ? WebAgentText.Instructions : null;

    public bool Owns(string toolName) => On && WebToolSchemas.IsWebTool(toolName);

    /// <summary>The timeline row's title ("Visus · Read the page").</summary>
    public static string Title(string toolName) => "Visus \u00B7 " + (WebToolSchemas.Spec(toolName)?.Title ?? toolName);

    /// <summary>Runs one web tool; a refusal is a failed result, never a throw. A stop or the turn's end cancels it.</summary>
    public async Task<NativeToolResult> Execute(string toolName, JSONValue arguments, CancellationToken? cancellationToken = null)
    {
        var result = await Run(toolName, arguments, vision: false, cancellationToken ?? SwiftTask.CancellationToken).ConfigureAwait(true);
        return new NativeToolResult(Title(toolName), result.Render(), result.Failed, []);
    }

    /// <summary>
    /// Runs one web tool for a session that can send the screenshot as a picture after the round (<paramref name="vision"/>:
    /// the model takes images), as Tactus's native tools do (ED-NEW-pcctl-42).
    /// </summary>
    public async Task<WebToolResult> Run(string toolName, JSONValue arguments, bool vision, CancellationToken cancellationToken)
    {
        if (hub().Engine(Thread) is not { } engine) return WebToolResult.Fail(WebErrorCodes.Off, "Visus is not available here.");
        JsonObject? args;
        try
        {
            args = JsonNode.Parse(arguments.IsNull ? "{}" : arguments.CompactString) as JsonObject;
        }
        catch (JsonException)
        {
            args = null;
        }
        if (args is null) return WebToolResult.Fail(WebErrorCodes.BadArgs, "the arguments must be a JSON object.");
        var active = registration is null ? null : PcControlBridge.ForCall(registration);
        if (active is not null && !await PcControlBridge.ApproveMission(active, toolName, args, cancellationToken).ConfigureAwait(true))
            return WebToolResult.Fail(WebErrorCodes.NotApproved, "The user did not allow this step.");
        cancellationToken.ThrowIfCancellationRequested();
        var result = await engine.Call(toolName, args, new WebAgentCaller(Thread, model, vision), cancellationToken).ConfigureAwait(true);
        if (active is not null && !PcControlBridge.CompletedMission(active, toolName, args, result.Error == WebErrorCodes.WaitingForUser ? "waiting_for_user" : result.Failed ? "failed" : "done"))
            return WebToolResult.Fail(WebErrorCodes.NotApproved, "The activity log could not record the result; do not repeat the step.");
        return result;
    }
}

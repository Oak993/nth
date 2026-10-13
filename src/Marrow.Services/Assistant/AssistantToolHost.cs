// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.3, 12.4, 15.4, 15.5; unit AS-4; ED-NEW-dextra-as4-3). No Swift twin.
// The seam between the nth-dextra tools and the controller that acts on them (AS-5 and later units). The tools decide
// nothing about missions, duties or memory themselves: they check the arguments, ask the policy engine, write the
// audit line, and hand the call to the host (Run on Allow, Ask on Ask; nothing on Deny). Until a host is bound, no chat
// is Dextra's, so no provider is given the tools and every launch is exactly as before.
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using System.Text.Json.Nodes;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Assistant;

/// <summary>How a call reached nth: the MCP relay (CLI providers), a native tool (API providers) or a fenced block.</summary>
public enum AssistantRoute { Mcp, Native, Fenced }

/// <summary>What the host says a chat is: the assistant it belongs to, its mission, and its role.</summary>
public sealed record AssistantChatIdentity(string AssistantId, Guid? MissionId, AssistantChatRole Role);

/// <summary>The chat a registration serves, as it was when its session launched.</summary>
public sealed record AssistantChat(Guid Thread, string AssistantId, Guid? MissionId, AssistantChatRole Role, ProviderKind Provider, string? Model);

/// <summary>One call the policy engine has decided (12.3: the engine is asked before every tool acts).</summary>
public sealed record AssistantToolCall(
    AssistantChat Chat, AssistantToolDefinition Tool, JSONValue Arguments, Classification Classification, PolicyDecision Decision, AssistantRoute Route)
{
    /// <summary>The call's own id, the same in its audit lines and on its card.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>A delegation from a tainted context: the brief and material go as data only (12.4).</summary>
    public bool DataOnly => Decision.DataOnly;

    /// <summary>A delegation from a tainted context: the child mission starts tainted (12.4).</summary>
    public bool ChildStartsTainted => Decision.ChildStartsTainted;

    /// <summary>A memory note written from a tainted context: kept, quarantined, never used until the owner keeps it (8.2).</summary>
    public bool Quarantine => Decision.Quarantine;
}

/// <summary>What a call returns to the model.</summary>
public sealed record AssistantToolResult(bool IsError, string Text)
{
    /// <summary>
    /// The taint of what the result carries into the chat (12.4): a summary of a chat that read the web is tainted. The
    /// host sets it; AS-7's wiring absorbs it into the chat's context.
    /// </summary>
    public Taint Taint { get; init; } = Taint.Clean;

    /// <summary>The decision card's id, when the call went to the owner.</summary>
    public string? DecisionId { get; init; }

    public static AssistantToolResult Done(string text) => new(false, text);

    public static AssistantToolResult Failed(string text) => new(true, text);
}

/// <summary>The controller behind the nth-dextra tools.</summary>
public interface IAssistantToolHost
{
    /// <summary>ED-NEW-mission-pc-1: a sealed Tactus/Visus call waits for Dextra's own approval; an unbound host refuses.</summary>
    Task<bool> ApprovePc(PcRegistration registration, string tool, JsonObject arguments, CancellationToken cancellationToken) => Task.FromResult(false);

    /// <summary>The returned outcome of that call (no page content or typed text). A failed audit withholds the result.</summary>
    bool PcCompleted(PcRegistration registration, string tool, JsonObject arguments, string outcome) => false;

    /// <summary>The chat's assistant, mission and role; null for a chat that is not Dextra's (it gets no tools).</summary>
    AssistantChatIdentity? Identify(Guid thread);

    /// <summary>
    /// Whether the engine's Allow may run a tool alone. Off until AS-7's injection corpus passes (16, the ordering rule);
    /// while off, only the tools marked <see cref="AssistantToolDefinition.RunsBeforeAutoAnswer"/> run on an Allow.
    /// </summary>
    bool AutoAnswering { get; }

    /// <summary>What the policy engine reads about the chat now: its chain, grants, mission, taint, duty, purchases.</summary>
    PolicyContext PolicyContext(AssistantChat chat);

    /// <summary>
    /// The chat belongs to a dry run of a duty (7.3, AS-10) and the call is not a read: the answer to give the model instead of acting,
    /// and the call is listed as a step. Null: act as usual (not a dry run, or a read, which is real).
    /// </summary>
    string? DryAnswer(AssistantToolCall call) => null;

    /// <summary>Acts on an allowed call.</summary>
    Task<AssistantToolResult> Run(AssistantToolCall call, CancellationToken cancellationToken);

    /// <summary>Puts a call the policy asks about in front of the owner as a decision card; returns the card's id.</summary>
    Task<string> Ask(AssistantToolCall call, CancellationToken cancellationToken);

    /// <summary>
    /// A call refused with "needs an untainted request" (12.4): offers the owner a card he can raise himself. Nothing
    /// runs from it unless he does.
    /// </summary>
    void Offer(AssistantToolCall call);

    /// <summary>Writes one audit line (12.5), through the single writer. Throws when it cannot, and then nothing runs.</summary>
    void Audit(AuditEntry entry);

    /// <summary>
    /// A result is about to reach the chat with untrusted material in it (12.4 "Taint follows text", AS-7): the chat's
    /// context takes its taint first. Called only with a tainted <paramref name="taint"/>; a host that throws makes the
    /// result withheld (fail closed).
    /// </summary>
    void Absorb(AssistantChat chat, Taint taint);

    /// <summary>
    /// Before a call of the chat is decided (AS-7, ED-NEW-dextra-as7-18): the host takes in the chat's events that came
    /// before it (a page the chat read taints it first). A host that throws or times out makes the call refused.
    /// </summary>
    Task Settle(AssistantChat chat, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// The notes this chat is told at its start (AS-12, 8.2 "used in a prompt"): used notes only, one line each; null or empty for a chat
    /// that is not told any (a mission agent, a host with no memory). Called when the provider session initialises.
    /// </summary>
    string? MemoryBlock(AssistantChat chat) => null;
}

/// <summary>The host while none is bound: no chat is Dextra's.</summary>
public sealed class NoAssistantToolHost : IAssistantToolHost
{
    public static NoAssistantToolHost Instance { get; } = new();

    public AssistantChatIdentity? Identify(Guid thread) => null;

    public bool AutoAnswering => false;

    public PolicyContext PolicyContext(AssistantChat chat) => throw new InvalidOperationException("No Dextra controller is running.");

    public Task<AssistantToolResult> Run(AssistantToolCall call, CancellationToken cancellationToken) => throw new InvalidOperationException("No Dextra controller is running.");

    public Task<string> Ask(AssistantToolCall call, CancellationToken cancellationToken) => throw new InvalidOperationException("No Dextra controller is running.");

    public void Offer(AssistantToolCall call) { }

    public void Audit(AuditEntry entry) => throw new InvalidOperationException("No Dextra controller is running.");

    public void Absorb(AssistantChat chat, Taint taint) => throw new InvalidOperationException("No Dextra controller is running.");
}

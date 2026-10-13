using System.Diagnostics;
using System.Linq;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Providers/ProviderSession.swift (B3 52bc792), :1-230. B3 adds ToolText.blocks
// (:211-219) and ApprovalRequest.Kind.grantKey (:221-230), both relocated here from the Claude, Pi and Command
// Code sessions (lane A7); their own switchover to the shared helpers happens in those files' own lanes.

namespace Marrow.Services.Providers;

// :4-19. What a thread asks a provider to do for one turn (R-VAL-1: immutable record).
public sealed record TurnInput
{
    public required string Text { get; init; }                     // :5
    public required ValueList<Attachment> Images { get; init; }    // :6
    public string? Model { get; init; }                            // :7
    public string? Effort { get; init; }                           // :8
    /// <summary>A Codex service tier for this turn: the fast tier, "default", or nil when the model has none.</summary>
    public string? ServiceTier { get; init; }                      // :9-10
    /// <summary>Cursor ACP's Fast variant (`model_config` / `fast`); other providers ignore it.</summary>
    public bool FastMode { get; init; }                            // :11-12
    public required RuntimeMode RuntimeMode { get; init; }         // :13
    public required InteractionMode InteractionMode { get; init; } // :14
    /// <summary>
    /// A Hydra head past its budget answering for the last time: the turn is one reply with no tools, where the
    /// session can arrange that.
    /// </summary>
    public bool IsFinalReport { get; init; }                       // :15-17
    public SlashCommand? Command { get; init; }                    // :18
}

// :22-47. Everything a provider session needs to launch.
public sealed record SessionConfiguration
{
    // PORT-ONLY: OD-0114 / ED-404; null only for legacy call sites/test fixtures.
    public Guid? AccountId { get; init; }
    public HandshakePoint? AccountHandshakePoint { get; init; }
    public required ProviderKind Provider { get; init; }                 // :23
    public SwiftURL? Executable { get; init; }                           // :24
    public required SwiftURL WorkingDirectory { get; init; }             // :25
    public required ValueMap<string, string> Environment { get; init; }  // :26
    public string? ResumeID { get; init; }                               // :27
    public string? ResumeAt { get; init; }                               // :28
    // DEVIATION OD-0114 / ED-407: supported Codex path resume, only set by a version-gated handoff.
    public string? ResumePath { get; init; }
    public string? Model { get; init; }                                  // :29
    public string? Effort { get; init; }                                 // :30
    public bool FastMode { get; init; }                                  // :31
    public required RuntimeMode RuntimeMode { get; init; }               // :32
    public required InteractionMode InteractionMode { get; init; }       // :33
    /// <summary>Native API providers (DeepSeek, Meta, Z.ai) authenticate with this instead of a CLI.</summary>
    public string? ApiKey { get; init; }                                 // :34-35
    /// <summary>
    /// Set while Hydra is on for the thread: providers that run heads of their own define them at launch, on the
    /// pair's model and effort, and steer the lead towards them.
    /// </summary>
    public HydraLaunch? Hydra { get; init; }                            // :36-38
    /// <summary>
    /// The conversation so far, for a provider that keeps nothing between launches (the API providers hold their
    /// history in memory only): the exchanges go back in as context, so a session that starts over still knows
    /// what was said.
    /// </summary>
    public ValueList<TranscriptMessage> Transcript { get; init; } = ValueList<TranscriptMessage>.Empty; // :39-42
    /// <summary>
    /// The thread is a Hydra head that Droppy Code runs: the session paces it, so a head that keeps reading
    /// without ever changing anything is told to act.
    /// </summary>
    public bool IsHydraHead { get; init; }                              // :43-45
    public bool AntiSlopEnabled { get; init; } = true;                  // :46
    // PORT-ONLY: SK-17 / OD-0099 Q-SKILLS-8 A. The launch owner retains and disposes this immutable lease.
    public Marrow.Services.Port.Skills.Generations.Generation? SkillGeneration { get; init; }
    // PORT-ONLY SK-17: the app's private stdio relay entry point, never an executable supplied by downloaded skills.
    public string? SkillMcpExecutable { get; init; }
    // PORT-ONLY RR-15: explicit host paths and per-chat request_tool flow; never inferred from downloaded files.
    public string? SkillDataRoot { get; init; }
    public string? SkillHome { get; init; }
    public Marrow.Services.Port.Skills.Providers.IToolRequestOffer? SkillToolOffer { get; init; }
    // PORT-ONLY (OD-0134 Ignis, ED-NEW-ignis-runtime): the chat this session serves, so Ignis can answer per chat (cache
    // lifetimes at a Claude launch, request-level cache levers). Null: legacy call sites and test fixtures.
    public Guid? IgnisThreadID { get; init; }
    // PORT-ONLY (web surfaces W26, ED-NEW-web-w26-1): a session that must run no tool at all (an artifact's nth.complete):
    // no built-in tools, no MCP servers, no plugins. Only providers that can guarantee it are given such a session.
    public bool NoTools { get; init; }
}

// :50-58. One exchange of a conversation, for replaying it into a session that starts over.
public sealed record TranscriptMessage
{
    // :51-54
    public enum Role { User, Assistant }

    public required Role TranscriptMessageRole { get; init; } // :56 Swift: role (R-NAME-3)
    public required string Text { get; init; }                // :57
}

// :61-79. A head a provider started inside the lead's session.
public sealed record AgentSpawn
{
    /// <summary>The provider's id for the head, which its later events carry.</summary>
    public required string Id { get; init; }        // :63-63

    /// <summary>Claude's task id, which is what stops the head; nil where the id above does.</summary>
    public string? TaskID { get; init; }             // :64-65

    /// <summary>The tool call in the lead's transcript that spawned the head.</summary>
    public string? ToolUseID { get; init; }          // :66-67

    public required string Description { get; init; } // :68

    /// <summary>The head's brief, when the provider says.</summary>
    public string? Prompt { get; init; }             // :69-70

    public string? Model { get; init; }              // :71

    /// <summary>
    /// The head profile the spawn was routed to, when the provider's report names one: Claude's and Copilot's
    /// agent definitions are one per profile, and Codex's spawn call carries the profile's model and effort,
    /// which the session matches back.
    /// </summary>
    public string? Profile { get; init; }            // :72-75

    /// <summary>
    /// Whether the spawning tool call returns before the head is done. A foreground head is finished by its
    /// tool result; a background one only by the provider saying so.
    /// </summary>
    public bool IsBackground { get; init; } = true;  // :76-78
}

// :82-124. Normalized events every provider adapter emits into a thread. R-ENUM-2: an associated-value enum
// ports to an abstract record with a private constructor and nested sealed records, in declaration order (the
// order itself is PR-RUNTIME-001, tokenSpend immediately after usage).
public abstract record ProviderEvent
{
    private ProviderEvent() { }

    public sealed record SessionReady(string SessionID) : ProviderEvent;                    // :83
    public sealed record TurnStarted(string? ProviderTurnID) : ProviderEvent;                // :84
    public sealed record MessageDelta(string Id, string Text) : ProviderEvent;               // :85
    public sealed record MessageCompleted(string Id, string Text) : ProviderEvent;           // :86
    public sealed record ReasoningDelta(string Id, string Text) : ProviderEvent;             // :87
    public sealed record ReasoningCompleted(string Id, string Text) : ProviderEvent;         // :88
    public sealed record ToolStarted(string Id, ToolCall Call) : ProviderEvent;              // :89
    public sealed record ToolOutput(string Id, string Text) : ProviderEvent;                 // :90
    public sealed record ToolUpdated(string Id, ToolUpdate Update) : ProviderEvent;          // :91
    public sealed record PlanDelta(string Id, string Text) : ProviderEvent;                  // :92
    public sealed record PlanCompleted(string Id, string Markdown) : ProviderEvent;          // :93
    public sealed record Todos(ValueList<TodoStep> Value) : ProviderEvent;                   // :94
    public sealed record Approval(ApprovalRequest Value) : ProviderEvent;                    // :95
    public sealed record Question(QuestionRequest Value) : ProviderEvent;                    // :96
    public sealed record RequestResolved(string Id) : ProviderEvent;                         // :97
    public sealed record Usage(ContextUsage Value) : ProviderEvent;                          // :98

    /// <summary>
    /// Tokens newly consumed by one request or turn, with the identity behind them. Distinct from
    /// <see cref="Usage"/>, which is context occupancy: an event here is spend, and a head's spend carries its
    /// agentID; the turn keeps each report for run metrics.
    /// </summary>
    public sealed record TokenSpend(global::Marrow.Core.Models.TokenSpend Value) : ProviderEvent; // :99-104

    public sealed record Diff(string Value) : ProviderEvent;                                 // :105
    public sealed record Notice(global::Marrow.Core.Models.Notice Value) : ProviderEvent;     // :106

    /// <summary>The account's usage limit is spent; the turn stops until it resets, at the time given when known.</summary>
    public sealed record UsageLimit(DateTimeOffset? ResetsAt, UsageAccountKey? Account = null) : ProviderEvent;                // :107-108

    public sealed record ModeChanged(InteractionMode Value) : ProviderEvent;                  // :109
    public sealed record Models(ValueList<ModelOption> Value, string? Current) : ProviderEvent; // :110
    public sealed record Commands(ValueList<SlashCommand> Value) : ProviderEvent;             // :111
    public sealed record Title(string Value) : ProviderEvent;                                 // :112
    public sealed record AssistantMessageID(string Value) : ProviderEvent;                    // :113
    public sealed record TurnCompleted(TurnStatus Status, string? Error) : ProviderEvent;     // :114
    public sealed record Exited(string? Error) : ProviderEvent;                               // :115

    /// <summary>A head started inside the session, or an existing one was described further.</summary>
    public sealed record AgentStarted(AgentSpawn Value) : ProviderEvent;                      // :116-117

    /// <summary>An event from inside a head's own transcript.</summary>
    public sealed record AgentEvent(string AgentID, ProviderEvent Value) : ProviderEvent;     // :118-119

    /// <summary>The provider's word on a running head: a progress note, its last tool, its spend.</summary>
    public sealed record AgentProgress(string AgentID, string? Summary, string? LastTool, long? Tokens, long? ToolCalls) : ProviderEvent; // :120-121

    /// <summary>A head finished, with the provider's summary of its result when it has one.</summary>
    public sealed record AgentFinished(string AgentID, TurnStatus Status, string? Summary) : ProviderEvent; // :122-123

    // DEVIATION OD-0134 / ED-NEW-ignis-contract: no Swift case. Ignis (port-only) needs one request's cache-aware usage
    // (uncached input, cache read, 5-minute / 1-hour / unknown-TTL writes, output); TokenSpend has no cache-write field
    // and is dropped when Claude reports iterations. Appended after the 31 Swift cases so their order is untouched.
    /// <summary>One request's or turn's cache usage, for Ignis (AgentID set: a head's).</summary>
    public sealed record CacheUsage(global::Marrow.Core.Port.Ignis.IgnisTurnUsage Value) : ProviderEvent;
    /// <summary>PORT-ONLY (OD-0134 Ignis, ED-NEW-ignis-runtime): the provider hinted the chat now runs on (or off) paid usage credits.</summary>
    public sealed record CacheBilling(bool OnCredits) : ProviderEvent;
}

// :126-134. Every field is independently optional; an absent field means "unchanged" at the application site
// (PR-RUNTIME-152), never "clear".
public sealed record ToolUpdate
{
    public string? Title { get; init; }             // :127
    public string? Detail { get; init; }             // :128
    public string? Output { get; init; }             // :129
    public ToolCall.Status? Status { get; init; }    // :130
    public long? ExitCode { get; init; }             // :131
    public ValueList<FileEdit>? Edits { get; init; } // :132
    public ToolCall.Kind? Kind { get; init; }        // :133
    /// <summary>PORT-ONLY (web surfaces W18, ED-NEW-web-w18-1): the structured part of an MCP tool result (images, resource links, UI resource, structuredContent). Replaces the row's payload when set.</summary>
    public ToolPayload? Payload { get; init; }
}

// :136-151. R-PROTO-1: a Swift protocol ports to an interface. R-ISO-1: the Swift @MainActor annotation means
// every member below is only ever called on the main actor; there is no per-call MainActor check here.
public interface ProviderSession // :137
{
    // :138
    Action<ProviderEvent>? OnEvent { get; set; }

    // :139
    bool IsRunning { get; }

    /// <summary>Launches the provider and returns its session id.</summary>
    Task<string> Start(); // :142

    Task Send(TurnInput input); // :143

    Task Interrupt(); // :144

    void ResolveApproval(string requestID, string optionID); // :145

    // PORT-ONLY: SK-12 / R1. Protocols without a denial message retain their own response shape.
    void ResolveApproval(string requestID, string optionID, string? reason) => ResolveApproval(requestID, optionID);

    void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers); // :146

    Task Compact(); // :147

    void Stop(); // :148

    /// <summary>Stops a head running inside the session. Returns whether the provider could.</summary>
    // :153-155. A requirement with an extension default -> default interface member (R-PROTO-1).
    Task<bool> StopAgent(string id) => Task.FromResult(false); // :150, :153-155
}

// :157-176. R-ERR-3: a Swift error enum keeps its cases as a nested record union.
public sealed class ProviderError(ProviderError.Case errorCase) : SwiftError
{
    public abstract record Case
    {
        private Case() { }
        public sealed record NotInstalled(ProviderKind Provider) : Case; // :158
        public sealed record NotRunning : Case;                          // :159
        public sealed record Failed(string Message) : Case;              // :160
    }

    public Case ErrorCase { get; } = errorCase;

    // :162-174
    public override string? ErrorDescription => ErrorCase switch
    {
        Case.NotInstalled(var provider) => provider.IsAPIKeyBased
            // :165-166. U+2192 RIGHTWARDS ARROW with one space on each side.
            ? $"{provider.DisplayName} needs an API key. Add one in Settings \U00002192 Providers \U00002192 {provider.DisplayName}."
            // :167-168. The login command is wrapped in single backtick characters.
            : $"{provider.DisplayName} is not installed. Install it and sign in with `{provider.LoginCommand}`.",
        Case.NotRunning => "The agent session is not running.", // :171
        Case.Failed(var message) => message,                    // :173
        _ => throw new UnreachableException(),
    };
}

// :178-209
public static class ToolTitles
{
    // :179-192. The prefixes tested in order; the first match is removed, and only one level of matching
    // quotes is stripped.
    private static readonly string[] ShellPrefixes =
    [
        "/bin/zsh -lc ", "/bin/bash -lc ", "bash -lc ", "zsh -lc ", "/bin/sh -c ", "sh -c ", // :182
    ];

    /// <summary>Removes the `/bin/zsh -lc '...'` wrapper providers put around shell commands.</summary>
    public static string UnwrapShell(string command)
    {
        var trimmed = command.Trimmed(); // :181, .whitespacesAndNewlines
        foreach (var prefix in ShellPrefixes)
        {
            if (!SwiftString.HasPrefix(trimmed, prefix)) continue; // :183
            var rest = SwiftString.DropFirst(trimmed, SwiftString.Count(prefix)); // :184
            if (SwiftString.Count(rest) >= 2) // :185
            {
                var first = SwiftString.Prefix(rest, 1);
                var last = SwiftString.Suffix(rest, 1);
                if ((first == "'" || first == "\"") && last == first) // :185
                {
                    rest = SwiftString.DropLast(SwiftString.DropFirst(rest)); // :186
                    if (first == "'") rest = rest.Replace("'\\''", "'", StringComparison.Ordinal); // :187
                }
            }
            return rest; // :189
        }
        return trimmed; // :191
    }

    // :194-197. DEVIATION ED-019(3)/ED-137/ED-039(2): the root-containment test is the same case-insensitive,
    // forward-slashed comparison TouchedPaths.Relative already applies (settled at SwiftPath.TailUnder, whose
    // own doc comment cites this site by name), in place of Swift's byte-exact "/"-suffixed string prefix test.
    // The Swift text itself carries no case folding or separator translation; the port makes the same
    // separator/case decision this site and TouchedPaths.relative share (platform gap, recorded here).
    public static string RelativePath(string path, string root) => SwiftPath.TailUnder(path, root) ?? path;

    /// <summary>Counts additions and deletions in a unified diff.</summary>
    public static (int Additions, int Deletions) DiffStats(string diff)
    {
        var additions = 0;
        var deletions = 0;
        foreach (var line in SwiftString.Split(diff, "\n", omittingEmptySubsequences: false)) // :203
        {
            if (SwiftString.HasPrefix(line, "+++") || SwiftString.HasPrefix(line, "---")) continue; // :204
            if (SwiftString.HasPrefix(line, "+")) additions++; // :205
            else if (SwiftString.HasPrefix(line, "-")) deletions++; // :205
        }
        return (additions, deletions); // :207
    }
}

// :211-219 (B3). Re-cites PR-PROVCLI-053, moved here from the Claude session.
public static class ToolText
{
    /// <summary>The text of a content-block array, as tool results and messages carry it.</summary>
    public static string Blocks(JSONValue? value) // :212-218
    {
        if (value is null || value.IsNull) return ""; // :214
        if (value.JSONValueString is { } text) return text; // :215
        if (value.JSONValueArray is not { } blocks) return ""; // :216
        return string.Join("\n", blocks.Select(block => block["text"]?.JSONValueString).OfType<string>()); // :217
    }
}

// :221-230 (B3). Re-cites PR-PROVCLI-248 and PR-PROVCLI-285, moved here from the Pi and Command Code sessions.
// R-NAME-4: an extension of a nested enum is `static class <Owner><Nested>Extensions`.
public static class ApprovalRequestKindExtensions
{
    /// <summary>The key an "approve for session" grant is remembered under.</summary>
    public static string GrantKey(this ApprovalRequest.Kind kind) => kind switch // :222-229
    {
        ApprovalRequest.Kind.Command => "command",
        ApprovalRequest.Kind.FileChange => "fileChange",
        ApprovalRequest.Kind.Tool or ApprovalRequest.Kind.Permissions or ApprovalRequest.Kind.Plan => "tool",
        _ => throw new UnreachableException(),
    };
}

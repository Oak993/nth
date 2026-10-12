// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.3, 12.4, 12.5, 15.5 "Policy check"; unit AS-4; ED-NEW-dextra-as4-4). No Swift twin.
// One call of an nth-dextra tool, the same on every route (MCP, native, fenced): the tool must exist and be offered to
// the chat, the arguments must fit its schema, then the policy engine decides, then the audit line is written, and only
// then does anything act: Run on Allow, a card on Ask, nothing on Deny. A policy that cannot be read, a log that cannot
// be written or a host that throws refuses the call (fail closed). Until AS-7 turns auto-answering on, an Allow runs
// alone only for the tools that read nth's own state, stop work, or put something in front of the owner or the parent.
// A result that carries untrusted material taints the chat before the model reads it, and a call is decided only after
// the chat's earlier events reached its taint (AS-7).
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;

namespace Marrow.Services.Assistant;

public sealed class AssistantToolRunner(AssistantRegistration registration, Func<Classification, PolicyContext, PolicyDecision>? policy = null)
{
    private readonly Func<Classification, PolicyContext, PolicyDecision> decide = policy ?? PolicyEngine.Decide;

    public AssistantRegistration Registration => registration;

    /// <summary>Runs one call and says what to tell the model. Never throws, except when the call is cancelled.</summary>
    public async Task<AssistantToolResult> Call(string? name, JSONValue? arguments, AssistantRoute route, CancellationToken cancellationToken)
    {
        var chat = registration.Chat;
        var host = registration.Host;
        if (!registration.IsLive) return AssistantToolResult.Failed("Not run: this chat's " + AssistantNames.Server + " tools are turned off.");
        var tool = AssistantToolCatalog.Find(name);
        if (tool is null || !tool.OfferedTo(chat.Role))
        {
            TryAudit(host, Unplaced(chat, name, route, "unknown tool"));
            return AssistantToolResult.Failed($"Not run: \"{name}\" is not an {AssistantNames.Server} tool this chat can use.");
        }
        var args = arguments is null or JSONValue.Null ? JSONValue.FromDictionaryLiteral() : arguments;
        if (tool.Validate(args) is { } problem)
        {
            TryAudit(host, Unplaced(chat, tool.Name, route, "invalid arguments"));
            return AssistantToolResult.Failed($"Not run: {problem}.");
        }

        var classification = ActionClassifier.Classify(new ActionRequest.AssistantTool(tool.Name), new ClassifierContext());
        // AS-7: the chat's earlier events (a page it read) reach its taint before the call is decided (ED-NEW-dextra-as7-18).
        try { await host.Settle(chat, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            TryAudit(host, Entry(chat, tool, args, classification, route, "refused: the chat's earlier tool calls could not be checked first", null));
            return AssistantToolResult.Failed("Not run: nth could not check this chat's earlier tool calls first.");
        }
        PolicyDecision decision;
        try { decision = decide(classification, host.PolicyContext(chat)); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            TryAudit(host, Entry(chat, tool, args, classification, route, "refused: the rules could not be read", null));
            return AssistantToolResult.Failed("Not run: nth could not check its rules for this call.");
        }
        if (decision.Verdict == Verdict.Allow && !tool.RunsBeforeAutoAnswer && !AutoAnswering(host))
            decision = decision with
            {
                Verdict = Verdict.Ask, Rule = PolicyRule.AutoAnswerOff, GrantsUsed = ValueList<Guid>.Empty,
                Reason = "Asks: nth does not answer for the owner until its injection checks pass",
            };
        var call = new AssistantToolCall(chat, tool, args, classification, decision, route);

        // The decision is logged before anything acts; a log that cannot be written stops the call here (12.5).
        try { host.Audit(Entry(call, Outcome(decision))); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return AssistantToolResult.Failed("Not run: the activity log could not be written.");
        }

        switch (decision.Verdict)
        {
            case Verdict.Deny:
                if (decision.Rule == PolicyRule.NeedsUntaintedRequest)
                {
                    try { host.Offer(call); }
                    catch (Exception error) when (error is not OperationCanceledException) { }
                    return AssistantToolResult.Failed("Refused: " + PolicyEngine.NeedsUntaintedRequest + ". This chat read untrusted content, so it cannot "
                        + "change standing configuration; the owner can raise it himself from the card nth offers him.");
                }
                return AssistantToolResult.Failed("Refused: " + decision.Reason + ".");
            case Verdict.Ask:
                string card;
                try { card = await host.Ask(call, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    TryAudit(host, Entry(call, "failed: the card could not be raised"));
                    return AssistantToolResult.Failed("Not run: the owner's card could not be raised.");
                }
                return AssistantToolResult.Done($"Waiting for the owner (card {card}): {decision.Reason}. Do not call it again; his answer comes back to this chat.")
                    with { DecisionId = card };
            default:
                AssistantToolResult result;
                try { result = await host.Run(call, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    result = AssistantToolResult.Failed("Failed: " + error.Message);
                }
                TryAudit(host, Entry(call, result.IsError ? "failed" : "done"));
                // 12.4: what the result carries in taints the chat before the model reads it; when the taint cannot be
                // taken, the result is withheld (AS-7).
                if (result.Taint.IsTainted)
                {
                    try { host.Absorb(chat, result.Taint); }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        TryAudit(host, Entry(call, "withheld: its taint could not be recorded"));
                        return AssistantToolResult.Failed("Not shown: nth could not mark this result as untrusted content.");
                    }
                }
                return result;
        }
    }

    private static bool AutoAnswering(IAssistantToolHost host)
    {
        try { return host.AutoAnswering; }
        catch (Exception error) when (error is not OperationCanceledException) { return false; }
    }

    private static void TryAudit(IAssistantToolHost host, AuditEntry entry)
    {
        try { host.Audit(entry); }
        catch (Exception error) when (error is not OperationCanceledException) { }
    }

    private static string Outcome(PolicyDecision decision) => decision.Verdict switch
    {
        Verdict.Allow => "allowed",
        Verdict.Ask => "asked: " + decision.Rule,
        _ => "refused: " + decision.Rule,
    };

    /// <summary>The audit line of a decided call.</summary>
    public static AuditEntry Entry(AssistantToolCall call, string result) =>
        Entry(call.Chat, call.Tool, call.Arguments, call.Classification, call.Route, result, call.Decision) with
        {
            Inputs = [AuditInput.Plain("call", call.Id), .. Inputs(call.Tool, call.Arguments), AuditInput.Plain("route", call.Route.ToString())],
        };

    private static AuditEntry Entry(AssistantChat chat, AssistantToolDefinition tool, JSONValue args, Classification classification, AssistantRoute route,
        string result, PolicyDecision? decision) => new()
    {
        Assistant = chat.AssistantId,
        Mission = chat.MissionId?.ToString("D"),
        Agent = chat.Thread.ToString("D"),
        Tool = AssistantNames.Server + "/" + tool.Name,
        ActionClass = classification.Class.ToString(),
        Target = classification.Target,
        Inputs = [.. Inputs(tool, args), AuditInput.Plain("route", route.ToString())],
        Result = result,
        AllowedBy = decision is { Verdict: Verdict.Allow } allowed
            ? allowed.GrantsUsed.Count > 0 ? string.Join(",", allowed.GrantsUsed.Select(g => g.ToString("D"))) : "rule:" + allowed.Rule
            : null,
    };

    // A call that never reached the engine (no such tool, arguments that do not fit): its name and route only, never
    // the arguments, which may be anything.
    private static AuditEntry Unplaced(AssistantChat chat, string? name, AssistantRoute route, string why) => new()
    {
        Assistant = chat.AssistantId,
        Mission = chat.MissionId?.ToString("D"),
        Agent = chat.Thread.ToString("D"),
        Tool = AssistantNames.Server + "/" + Clip(name ?? "", 100),
        ActionClass = ActionClass.Unknown.ToString(),
        Inputs = [AuditInput.Plain("route", route.ToString())],
        Result = "refused: " + why,
    };

    /// <summary>
    /// The inputs as the log keeps them: what the agent wrote (briefs, messages, notes) as its length only, names and ids
    /// as text (masked for secrets by the log), numbers and switches as themselves.
    /// </summary>
    public static IReadOnlyList<AuditInput> Inputs(AssistantToolDefinition tool, JSONValue args)
    {
        var inputs = new List<AuditInput>();
        if (args.JSONValueObject is not { } given) return inputs;
        foreach (var p in tool.Parameters)
        {
            if (!given.TryGetValue(p.Name, out var value) || value is JSONValue.Null) continue;
            switch (p.Type)
            {
                case AssistantParameterType.String:
                    inputs.Add(p.FreeText ? AuditInput.Typed(p.Name, value.JSONValueString) : AuditInput.Plain(p.Name, value.JSONValueString));
                    break;
                case AssistantParameterType.StringList:
                    var items = (value.JSONValueArray ?? []).Select(i => i.JSONValueString ?? "").ToList();
                    inputs.Add(p.FreeText ? AuditInput.Typed(p.Name, string.Join("\n", items)) : AuditInput.Plain(p.Name, string.Join(", ", items)));
                    break;
                case AssistantParameterType.Boolean:
                    inputs.Add(AuditInput.Boolean(p.Name, value.JSONValueBool == true));
                    break;
                default:
                    inputs.Add(AuditInput.Numeric(p.Name, value.JSONValueDouble ?? 0));
                    break;
            }
        }
        return inputs;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}

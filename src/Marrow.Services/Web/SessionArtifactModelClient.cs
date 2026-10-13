// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5 "nth.complete ... a fresh, context-free call to the thread's model (same
// provider and model)"; ED-NEW-web-w13-4). One artifact call as one turn of a new headless provider session on the
// thread's provider and model, made the way Ignis Advisor's is (the provider's first enabled account, its login or API
// key) but in its own empty folder, in plan mode, as a final report: no thread history, no project, no memory, no tools
// (any approval the session asks for is declined, any question answered with nothing). The turn's text is the artifact's
// system text (after nth's untrusted-app line) and its own messages. Streaming text goes to the page as it arrives; the
// reply stops at the call's output cap. Tokens are the session's reported usage, or an estimate (four characters a token)
// where a provider reports none. No Swift counterpart.
// + ED-NEW-web-w26-1 (W26 security review): plan mode alone still let a Claude session read files with no approval and
// reach the user's MCP servers, so the session runs with SessionConfiguration.NoTools, and only on providers that can
// guarantee it (Claude through --tools "", the API providers by sending no tool list). Any other provider has no model
// an artifact can use.
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Advisor;
using Marrow.Core.Port.Ignis;
using Marrow.Services.Providers;

namespace Marrow.Services.Web;

/// <param name="choice">The thread's provider and model; null when the thread has none.</param>
/// <param name="name">The model's display name for the consent card.</param>
/// <param name="makeSession">A new headless session on that provider and model, in an empty folder.</param>
public sealed class SessionArtifactModelClient(Func<Guid, AdvisorChoice?> choice, Func<AdvisorChoice, string> name, Func<AdvisorChoice, ProviderSession> makeSession)
    : IArtifactModelClient
{
    public string? ModelName(Guid thread) => choice(thread) is { } chosen && RunsWithoutTools(chosen.Provider) ? name(chosen) : null;

    /// <summary>ED-NEW-web-w26-1: the providers whose sessions honour <see cref="SessionConfiguration.NoTools"/>.</summary>
    public static bool RunsWithoutTools(ProviderKind provider) =>
        provider == ProviderKind.Claude || provider == ProviderKind.Local || provider.IsAPIKeyBased;

    /// <summary>
    /// ED-NEW-web-w26-1: the one configuration an artifact call runs on: the account's login or key, an empty folder,
    /// supervised plan mode, and no tools, servers, plugins, Hydra or history.
    /// </summary>
    public static SessionConfiguration Configuration(AdvisorChoice chosen, Guid account, SwiftURL? executable, SwiftURL folder,
        ValueMap<string, string> environment, string? apiKey) => new()
    {
        Provider = chosen.Provider,
        Executable = executable,
        WorkingDirectory = folder,
        AccountId = account,
        Environment = environment,
        Model = chosen.Model.Length == 0 ? null : chosen.Model,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Plan,
        ApiKey = apiKey,
        AntiSlopEnabled = false,
        NoTools = true,
    };

    public async Task<ArtifactCompletionResult> CompleteAsync(Guid thread, ArtifactCompletionRequest request, Action<string>? onDelta, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var chosen = choice(thread) ?? throw new ArtifactBridgeException(ArtifactBridgeErrors.Unavailable, "This chat has no model an artifact can use.");
        if (!RunsWithoutTools(chosen.Provider))
            throw new ArtifactBridgeException(ArtifactBridgeErrors.Unavailable, chosen.Provider.DisplayName + " cannot answer an artifact without tools; use Claude or an API provider.");
        var prompt = Prompt(request);
        var maxCharacters = (long)request.MaxTokens * 4;
        var session = makeSession(chosen);
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var text = new StringBuilder();
        string? completed = null;
        IgnisTurnUsage? usage = null;
        TokenSpend? spend = null;
        string? failure = null;
        var capped = false;
        session.OnEvent = providerEvent =>
        {
            switch (providerEvent)
            {
                case ProviderEvent.MessageDelta delta when !capped:
                    var piece = delta.Text;
                    if (text.Length + piece.Length > maxCharacters)
                    {
                        piece = piece[..(int)Math.Max(0, maxCharacters - text.Length)];
                        capped = true;
                    }

                    text.Append(piece);
                    if (piece.Length > 0) onDelta?.Invoke(piece);
                    if (capped) finished.TrySetResult(true);
                    break;
                case ProviderEvent.MessageCompleted message when message.Text.Trim().Length > 0:
                    completed = completed is null ? message.Text : completed + "\n\n" + message.Text;
                    break;
                case ProviderEvent.CacheUsage report when report.Value.AgentID is null:
                    usage = usage is null ? report.Value : usage with
                    {
                        UncachedInput = usage.UncachedInput + report.Value.UncachedInput,
                        CacheRead = usage.CacheRead + report.Value.CacheRead,
                        CacheWrite5m = usage.CacheWrite5m + report.Value.CacheWrite5m,
                        CacheWrite1h = usage.CacheWrite1h + report.Value.CacheWrite1h,
                        CacheWriteUnknownTtl = usage.CacheWriteUnknownTtl + report.Value.CacheWriteUnknownTtl,
                        Output = usage.Output + report.Value.Output,
                    };
                    break;
                case ProviderEvent.TokenSpend tokens when tokens.Value.AgentID is null:
                    spend = spend is null ? tokens.Value : new TokenSpend(spend.Provider, spend.TotalTokens + tokens.Value.TotalTokens,
                        (spend.InputTokens ?? 0) + (tokens.Value.InputTokens ?? 0), (spend.OutputTokens ?? 0) + (tokens.Value.OutputTokens ?? 0),
                        (spend.CachedInputTokens ?? 0) + (tokens.Value.CachedInputTokens ?? 0), spend.Model ?? tokens.Value.Model);
                    break;
                case ProviderEvent.Approval approval:
                    if (approval.Value.Options.FirstOrDefault(o => o.OptionRole is ApprovalRequest.Option.Role.Decline or ApprovalRequest.Option.Role.Cancel) is { } decline)
                        session.ResolveApproval(approval.Value.Id, decline.Id, "An artifact's model call runs no tools.");
                    break;
                case ProviderEvent.Question question:
                    session.AnswerQuestion(question.Value.Id, ValueMap<string, ValueList<string>>.Empty);
                    break;
                case ProviderEvent.TurnCompleted turn:
                    if (turn.Status != TurnStatus.Completed) failure = turn.Error ?? "the turn " + SwiftEnum<TurnStatus>.RawValue(turn.Status) + ".";
                    finished.TrySetResult(true);
                    break;
                case ProviderEvent.Exited exited:
                    failure ??= exited.Error ?? "the session ended.";
                    finished.TrySetResult(true);
                    break;
            }
        };
        using var registration = cancellationToken.Register(() => finished.TrySetCanceled(cancellationToken));
        try
        {
            await session.Start().ConfigureAwait(true);
            await session.Send(new TurnInput
            {
                Text = prompt,
                Images = ValueList<Attachment>.Empty,
                Model = chosen.Model.Length == 0 ? null : chosen.Model,
                RuntimeMode = RuntimeMode.Supervised,
                InteractionMode = InteractionMode.Plan,
                IsFinalReport = true,
            }).ConfigureAwait(true);
            await finished.Task.ConfigureAwait(true);
        }
        finally
        {
            session.OnEvent = null;
            try { session.Stop(); }
            catch (Exception error) when (SwiftTry.IsThrownError(error)) { } // Stopping is best effort; the reply is already in hand.
        }

        // A provider that sends no deltas still answers in full (capped the same way).
        var reply = text.Length > 0 ? text.ToString() : completed ?? "";
        if (reply.Length > maxCharacters) reply = reply[..(int)maxCharacters];
        if (text.Length == 0 && reply.Length > 0) onDelta?.Invoke(reply);
        if (reply.Length == 0 && failure is not null) throw new ArtifactBridgeException(ArtifactBridgeErrors.Failed, "The model call failed: " + failure);
        var (input, output) = usage is not null ? ((int)(usage.UncachedInput + usage.CacheRead + usage.CacheWrite5m + usage.CacheWrite1h + usage.CacheWriteUnknownTtl), (int)usage.Output)
            : spend is not null ? ((int)(spend.InputTokens ?? Math.Max(0, spend.TotalTokens - (spend.OutputTokens ?? 0))), (int)(spend.OutputTokens ?? 0))
            : (Estimate(prompt), Estimate(reply));
        return new ArtifactCompletionResult(reply, input, output);
    }

    /// <summary>
    /// The turn's one text: the system text, then the app's own messages (a single message as it is; several marked by
    /// role, so the model answers the last user message).
    /// </summary>
    public static string Prompt(ArtifactCompletionRequest request)
    {
        var builder = new StringBuilder(request.System).Append("\n\n");
        if (request.Messages.Count == 1) return builder.Append(request.Messages[0].Content).ToString();
        builder.Append("The app's conversation follows. Answer its last user message as the assistant, in plain text.\n");
        foreach (var message in request.Messages)
            builder.Append("\n<").Append(message.Role).Append(">\n").Append(message.Content).Append("\n</").Append(message.Role).Append(">\n");
        return builder.ToString();
    }

    private static int Estimate(string text) => (text.Length + 3) / 4;
}

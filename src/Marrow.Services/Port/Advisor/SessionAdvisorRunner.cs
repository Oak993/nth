// PORT-ONLY: Ignis Advisor (ED-NEW-ignisadvisor-2). One piece of advice as one turn of a headless provider session on
// the advisor's provider and model: the same sessions, logins and API keys a chat uses (Claude, Codex, the ACP CLIs, the
// API providers), so every provider nth supports can advise. The turn is a final report (one reply, no tools where the
// session can arrange that) in plan mode; any approval it still asks for is declined, any question answered with nothing.
// The reply is the turn's assistant text; the usage is the sum of the turn's own cache-aware usage reports (Ignis's), or,
// where a provider reports none, its token spend.
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Advisor;
using Marrow.Core.Port.Ignis;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.Advisor;

public sealed class SessionAdvisorRunner(Func<AdvisorChoice, ProviderSession> makeSession) : IAdvisorRunner
{
    public async Task<AdvisorRun> Run(AdvisorChoice choice, string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = makeSession(choice);
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new List<string>();
        var deltas = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        IgnisTurnUsage? cacheUsage = null;
        TokenSpend? spend = null;
        string? failure = null;
        session.OnEvent = providerEvent =>
        {
            switch (providerEvent)
            {
                case ProviderEvent.MessageDelta delta:
                    if (!deltas.TryGetValue(delta.Id, out var builder)) deltas[delta.Id] = builder = new StringBuilder();
                    builder.Append(delta.Text);
                    break;
                case ProviderEvent.MessageCompleted message when message.Text.Trim().Length > 0:
                    completed.Add(message.Text.Trim());
                    break;
                case ProviderEvent.CacheUsage usage when usage.Value.AgentID is null:
                    cacheUsage = cacheUsage is null ? usage.Value : Add(cacheUsage, usage.Value);
                    break;
                case ProviderEvent.TokenSpend tokens when tokens.Value.AgentID is null:
                    spend = spend is null ? tokens.Value : new TokenSpend(spend.Provider, spend.TotalTokens + tokens.Value.TotalTokens,
                        (spend.InputTokens ?? 0) + (tokens.Value.InputTokens ?? 0), (spend.OutputTokens ?? 0) + (tokens.Value.OutputTokens ?? 0),
                        (spend.CachedInputTokens ?? 0) + (tokens.Value.CachedInputTokens ?? 0), spend.Model ?? tokens.Value.Model);
                    break;
                case ProviderEvent.Approval approval:
                    if (approval.Value.Options.FirstOrDefault(o => o.OptionRole is ApprovalRequest.Option.Role.Decline or ApprovalRequest.Option.Role.Cancel) is { } decline)
                        session.ResolveApproval(approval.Value.Id, decline.Id, "The advisor only gives advice; it runs no tools.");
                    break;
                case ProviderEvent.Question question:
                    session.AnswerQuestion(question.Value.Id, ValueMap<string, ValueList<string>>.Empty);
                    break;
                case ProviderEvent.TurnCompleted turn:
                    if (turn.Status != TurnStatus.Completed) failure = turn.Error ?? "the advisor's turn " + SwiftEnum<TurnStatus>.RawValue(turn.Status) + ".";
                    finished.TrySetResult(true);
                    break;
                case ProviderEvent.Exited exited:
                    failure ??= exited.Error ?? "the advisor's session ended.";
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
                Model = choice.Model.Length == 0 ? null : choice.Model,
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
            catch (Exception error) when (SwiftTry.IsThrownError(error)) { } // Stopping is best effort; the advice is already in hand.
        }
        var text = completed.Count > 0 ? string.Join("\n\n", completed) : string.Join("\n\n", deltas.Values.Select(b => b.ToString().Trim()).Where(t => t.Length > 0));
        if (text.Length == 0 && failure is not null) throw new InvalidOperationException(failure);
        return new AdvisorRun(text, cacheUsage ?? FromSpend(choice, spend));
    }

    private static IgnisTurnUsage Add(IgnisTurnUsage a, IgnisTurnUsage b) => a with
    {
        UncachedInput = a.UncachedInput + b.UncachedInput,
        CacheRead = a.CacheRead + b.CacheRead,
        CacheWrite5m = a.CacheWrite5m + b.CacheWrite5m,
        CacheWrite1h = a.CacheWrite1h + b.CacheWrite1h,
        CacheWriteUnknownTtl = a.CacheWriteUnknownTtl + b.CacheWriteUnknownTtl,
        Output = a.Output + b.Output,
        Reported = a.Reported || b.Reported,
    };

    /// <summary>
    /// A token spend as usage. ENGINEERING DECISION: its input counts the cached tokens too (the OpenAI convention the
    /// API sessions and Codex report), so the uncached part is input minus cached.
    /// </summary>
    private static IgnisTurnUsage? FromSpend(AdvisorChoice choice, TokenSpend? spend)
    {
        if (spend is null) return null;
        var cached = Math.Max(0, spend.CachedInputTokens ?? 0);
        var input = Math.Max(cached, spend.InputTokens ?? Math.Max(0, spend.TotalTokens - (spend.OutputTokens ?? 0)));
        return new IgnisTurnUsage(choice.Provider, choice.Model, null, input - cached, cached, 0, 0, 0, Math.Max(0, spend.OutputTokens ?? 0), null, null, spend.CachedInputTokens is not null);
    }
}

// PORT-ONLY: web surfaces W25, ask_user (DESIGN.md 7.3, 14 W25; ED-NEW-web-w25-1). One chat's runtime as the ask_user tool sees it
// and as the card sees it. The tool (Marrow.Services.Web.AskUserTool, from the nth-ui server or the native tool loop) puts a card in
// the thread through <see cref="AskUser"/> and waits; the card answers through <see cref="AnswerAsk"/> or <see cref="SkipAsk"/>; Stop
// (<see cref="Interrupt"/>) ends the wait with a stopped card.
//
// A card is one persisted timeline item (TimelineItem.Content.Ask), so it survives a restart as a waiting card. The wait itself lives
// only as long as the provider's call: when the call is gone (the app was restarted, the provider ended the call, the turn finished)
// the card stays waiting, and the person's answer then starts a new turn whose first message carries the question and the answer.
using System.Globalization;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Web;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    private readonly Dictionary<string, TaskCompletionSource<AskOutcome>> askWaits = new(StringComparer.Ordinal);
    private readonly HashSet<string> hiddenAskToolIds = new(StringComparer.Ordinal);
    private int liveAsks;

    /// <summary>How many ask_user questions a model is waiting on now. The turn shows "Waiting for your answer" while it is above zero.</summary>
    public int LiveAskCount { get => Read(liveAsks); private set => Set(ref liveAsks, value); }

    /// <summary>True while a model waits for the person's answer to an ask_user question.</summary>
    public bool IsWaitingForAnswer => LiveAskCount > 0;

    /// <summary>The ask_user cards still waiting for the person, whether a call waits on them or the call is gone (after a restart).</summary>
    public ValueList<TimelineEntry> WaitingAskEntries =>
        new(Entries.Where(entry => entry.Item.TimelineItemContent is TimelineItem.Content.Ask { Value.IsWaiting: true }));

    /// <summary>The chat's side of ask_user, for the process-wide directory (AskUserStartup).</summary>
    internal IAskUserHost AskHost => new RuntimeAskHost(this);

    /// <summary>
    /// Puts <paramref name="card"/> in the thread and waits for the person (on the main actor). The wait ends with the person's answer, a
    /// skip, a Stop, or, when the provider's call ends first (<paramref name="cancellation"/>), detached: the card then stays waiting.
    /// </summary>
    internal async Task<AskOutcome> AskUser(AskCard card, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested || askWaits.ContainsKey(card.Id)) return new AskOutcome(AskOutcomeKind.Detached, card);
        var wait = new TaskCompletionSource<AskOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        askWaits[card.Id] = wait;
        LiveAskCount = askWaits.Count;
        // A model whose first call ended early (a provider's own tool time limit) asks the same questions again: the new card takes the stale one's place.
        foreach (var stale in Entries.Where(entry => entry.Item.TimelineItemContent is TimelineItem.Content.Ask { Value.IsWaiting: true } old
                     && !askWaits.ContainsKey(old.Value.Id) && SameQuestions(old.Value, card)).ToArray())
            Remove(stale.Id);
        Append(new TimelineItem(TurnIDForNewRows, new TimelineItem.Content.Ask(card)));
        SaveNow(); // a quit a moment later still finds the question
        App?.ThreadNeedsAttention(ThreadID);
        using var registration = cancellation.Register(() => MainActor.Post(() => DetachAsk(card.Id)));
        return await wait.Task;
    }

    /// <summary>
    /// The person's answers. Returns the check (its problems are for the card to show) or null when the card is not waiting. When the
    /// answers are valid the card closes as answered; a model waiting on it gets them as the tool's result, and with no call waiting
    /// (after a restart) a new turn starts carrying the question and the answers.
    /// </summary>
    public AskCheck? AnswerAsk(string cardId, IReadOnlyDictionary<string, IReadOnlyList<string>> given, CultureInfo? culture = null)
    {
        if (WaitingAsk(cardId) is not ({ } entry, { } card)) return null;
        var check = AskUserSchema.Check(card, given, culture);
        if (!check.IsValid) return check;
        var closed = card.Closed(AskState.Answered, check.Answers);
        CloseAsk(entry, closed);
        if (!Finish(cardId, AskOutcomeKind.Answered, closed)) ResumeFromAsk(closed);
        return check;
    }

    /// <summary>The person chose not to answer: the card closes as skipped and a model waiting on it is told so.</summary>
    public void SkipAsk(string cardId)
    {
        if (WaitingAsk(cardId) is not ({ } entry, { } card)) return;
        var closed = card.Closed(AskState.Skipped);
        CloseAsk(entry, closed);
        Finish(cardId, AskOutcomeKind.Skipped, closed);
    }

    /// <summary>Stop: every question the running turn waits on closes as stopped, and the model waiting on it is told to stop.</summary>
    private void StopAsks()
    {
        foreach (var cardId in askWaits.Keys.ToArray())
        {
            if (WaitingAsk(cardId) is not ({ } entry, { } card)) continue;
            var closed = card.Closed(AskState.Stopped);
            CloseAsk(entry, closed);
            Finish(cardId, AskOutcomeKind.Stopped, closed);
        }
    }

    /// <summary>The provider's call is gone: nobody waits on the card any more, and it stays waiting for the person.</summary>
    private void DetachAsk(string cardId) => Finish(cardId, AskOutcomeKind.Detached, WaitingAsk(cardId)?.Card);

    private void DetachAllAsks()
    {
        foreach (var cardId in askWaits.Keys.ToArray()) DetachAsk(cardId);
    }

    private void DetachAsksOfTurn(Guid turnID)
    {
        foreach (var cardId in askWaits.Keys.ToArray())
            if (AskEntry(cardId) is { } entry && entry.TurnID == turnID) DetachAsk(cardId);
    }

    private bool Finish(string cardId, AskOutcomeKind kind, AskCard? card)
    {
        if (!askWaits.Remove(cardId, out var wait)) return false;
        LiveAskCount = askWaits.Count;
        var held = card ?? (AskEntry(cardId)?.Item.TimelineItemContent as TimelineItem.Content.Ask)?.Value
            ?? new AskCard { Id = cardId, Questions = ValueList<AskQuestion>.Empty };
        wait.TrySetResult(new AskOutcome(kind, held));
        return true;
    }

    private static bool SameQuestions(AskCard a, AskCard b) =>
        a.Questions.Count == b.Questions.Count
        && a.Questions.Zip(b.Questions).All(pair => string.Equals(pair.First.Prompt.Trim(), pair.Second.Prompt.Trim(), StringComparison.OrdinalIgnoreCase));

    private TimelineEntry? AskEntry(string cardId) =>
        Entries.FirstOrDefault(entry => entry.Item.TimelineItemContent is TimelineItem.Content.Ask ask && ask.Value.Id == cardId);

    private (TimelineEntry Entry, AskCard Card)? WaitingAsk(string cardId) =>
        AskEntry(cardId) is { } entry && entry.Item.TimelineItemContent is TimelineItem.Content.Ask { Value.IsWaiting: true } ask ? (entry, ask.Value) : null;

    private void CloseAsk(TimelineEntry entry, AskCard closed)
    {
        entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Ask(closed) };
        saveRevision++;
        SaveNow();
    }

    // The answer to a question whose call is gone starts a new turn, as any message would (it queues behind a turn already running).
    private void ResumeFromAsk(AskCard answered) =>
        _ = MainActor.Spawn(async () =>
        {
            await EnsureLoaded();
            await StartTurn(AskUserWords.ResumeMessage(answered), Array.Empty<Attachment>());
        });

    /// <summary>
    /// A provider's own row for the ask_user tool is left out: the card is its place. True when <paramref name="event"/> is such a row's
    /// event (its start, output or update) and the timeline must not draw it.
    /// </summary>
    private bool HidesAskToolRow(ProviderEvent @event)
    {
        switch (@event)
        {
            case ProviderEvent.ToolStarted started when AskUserTool.IsProviderRow(started):
                hiddenAskToolIds.Add(started.Id);
                return true;
            case ProviderEvent.ToolOutput output: return hiddenAskToolIds.Contains(output.Id);
            case ProviderEvent.ToolUpdated update: return hiddenAskToolIds.Contains(update.Id);
            default: return false;
        }
    }

    /// <summary>
    /// A provider that asks the person to allow the ask_user tool before it runs it (Codex, Copilot, ACP in a supervised chat) is answered at
    /// once: the tool only shows the person a question, and the question is the ask. True when <paramref name="approval"/> was such a request,
    /// known by the server and tool the provider's protocol named, never by its text (ED-NEW-web-sec-rev-1).
    /// </summary>
    private bool AutoApprovesAsk(ProviderEvent.Approval approval)
    {
        var request = approval.Value;
        if (!AskUserTool.IsApprovalFor(approval) || request.Options.FirstOrDefault(option => option.OptionRole == ApprovalRequest.Option.Role.Approve) is not { } approve) return false;
        session?.ResolveApproval(request.Id, approve.Id);
        return true;
    }

    private sealed class RuntimeAskHost(ThreadRuntime runtime) : IAskUserHost
    {
        public Task<AskOutcome> Ask(AskCard card, CancellationToken cancellation) => MainActor.Invoke(() => runtime.AskUser(card, cancellation));
    }
}

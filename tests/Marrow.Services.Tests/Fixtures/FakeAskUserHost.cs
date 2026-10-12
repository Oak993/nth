using Marrow.Core.Models;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (web surfaces W25, ED-NEW-web-w25-1): the chat's side of ask_user, scripted by the test. Every card asked is kept in
/// <see cref="Asked"/>; the wait ends when the test calls <see cref="Answer"/>, <see cref="Skip"/> or <see cref="Stop"/>, or when the
/// wait's cancellation token is cancelled (a <see cref="AskOutcomeKind.Detached"/> outcome, as the real host gives).
/// </summary>
public sealed class FakeAskUserHost : IAskUserHost
{
    private readonly List<TaskCompletionSource<AskOutcome>> waits = [];

    public List<AskCard> Asked { get; } = [];

    public int Waiting => waits.Count(wait => !wait.Task.IsCompleted);

    public async Task<AskOutcome> Ask(AskCard card, CancellationToken cancellation)
    {
        var wait = new TaskCompletionSource<AskOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (waits) { Asked.Add(card); waits.Add(wait); }
        await using var registration = cancellation.Register(() => wait.TrySetResult(new AskOutcome(AskOutcomeKind.Detached, card)));
        return await wait.Task.ConfigureAwait(false);
    }

    public void Answer(int index, params (string Question, string Answer)[] answers)
    {
        var card = Asked[index];
        var map = new ValueMapBuilder();
        foreach (var (question, answer) in answers) map.Add(question, answer);
        Finish(index, new AskOutcome(AskOutcomeKind.Answered, card.Closed(AskState.Answered, map.Build())));
    }

    public void Skip(int index) => Finish(index, new AskOutcome(AskOutcomeKind.Skipped, Asked[index].Closed(AskState.Skipped)));

    public void Stop(int index) => Finish(index, new AskOutcome(AskOutcomeKind.Stopped, Asked[index].Closed(AskState.Stopped)));

    private void Finish(int index, AskOutcome outcome)
    {
        lock (waits) waits[index].TrySetResult(outcome);
    }

    /// <summary>Waits until <paramref name="count"/> cards have been asked.</summary>
    public async Task WaitForAsked(int count)
    {
        for (var attempt = 0; attempt < 400 && Asked.Count < count; attempt++) await Task.Delay(10);
        Assert.True(Asked.Count >= count, "The card was never asked.");
    }

    private sealed class ValueMapBuilder
    {
        private readonly List<KeyValuePair<string, Marrow.Core.Port.ValueList<string>>> pairs = [];

        public void Add(string question, string answer) => pairs.Add(KeyValuePair.Create(question, new Marrow.Core.Port.ValueList<string>([answer])));

        public Marrow.Core.Port.ValueMap<string, Marrow.Core.Port.ValueList<string>> Build() => new(pairs, StringComparer.Ordinal);
    }
}

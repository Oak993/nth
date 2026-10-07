// PORT-ONLY: MA-3b-E1 / OD-0115 opinions=B. Explicit per-message fan-out, with a single synthesis by the original account.
using System.Globalization;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.Services.Port.MultiAccount;

public sealed class OpinionsEngine : IOpinionsPort, IDisposable
{
    private sealed class Preparation(Guid thread, ConversationHeadAccount source, ConversationHeadAccount[] targets, long revision, CancellationToken ct)
    {
        public Guid Thread { get; } = thread;
        public ConversationHeadAccount Source { get; } = source;
        public ConversationHeadAccount[] Targets { get; } = targets;
        public long Revision { get; } = revision;
        public CancellationTokenSource Cancel { get; } = CancellationTokenSource.CreateLinkedTokenSource(ct);
    }

    private sealed class HeadState(ConversationHeadAccount account, CancellationToken ct)
    {
        public ConversationHeadAccount Account { get; } = account;
        public CancellationTokenSource Cancel { get; } = CancellationTokenSource.CreateLinkedTokenSource(ct);
        public string Text { get; set; } = "";
        public bool Done { get; set; }
        public string? Error { get; set; }
    }

    private sealed class RoundState(Preparation preparation, string message, string context)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Preparation Preparation { get; } = preparation;
        public string Message { get; } = message;
        public string Context { get; } = context;
        public List<HeadState> Heads { get; } = new[] { preparation.Source }.Concat(preparation.Targets)
            .Select(account => new HeadState(account, preparation.Cancel.Token)).ToList();
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completion => Finished.Task;
        public bool Combining { get; set; }
        public bool Combined { get; set; }
        public bool PostAttempted { get; set; }
    }

    private readonly IConversationHeadHost host;
    private readonly object gate = new();
    private readonly List<Preparation> preparing = [];
    private readonly List<RoundState> rounds = [];
    private bool disposed;

    public OpinionsEngine(IConversationHeadHost host) { this.host = host; host.AccountsChanged += AccountsChanged; }
    public event Action<Guid>? Changed;

    public IReadOnlyList<Guid> EveryoneWithRoom(Guid thread) => Array.AsReadOnly(host.Accounts(thread)
        .Where(account => account.Id != host.ActiveAccount(thread) && account.Enabled && account.HasRoom).Select(account => account.Id).Distinct().ToArray());

    public async Task<Guid> AskAsync(Guid thread, string message, IReadOnlyList<Guid> accounts, CancellationToken ct = default)
    {
        if (accounts.Count == 0) throw new ArgumentException("At least one other account is needed.", nameof(accounts));
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A message is required.", nameof(message));
        Preparation preparation;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var rows = host.Accounts(thread);
            var source = rows.FirstOrDefault(account => account.Id == host.ActiveAccount(thread) && account.Enabled && account.HasRoom)
                ?? throw new InvalidOperationException("The answering account is unavailable.");
            var targets = accounts.Distinct().Select(id => rows.FirstOrDefault(account => account.Id == id && id != source.Id && account.Enabled && account.HasRoom)
                ?? throw new InvalidOperationException("An opinion account is unavailable.")).ToArray();
            preparation = new(thread, source, targets, host.Revision(thread), ct);
            preparing.Add(preparation);
        }
        try
        {
            var token = preparation.Cancel.Token;
            if (!await host.ConfirmTransferAsync(thread, preparation.Source, preparation.Targets, token).WaitAsync(token))
                throw new OperationCanceledException("The conversation transfer was declined.");
            // A changed target is a rejection before any fan-out; no subset is silently substituted.
            EnsureAvailable(preparation);
            var context = await host.ContextAsync(thread, token).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            EnsureAvailable(preparation);
            RoundState round;
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                EnsureAvailable(preparation);
                round = new(preparation, message, context);
                rounds.Add(round);
                preparing.Remove(preparation);
            }
            _ = RunRoundAsync(round);
            Changed?.Invoke(thread);
            return round.Id;
        }
        catch
        {
            lock (gate) preparing.Remove(preparation);
            preparation.Cancel.Cancel();
            throw;
        }
    }

    public IReadOnlyList<OpinionRound> Rounds(Guid thread)
    {
        lock (gate) return Array.AsReadOnly(rounds.Where(round => round.Preparation.Thread == thread).Select(round =>
            new OpinionRound(round.Id, thread, round.Message, Array.AsReadOnly(round.Heads.Select(head =>
                new OpinionAnswer(head.Account.Id, head.Text, head.Done) { Error = head.Error }).ToArray()), round.Combined)).ToArray());
    }

    public IReadOnlyDictionary<Guid, string> Errors(Guid thread, Guid round)
    {
        lock (gate) return new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, string>(Find(thread, round).Heads
            .Where(head => head.Error is not null).ToDictionary(head => head.Account.Id, head => head.Error!));
    }

    public Task WaitAsync(Guid thread, Guid round) { lock (gate) return Find(thread, round).Completion; }
    public void Cancel(Guid thread, Guid round) { RoundState state; lock (gate) state = Find(thread, round); state.Preparation.Cancel.Cancel(); }

    private async Task RunRoundAsync(RoundState round)
    {
        try
        {
            await Task.WhenAll(round.Heads.Select((head, index) => RunHeadAsync(round, head, index)));
            round.Finished.TrySetResult();
        }
        catch (Exception error) { round.Finished.TrySetException(error); }
    }

    private async Task RunHeadAsync(RoundState round, HeadState head, int index)
    {
        try
        {
            var ct = head.Cancel.Token;
            ct.ThrowIfCancellationRequested();
            if (!Permitted(round.Preparation, head)) throw new InvalidOperationException("The account is no longer available.");
            var answer = await host.RunAsync(new(round.Preparation.Thread, head.Account,
                index == 0 ? ConversationHeadPurpose.Answer : ConversationHeadPurpose.Opinion, round.Context, round.Message),
                text => Update(round, head, text), () => Permitted(round.Preparation, head), ct).WaitAsync(ct);
            lock (gate)
            {
                ct.ThrowIfCancellationRequested();
                if (!Permitted(round.Preparation, head) || string.IsNullOrWhiteSpace(answer))
                    throw new InvalidOperationException("The account did not return an authorized answer.");
                head.Text = answer; head.Done = true;
            }
        }
        catch (Exception error)
        {
            lock (gate) { head.Done = true; head.Error = error is OperationCanceledException ? "Opinion cancelled." : error.Message; }
        }
        Changed?.Invoke(round.Preparation.Thread);
    }

    public async Task CombineAsync(Guid thread, Guid round, CancellationToken ct = default)
    {
        RoundState state;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            state = Find(thread, round);
            if (state.Combined || state.Combining || state.PostAttempted || !state.Completion.IsCompleted
                || state.Heads.Any(head => !head.Done || head.Error is not null || string.IsNullOrWhiteSpace(head.Text)))
                throw new InvalidOperationException("This round cannot be combined.");
            EnsureAvailable(state.Preparation);
            state.Combining = true;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, state.Preparation.Cancel.Token);
        try
        {
            var token = linked.Token;
            var prompt = "Combine these answers into one response to the user's message. Take no actions.\nUser message:\n" + state.Message
                + "\nAnswers:\n" + string.Join("\n\n", state.Heads.Select(head => head.Account.Name + ":\n" + head.Text));
            var answer = await host.RunAsync(new(thread, state.Preparation.Source, ConversationHeadPurpose.Combine, state.Context, prompt),
                _ => { }, () => SourcePermitted(state.Preparation) && !token.IsCancellationRequested, token).WaitAsync(token);
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                EnsureAvailable(state.Preparation);
                if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("The account returned no combined answer.");
                // Once append is attempted it cannot be retried: a host error may follow a committed append.
                state.PostAttempted = true;
            }
            var byline = state.Preparation.Source.Name + " \U000000B7 combined " + state.Heads.Count.ToString(CultureInfo.InvariantCulture) + " answers";
            await host.PostCombinedAsync(thread, state.Id, answer, byline, token);
            lock (gate) state.Combined = true;
            Changed?.Invoke(thread);
        }
        finally { lock (gate) state.Combining = false; }
    }

    private void Update(RoundState round, HeadState head, string text)
    {
        lock (gate) { if (head.Done || !Permitted(round.Preparation, head)) return; head.Text = text; }
        Changed?.Invoke(round.Preparation.Thread);
    }

    private RoundState Find(Guid thread, Guid id) => rounds.FirstOrDefault(round => round.Id == id && round.Preparation.Thread == thread)
        ?? throw new InvalidOperationException("The opinion round is unavailable.");

    private bool Available(Guid thread, ConversationHeadAccount account) => host.Accounts(thread)
        .Any(row => row.Id == account.Id && row.Service == account.Service && row.Enabled && row.HasRoom);

    private bool SourcePermitted(Preparation preparation)
    {
        lock (gate) return !disposed && !preparation.Cancel.IsCancellationRequested && host.ActiveAccount(preparation.Thread) == preparation.Source.Id
            && host.Revision(preparation.Thread) == preparation.Revision
            && Available(preparation.Thread, preparation.Source);
    }

    private bool Permitted(Preparation preparation, HeadState head) => SourcePermitted(preparation)
        && !head.Cancel.IsCancellationRequested && Available(preparation.Thread, head.Account);

    private void EnsureAvailable(Preparation preparation)
    {
        if (!SourcePermitted(preparation) || preparation.Targets.Any(account => !Available(preparation.Thread, account)))
            throw new InvalidOperationException("Permission to ask these accounts changed.");
    }

    private void AccountsChanged(Guid thread)
    {
        Preparation[] pending;
        RoundState[] live;
        lock (gate) { pending = preparing.Where(p => p.Thread == thread).ToArray(); live = rounds.Where(r => r.Preparation.Thread == thread).ToArray(); }
        foreach (var p in pending)
            if (!SourcePermitted(p) || p.Targets.Any(account => !Available(thread, account))) p.Cancel.Cancel();
        foreach (var round in live)
        {
            if (!SourcePermitted(round.Preparation)) round.Preparation.Cancel.Cancel();
            foreach (var head in round.Heads.Where(head => !Permitted(round.Preparation, head))) head.Cancel.Cancel();
        }
    }

    public void Dispose()
    {
        Preparation[] pending;
        lock (gate) { if (disposed) return; disposed = true; pending = preparing.Concat(rounds.Select(r => r.Preparation)).ToArray(); }
        host.AccountsChanged -= AccountsChanged;
        foreach (var p in pending) p.Cancel.Cancel();
    }
}

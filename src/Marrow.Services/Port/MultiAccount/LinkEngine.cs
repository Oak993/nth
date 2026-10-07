// PORT-ONLY: MA-3b-E1 / OD-0115 link=A. Linking is explicit; it never changes routing or performs a model turn.
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.Services.Port.MultiAccount;

public sealed record LinkCheckAnswer(Guid AccountId, string Text, bool Done, string? Error);
public sealed record LinkCheck(Guid AnswerId, Guid AnswerAccount, string Answer, IReadOnlyList<LinkCheckAnswer> Answers);

public sealed class LinkEngine : ILinkPort, IDisposable
{
    private sealed class CheckState(Guid thread, Guid answerId, ConversationHeadAccount source, long revision, bool requireSourceRoom, string answer,
        IReadOnlyList<ConversationHeadAccount> targets, CancellationTokenSource cancel)
    {
        public Guid Thread { get; } = thread;
        public Guid Source { get; } = source.Id;
        public ConversationHeadAccount SourceAccount { get; } = source;
        public long Revision { get; } = revision;
        public bool RequireSourceRoom { get; } = requireSourceRoom;
        public Guid AnswerId { get; } = answerId;
        public string Answer { get; } = answer;
        public CancellationTokenSource Cancel { get; } = cancel;
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<CheckHead> Heads { get; } = targets.Select(account => new CheckHead(account, cancel.Token)).ToList();
    }

    private sealed class CheckHead(ConversationHeadAccount account, CancellationToken ct)
    {
        public ConversationHeadAccount Account { get; } = account;
        public CancellationTokenSource Cancel { get; } = CancellationTokenSource.CreateLinkedTokenSource(ct);
        public string Text { get; set; } = "";
        public bool Done { get; set; }
        public string? Error { get; set; }
    }

    private readonly IConversationHeadHost host;
    private readonly object gate = new();
    private readonly Dictionary<Guid, List<Guid>> links = [];
    private readonly List<CheckState> checks = [];
    private bool disposed;

    public LinkEngine(IConversationHeadHost host) { this.host = host; host.AccountsChanged += AccountsChanged; }
    public event Action<Guid>? Changed;

    public IReadOnlyList<Guid> Linked(Guid thread)
    {
        // Keep explicit tap intent internally; the public link state cannot show an active/out/off account.
        lock (gate) return Array.AsReadOnly(links.GetValueOrDefault(thread)?.Where(id => Eligible(thread, id)).ToArray() ?? []);
    }

    public void Link(Guid thread, Guid account)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!Eligible(thread, account)) return;
            if (!links.TryGetValue(thread, out var ids)) links[thread] = ids = [];
            if (ids.Contains(account)) return;
            ids.Add(account);
        }
        Changed?.Invoke(thread);
    }

    public void Unlink(Guid thread, Guid account)
    {
        bool changed;
        lock (gate) changed = links.TryGetValue(thread, out var ids) && ids.Remove(account);
        CancelUnavailable(thread);
        if (changed) Changed?.Invoke(thread);
    }

    public IReadOnlyList<LinkCheck> Checks(Guid thread)
    {
        lock (gate) return Array.AsReadOnly(checks.Where(check => check.Thread == thread).Select(check =>
            new LinkCheck(check.AnswerId, check.Source, check.Answer, Array.AsReadOnly(check.Heads.Select(head =>
                new LinkCheckAnswer(head.Account.Id, head.Text, head.Done, head.Error)).ToArray()))).ToArray());
    }

    /// <summary>Called once for each completed primary answer. The answer identity prevents duplicate checks.</summary>
    public Task CheckCompletedAnswerAsync(Guid thread, Guid answerId, Guid answerAccount, string message, string answer, CancellationToken ct = default)
        => StartCheck(thread, answerId, answerAccount, message, answer, requireSourceRoom: false, ct);

    /// <summary>Checks an answer only while the source is available.</summary>
    public Task CheckAnswerAsync(Guid thread, Guid answerId, Guid answerAccount, string message, string answer, CancellationToken ct = default)
        => StartCheck(thread, answerId, answerAccount, message, answer, requireSourceRoom: true, ct);

    private Task StartCheck(Guid thread, Guid answerId, Guid answerAccount, string message, string answer, bool requireSourceRoom, CancellationToken ct)
    {
        CheckState state;
        ConversationHeadAccount source;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (checks.FirstOrDefault(check => check.Thread == thread && check.AnswerId == answerId) is { } existing) return existing.Done.Task;
            source = host.Accounts(thread).FirstOrDefault(row => row.Id == answerAccount && row.Enabled && (!requireSourceRoom || row.HasRoom))!;
            if (source is null || host.ActiveAccount(thread) != answerAccount) return Task.CompletedTask;
            var targets = (links.GetValueOrDefault(thread) ?? []).Where(id => Eligible(thread, id))
                .Select(id => host.Accounts(thread).First(row => row.Id == id)).ToArray();
            if (targets.Length == 0) return Task.CompletedTask;
            // A completed author may have just spent its final quota; only the checking targets receive model turns here.
            state = new(thread, answerId, source, host.Revision(thread), requireSourceRoom, answer, targets, CancellationTokenSource.CreateLinkedTokenSource(ct));
            checks.Add(state);
        }
        _ = CheckAsync(state, source, message);
        return state.Done.Task;
    }

    private async Task CheckAsync(CheckState state, ConversationHeadAccount source, string message)
    {
        try
        {
            var ct = state.Cancel.Token;
            if (!await host.ConfirmTransferAsync(state.Thread, source, state.Heads.Select(head => head.Account).ToArray(), ct).WaitAsync(ct))
                throw new OperationCanceledException("The conversation transfer was declined.");
            var context = await host.ContextAsync(state.Thread, ct).WaitAsync(ct);
            await Task.WhenAll(state.Heads.Select(async head =>
            {
                try
                {
                    var token = head.Cancel.Token;
                    token.ThrowIfCancellationRequested();
                    if (!Permitted(state, head)) throw new InvalidOperationException("The checking account is no longer available.");
                    var text = await host.RunAsync(new(state.Thread, head.Account, ConversationHeadPurpose.Check, context, message, state.Answer),
                        value => Update(state, head, value), () => Permitted(state, head), token).WaitAsync(token);
                    lock (gate)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!Permitted(state, head) || string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("The account did not return an authorized check.");
                        head.Text = text; head.Done = true;
                    }
                }
                catch (Exception error) { Fail(head, error); }
                Changed?.Invoke(state.Thread);
            }));
        }
        catch (Exception error)
        {
            foreach (var head in state.Heads) Fail(head, error);
            Changed?.Invoke(state.Thread);
        }
        finally
        {
            // Keep cancellation sources until engine disposal: captured late callbacks can still read their token safely.
            state.Done.TrySetResult();
        }
    }

    private void Update(CheckState state, CheckHead head, string text)
    {
        lock (gate) { if (head.Done || !Permitted(state, head)) return; head.Text = text; }
        Changed?.Invoke(state.Thread);
    }

    private void Fail(CheckHead head, Exception error)
    { lock (gate) { if (head.Done) return; head.Done = true; head.Error = error is OperationCanceledException ? "Check cancelled." : error.Message; } }

    private bool Eligible(Guid thread, Guid id) => id != host.ActiveAccount(thread)
        && host.Accounts(thread).Any(row => row.Id == id && row.Enabled && row.HasRoom);

    private bool Permitted(CheckState state, CheckHead head)
    {
        lock (gate) return !disposed && !state.Cancel.IsCancellationRequested && !head.Cancel.IsCancellationRequested
            && host.ActiveAccount(state.Thread) == state.Source
            && host.Revision(state.Thread) == state.Revision
            && host.Accounts(state.Thread).Any(row => row.Id == state.Source && row.Service == state.SourceAccount.Service
                && row.Enabled && (!state.RequireSourceRoom || row.HasRoom))
            && links.TryGetValue(state.Thread, out var ids) && ids.Contains(head.Account.Id) && Eligible(state.Thread, head.Account.Id)
            && host.Accounts(state.Thread).Any(row => row.Id == head.Account.Id && row.Service == head.Account.Service);
    }

    private void AccountsChanged(Guid thread) { CancelUnavailable(thread); Changed?.Invoke(thread); }

    private void CancelUnavailable(Guid thread)
    {
        CheckState[] states;
        lock (gate) states = checks.Where(check => check.Thread == thread && !check.Done.Task.IsCompleted).ToArray();
        foreach (var state in states)
        {
            foreach (var head in state.Heads.Where(head => !Permitted(state, head))) head.Cancel.Cancel();
            if (state.Heads.All(head => head.Cancel.IsCancellationRequested)) state.Cancel.Cancel();
        }
    }

    public void Dispose()
    {
        CheckState[] states;
        lock (gate) { if (disposed) return; disposed = true; states = checks.ToArray(); }
        host.AccountsChanged -= AccountsChanged;
        foreach (var state in states) state.Cancel.Cancel();
    }
}

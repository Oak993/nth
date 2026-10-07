using Marrow.Core.Models;
using Marrow.Services.Port.MultiAccount;

namespace Marrow.Services.Tests.Fixtures;

public sealed class ConversationHeadHarness : IConversationHeadHost
{
    public Guid Thread { get; } = Guid.NewGuid();
    public Guid Active { get; set; } = Guid.NewGuid();
    public long Version { get; private set; }
    public List<ConversationHeadAccount> Rows { get; } = [];
    public List<ConversationHeadFlight> Flights { get; } = [];
    public List<(Guid Thread, Guid Round, string Text, string Byline)> Posts { get; } = [];
    public List<IReadOnlyList<ConversationHeadAccount>> Confirmations { get; } = [];
    public Func<CancellationToken, Task<bool>> Confirm { get; set; } = _ => Task.FromResult(true);
    public Task<string>? ContextWait { get; set; }
    public Action<ConversationHeadFlight>? OnRun { get; set; }
    public event Action<Guid>? AccountsChanged;

    public ConversationHeadHarness() => Rows.Add(new(Active, ProviderKind.Claude, "Claude Max 1", true, true));
    public ConversationHeadAccount Add(ProviderKind service = ProviderKind.Claude, bool enabled = true, bool room = true)
    {
        var row = new ConversationHeadAccount(Guid.NewGuid(), service, service == ProviderKind.Local ? "Local 1" : "Claude Pro", enabled, room);
        Rows.Add(row);
        return row;
    }
    public void Change(ConversationHeadAccount account) { Rows[Rows.FindIndex(row => row.Id == account.Id)] = account; AccountsChanged?.Invoke(Thread); }
    public void ChangeActive(Guid account) { Active = account; AccountsChanged?.Invoke(Thread); }
    public void ChangeRevision() { Version++; AccountsChanged?.Invoke(Thread); }
    public long Revision(Guid thread) => Version;
    public Guid ActiveAccount(Guid thread) => Active;
    public IReadOnlyList<ConversationHeadAccount> Accounts(Guid thread) => Rows.ToArray();
    public Task<string> ContextAsync(Guid thread, CancellationToken ct) => ContextWait?.WaitAsync(ct) ?? Task.FromResult("RECORDED CONVERSATION");
    public Task<bool> ConfirmTransferAsync(Guid thread, ConversationHeadAccount source, IReadOnlyList<ConversationHeadAccount> targets, CancellationToken ct)
    { Confirmations.Add(targets.ToArray()); return Confirm(ct); }
    public Task<string> RunAsync(ConversationHeadRequest request, Action<string> text, Func<bool> permitted, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!permitted()) throw new InvalidOperationException("Account is unavailable.");
        var flight = new ConversationHeadFlight(request, text, permitted, ct);
        Flights.Add(flight);
        OnRun?.Invoke(flight);
        return flight.Result.Task.WaitAsync(ct);
    }
    public Task PostCombinedAsync(Guid thread, Guid round, string text, string byline, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); Posts.Add((thread, round, text, byline)); return Task.CompletedTask; }
}

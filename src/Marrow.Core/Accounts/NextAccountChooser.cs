namespace Marrow.Core.Accounts;

/// <summary>The next-account choice (NOTES "Account popover and priority", "Pills", "Pills loop").</summary>
public abstract record ChooserResult
{
    private ChooserResult() { }

    /// <summary>Move to this account (inside a pill any service; outside a pill the same service).</summary>
    public sealed record Move(SwitchTarget Target) : ChooserResult;

    /// <summary>Every account of the service is out and another service has one: outside a pill this is always asked. <see cref="EarliestReset"/> is when the service's own earliest account is back, for "wait".</summary>
    public sealed record OtherService(SwitchTarget Target, DateTimeOffset? EarliestReset) : ChooserResult;

    /// <summary>None is available: wait for the earliest reset (null when none is known).</summary>
    public sealed record WaitFor(DateTimeOffset? EarliestReset) : ChooserResult;

    /// <summary>There is no enabled account to wait for.</summary>
    public sealed record NoAccounts : ChooserResult;
}

public sealed class NextAccountChooser(IAccountStore store, IAccountLimits limits, TimeProvider clock)
{
    /// <summary>Where the thread goes when <paramref name="currentAccountId"/> runs out.</summary>
    public ChooserResult Next(string currentAccountId, Pill? pill, int? stepIndex)
    {
        if (pill is { Steps.Count: > 0 }) return NextInPill(currentAccountId, pill, stepIndex);

        var accounts = store.Accounts;
        var current = accounts.FirstOrDefault(a => a.Id == currentAccountId);
        var sameService = accounts.Where(a => current is null || a.Service == current.Service).ToList();
        var position = sameService.FindIndex(a => a.Id == currentAccountId);
        var ranked = sameService.Select((a, i) => (Account: a, Index: i)).Where(x => x.Account.Id != currentAccountId && IsAvailable(x.Account)).ToList();
        var pick = ranked.FirstOrDefault(x => x.Index > position);
        if (pick.Account is null) pick = ranked.FirstOrDefault();
        if (pick.Account is not null) return new ChooserResult.Move(new SwitchTarget(pick.Account));

        var earliest = EarliestReset(sameService.Where(a => a.Enabled));
        var other = accounts.FirstOrDefault(a => current is not null && a.Service != current.Service && IsAvailable(a));
        if (other is not null) return new ChooserResult.OtherService(new SwitchTarget(other), earliest);
        return sameService.Any(a => a.Enabled) ? new ChooserResult.WaitFor(earliest) : new ChooserResult.NoAccounts();
    }

    /// <summary>The highest-ranked available account ranked above the current one (pill step order, else priority order within the service), or null. <paramref name="isBack"/> is the caller's say on whether an available account has really come back.</summary>
    public SwitchTarget? EarlierBack(string currentAccountId, Pill? pill, int? stepIndex, Func<SwitchTarget, bool>? isBack = null)
    {
        if (pill is { Steps.Count: > 0 })
        {
            var at = CurrentStep(currentAccountId, pill, stepIndex);
            for (var i = 0; i < at; i++)
                if (PillStepAccountResolver.Resolve(store, pill.Steps[i]) is { } account && IsAvailable(account)
                    && new SwitchTarget(account, i, pill.Effective(i)) is var target && isBack?.Invoke(target) != false) return target;
            return null;
        }

        var accounts = store.Accounts;
        var current = accounts.FirstOrDefault(a => a.Id == currentAccountId);
        if (current is null) return null;
        foreach (var account in accounts.TakeWhile(a => a.Id != currentAccountId))
            if (account.Service == current.Service && IsAvailable(account) && new SwitchTarget(account) is var target && isBack?.Invoke(target) != false) return target;
        return null;
    }

    private ChooserResult NextInPill(string currentAccountId, Pill pill, int? stepIndex)
    {
        var at = CurrentStep(currentAccountId, pill, stepIndex);
        var steps = pill.Steps.Select((step, i) => (Step: step, Index: i, Account: PillStepAccountResolver.Resolve(store, step)))
            .Where(x => x.Account is { Enabled: true }).ToList();
        var ready = steps.Where(x => x.Index != at && IsAvailable(x.Account!)).ToList();
        // After the current step first; otherwise the loop: the highest-ranked step that is available again.
        var pick = ready.FirstOrDefault(x => x.Index > at);
        if (pick.Account is null) pick = ready.FirstOrDefault();
        if (pick.Account is not null) return new ChooserResult.Move(new SwitchTarget(pick.Account, pick.Index, pill.Effective(pick.Index)));
        return steps.Count == 0 ? new ChooserResult.NoAccounts() : new ChooserResult.WaitFor(EarliestReset(steps.Select(x => x.Account!)));
    }

    private static int CurrentStep(string currentAccountId, Pill pill, int? stepIndex)
    {
        if (stepIndex is { } index && index >= 0 && index < pill.Steps.Count) return index;
        var found = pill.Steps.ToList().FindIndex(s => string.Equals(s.AccountIdentity, currentAccountId, StringComparison.OrdinalIgnoreCase));
        return found;
    }

    private bool IsAvailable(AccountInfo account) =>
        account.Enabled && !(limits.Usage(account.Id)?.IsExhaustedAt(clock.GetUtcNow()) ?? false);

    private DateTimeOffset? EarliestReset(IEnumerable<AccountInfo> accounts) =>
        accounts.Select(a => limits.Usage(a.Id)?.ResetsAt).Where(r => r is not null).Min();
}

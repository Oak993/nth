namespace Marrow.Core.Accounts;

/// <summary>The next-account choice (NOTES "Account popover and priority", "Pills", "Pills loop").</summary>
public abstract record ChooserResult
{
    private ChooserResult() { }

    /// <summary>Move to this account (inside a pill any service; outside a pill the same service).</summary>
    public sealed record Move(SwitchTarget Target) : ChooserResult;

    /// <summary>Every account of the service is out and another service has one: outside a pill this is always asked.</summary>
    public sealed record OtherService(SwitchTarget Target) : ChooserResult;

    /// <summary>None is available: wait for the earliest reset (null when none is known).</summary>
    public sealed record WaitFor(DateTimeOffset? EarliestReset) : ChooserResult;

    /// <summary>There is no enabled account to wait for.</summary>
    public sealed record NoAccounts : ChooserResult;
}

public sealed class NextAccountChooser(IAccountStore store, IAccountLimits limits, TimeProvider clock)
{
    /// <summary>Where the thread goes when <paramref name="currentAccountId"/> runs out.</summary>
    public ChooserResult Next(string currentAccountId, Pill? pill, int? stepIndex) => throw new NotImplementedException();

    /// <summary>The highest-ranked available account ranked above the current one (pill step order, else priority order within the service), or null.</summary>
    public SwitchTarget? EarlierBack(string currentAccountId, Pill? pill, int? stepIndex) => throw new NotImplementedException();
}

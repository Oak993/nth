// PORT-ONLY: multi-account steps 3b-5, job 0. One account as the thread's popover (and the cards and usage panel) draw it: plain data over the account store and the usage the service reported.
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Core.Port.MultiAccount.Shared;

namespace Marrow.Core.Port.MultiAccount.Thread;

/// <param name="Number">Place in the service's order, from 1.</param>
/// <param name="CustomName">The name is one the user gave (its monogram takes initials).</param>
/// <param name="Usage">What the service reported; null when it reported nothing.</param>
public sealed record ThreadAccount(Guid Id, ProviderKind Service, int Number, string Name, bool CustomName, string Mono, string? Plan, bool Enabled, AccountUsageLine? Usage, AccountDefaults Defaults)
{
    /// <summary>A local model has no limit at all ("No limit · Stays on this PC"), and so does any account the service says so of.</summary>
    public bool NoLimit => Service == ProviderKind.Local || Usage is { NoLimit: true };

    /// <summary>The 5-hour window or the week is used up (the Models page's rule).</summary>
    public bool IsOut => !NoLimit && Usage is { } line && (line.FiveHour >= 1 || line.Week >= 1);

    /// <summary>The service says the account has no 5-hour window; never read from the plan's name.</summary>
    public bool NoFiveHour => NoFiveHourText.For(Usage) is not null;
}

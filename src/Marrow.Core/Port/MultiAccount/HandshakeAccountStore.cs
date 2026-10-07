// PORT-ONLY: OD-0114; ED-403. MA-1 metadata projected into the MA-5 routing contract.
using System.Text.RegularExpressions;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;

namespace Marrow.Core.Port.MultiAccount;

public sealed class HandshakeAccountStore(AccountStore accounts, Func<Account, bool> connected) : IAccountStore, IPillStepAccountStore
{
    public IReadOnlyList<AccountInfo> Accounts => accounts.Accounts.Where(connected)
        .OrderBy(a => (int)a.Service).ThenBy(a => a.Priority)
        .Select(a => new AccountInfo(a.Id.ToString("D"), a.Service.Id, a.CustomName ?? accounts.DefaultName(a.Id),
            a.Enabled, Plan(a.Plan), Point(a.Defaults.HandshakePoint))).ToArray();

    public AccountInfo? ResolvePillStep(PillStep step)
    {
        var rows = accounts.Accounts;
        var metadata = rows.Select(a => new AccountInfo(a.Id.ToString("D"), a.Service.Id,
            a.CustomName ?? accounts.DefaultName(a.Id), a.Enabled, Plan(a.Plan), Point(a.Defaults.HandshakePoint))).ToArray();
        var resolved = PillStepAccountResolver.Resolve(metadata, step);
        return resolved is not null && Guid.TryParse(resolved.Id, out var id)
            && rows.FirstOrDefault(row => row.Id == id) is { } selected && connected(selected) ? resolved : null;
    }

    private static PlanTier Plan(string? name) => string.IsNullOrWhiteSpace(name) ? PlanTier.Unknown
        : Regex.IsMatch(name, @"(?<!\d)(5|10|20)\s*[x×](?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            ? PlanTier.Large : PlanTier.Standard;

    private static Marrow.Core.Accounts.HandshakePoint? Point(Marrow.Core.Port.Accounts.HandshakePoint? point) =>
        point is null ? null : point.Percentage is { } percent
            ? Marrow.Core.Accounts.HandshakePoint.At(percent) : Marrow.Core.Accounts.HandshakePoint.Smart;
}

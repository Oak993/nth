// PORT-ONLY: T11 step 2. What the Models page asks the app to do that is not a stored setting: sign another account in, and sign one out. The app binds it to the provider registry; a test records it.
using Marrow.Core.Models;

namespace Marrow.Core.Port.MultiAccount.Settings;

public interface IAccountActions
{
    /// <summary>Adds an account of the service and signs it in. Null when it worked; otherwise one plain line that says why not.</summary>
    Task<string?> AddAccountAsync(ProviderKind service);

    Task SignOutAsync(Guid accountId);

    /// <summary>Adds a local server by address, looks for it and adds the models it offers as local entries.</summary>
    Task<LocalConnectResult> ConnectLocalAsync(string address);
}

/// <summary>What the service reports for one account: the 5-hour window and the week as fractions (0 to 1) and when each resets. A window the plan does not have is flagged, never faked.</summary>
public sealed record AccountUsageLine(double? FiveHour, DateTimeOffset? FiveHourReset, double? Week, DateTimeOffset? WeekReset, bool NoFiveHour, bool NoLimit);

public enum LocalConnectOutcome
{
    Found,
    NotFound,
    BadAddress,
}

/// <summary>What looking for a local server found. <see cref="Where"/> is "this PC" for a server on this machine, else its host.</summary>
public sealed record LocalConnectResult(LocalConnectOutcome Outcome, int Models, string Where,
    Marrow.Core.Port.Local.LocalServerKind Kind = Marrow.Core.Port.Local.LocalServerKind.Unknown);

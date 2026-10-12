// PORT-ONLY: T11 step 2. What Settings > Providers asks the app to do that is not a stored setting: sign another account in, sign one out, and keep or remove an API key (ED-NEW-providersmerge-1). The app binds it to the provider registry; a test records it.
using Marrow.Core.Models;

namespace Marrow.Core.Port.MultiAccount.Settings;

public interface IAccountActions
{
    /// <summary>Adds an account of the service and signs it in. Null when it worked; otherwise one plain line that says why not.</summary>
    Task<string?> AddAccountAsync(ProviderKind service);

    Task SignOutAsync(Guid accountId);

    /// <summary>Adds a local server by address, looks for it and adds the models it offers as local entries.</summary>
    Task<LocalConnectResult> ConnectLocalAsync(string address);

    /// <summary>The key kept for an account of an API-key service (the original account's in its old place, a further key's beside it in the Credential Manager), or null when it has none.</summary>
    string? KeySecret(Guid accountId);

    /// <summary>Keeps <paramref name="secret"/> as the account's key. Null when it was kept; otherwise one plain line that says why not.</summary>
    Task<string?> StoreKeyAsync(Guid accountId, string secret);

    /// <summary>Removes the account's key from where it is kept (the account itself stays; the model removes a further key's account).</summary>
    Task ForgetKeyAsync(Guid accountId);
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

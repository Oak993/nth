// PORT-ONLY: OD-0116, OD-0122, ED-439. What the host needs from a standby whose live session becomes the conversation's session at the switch.
using Marrow.Core.Accounts;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.MultiAccount;

public interface IAdoptableStandby : IStandbySession
{
    SessionConfiguration Configuration { get; }
    string SessionId { get; }
    HandoffPackage? ReceivedPackage { get; }
    bool ReadBackRequested { get; }

    /// <summary>Last checks and the unlock: the session may now take the user's turns.</summary>
    Task PrepareForAdoptionAsync(CancellationToken ct);

    /// <summary>Hands the live session to the runtime; the standby no longer owns it.</summary>
    ProviderSession TakeSession();
}

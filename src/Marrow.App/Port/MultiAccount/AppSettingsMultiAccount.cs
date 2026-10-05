using Marrow.Core.Accounts.Pills;
using Marrow.Core.Port.MultiAccount;

namespace Marrow.App;

public sealed partial class AppSettings
{
    private PillStore? pills;
    private AccountDecisionStore? accountDecisions;
    public PillStore Pills => pills ??= new(new PillStorage(defaults));
    public AccountDecisionStore AccountDecisions
    {
        get
        {
            if (accountDecisions is not null) return accountDecisions;
            try { return accountDecisions = new(defaults); }
            catch (InvalidDataException) { return accountDecisions = AccountDecisionStore.ReadOnly(defaults); }
        }
    }
    // OD-0116: spending on a standby is off until the user explicitly enables it.
    public bool AccountHandshakeEnabled
    {
        get => defaults.Object("multiAccountHandshake") is true;
        set => defaults.Set("multiAccountHandshake", value);
    }
}

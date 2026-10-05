using Marrow.Core.Accounts.Pills;
using Marrow.Core.Port.MultiAccount;

namespace Marrow.App;

public sealed partial class AppSettings
{
    private PillStore? pills;
    private AccountDecisionStore? accountDecisions;
    public PillStore Pills => pills ??= new(new PillStorage(defaults));
    public AccountDecisionStore AccountDecisions => accountDecisions ??= new(defaults);
}

// PORT-ONLY: SK-17. The existing ThreadRuntime CLI switch, with an async-flow factory seam for offline launch tests.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

internal static class CliSessionFactory
{
    // One construction seam shared with the account-bound launches (AccountSessionFactory.Constructor).
    public static Ambient<Func<SessionConfiguration, ProviderSession>> Current => Marrow.Services.Port.MultiAccount.AccountSessionFactory.Constructor;
}

using System.Runtime.CompilerServices;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY (R-TEST-10; ED-211): installs ProviderRegistry.ExecutableRootsForTesting before any test in this assembly
// runs, so no test can resolve, and therefore launch, a real installed provider CLI through a harness that forgot its
// own override. A module initializer runs once per process, before any test body (the ForgeStandIns precedent).

/// <summary>Installs the provider-CLI root guard for this test assembly.</summary>
public static class ProviderCliGuard
{
    [ModuleInitializer]
#pragma warning disable CA2255 // A module initializer in a test library is the point: it must run before any test resolves a CLI.
    internal static void Install()
#pragma warning restore CA2255
    {
        ProviderRegistry.ExecutableRootsForTesting = Marrow.Testing.ProviderCliRoots.Compute();
        Marrow.Services.Port.Local.LocalDiscovery.Factory.SetProcessDefault(() => new Marrow.Services.Port.Local.LocalDiscovery([])); // MA-3: never probe installed local models
    }
}

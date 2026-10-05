using System.Runtime.CompilerServices;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY (R-TEST-10; ED-211): installs ProviderRegistry.ExecutableRootsForTesting before any test in this assembly
// runs. AppModel reaches provider CLIs through ProviderRegistry (TextEngine, sessions, updaters); without this, a test
// that does not call AppModelHarness.PrepareBootstrap resolves the real installed CLI from PATH and runs it (2026-09-27:
// AppModelHydraMergeTests generated commit messages through the owner's signed-in CLI that way).

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

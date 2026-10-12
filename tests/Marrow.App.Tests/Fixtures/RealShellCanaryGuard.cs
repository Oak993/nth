using System.Runtime.CompilerServices;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a, "Canary", unit U10 (ED-NEW-inst-u10-2). The listing of nth's shortcuts on the real
// Desktop and in the real Start menu is taken once, before any test runs (read only); RealShellCanaryTests compares it at the end.
// This assembly holds the real COM shell-link round trip, which works in a temp folder only.
internal static class RealShellCanaryGuard
{
    [ModuleInitializer]
#pragma warning disable CA2255 // A module initializer in a test library is the point: the baseline must be taken before any test.
    internal static void Install() => RealShellCanary.Begin();
#pragma warning restore CA2255
}

/// <summary>The canary runs alone after every parallel collection has finished (xUnit runs the collections that disable parallelization last).</summary>
[CollectionDefinition("RealShellCanary", DisableParallelization = true)]
public sealed class RealShellCanaryCollection;

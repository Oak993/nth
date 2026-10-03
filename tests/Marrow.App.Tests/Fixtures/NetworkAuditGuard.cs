using System.Runtime.CompilerServices;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY (R-TEST-10): with MARROW_TEST_NETWORK_AUDIT set to a file path, this assembly's process default HTTP handler is the
// fail-closed audit handler (Marrow.Testing.NetworkAudit); unset, it does nothing.
internal static class NetworkAuditGuard
{
    [ModuleInitializer]
#pragma warning disable CA2255 // A module initializer in a test library is the point: it must run before any test.
    internal static void Install() => Marrow.Testing.NetworkAudit.InstallIfRequested();
#pragma warning restore CA2255
}

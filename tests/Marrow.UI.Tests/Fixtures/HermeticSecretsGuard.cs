using System.Runtime.CompilerServices;

namespace Marrow.UI.Tests.Fixtures;

// PORT-ONLY (R-TEST-10): no provider secret reaches a test in this assembly (Marrow.Testing.HermeticSecrets).
internal static class HermeticSecretsGuard
{
    [ModuleInitializer]
#pragma warning disable CA2255 // A module initializer in a test library is the point: it must run before any test.
    internal static void Install() => Marrow.Testing.HermeticSecrets.Clear();
#pragma warning restore CA2255
}
